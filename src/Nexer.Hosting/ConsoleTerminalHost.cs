using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using Nexer.Core;
using Nexer.Hosting.Platform;
using Nexer.Input;
using Nexer.Rendering;

namespace Nexer.Hosting;

/// <summary>
/// An <see cref="ITerminalHost"/> on the process console (Windows console / Windows Terminal, or a Unix tty).
/// Create with <see cref="StartAsync"/>; dispose to restore the terminal.
/// </summary>
/// <remarks>
/// <para>
/// Threading: <see cref="Present"/> copies the changed rows of the surface into a host-owned snapshot under a
/// short lock and returns without doing I/O. A background frame loop coalesces presents (at most one frame per
/// <see cref="ConsoleTerminalHostOptions.MinFrameInterval"/>), diffs the latest snapshot with
/// <see cref="VtRenderer"/> and writes it. A dedicated read loop decodes input into the bounded
/// <see cref="Input"/> channel and detects resizes.
/// </para>
/// <para>
/// The terminal is restored (modes, code pages, alternate screen, mouse, cursor) exactly once on disposal,
/// process exit, unhandled exception, or SIGINT/SIGTERM/SIGHUP/SIGQUIT.
/// </para>
/// </remarks>
public sealed class ConsoleTerminalHost : ITerminalHost
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly ITerminalPlatform platform;
    private readonly ConsoleTerminalHostOptions options;
    private readonly VtRenderer renderer;
    private readonly InputDecoder decoder;
    private readonly Channel<InputEvent> input;
    private readonly Channel<bool> wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });
    private readonly CancellationTokenSource stop = new();
    private readonly Lock snapshotGate = new();
    private readonly Lock writeGate = new();
    private readonly Lock sizeGate = new();
    private readonly byte[] restoreSequence;
    private readonly List<PosixSignalRegistration> signals = [];
    private readonly bool processHandlers;
    private SurfaceSnapshot? snapshot;
    private RenderCursor cursor;
    private bool dirty;
    private TerminalSize? pendingResize;
    private TerminalSize size;
    private long lastFrame;
    private int restored;
    private int disposed;
    private Task readLoop = Task.CompletedTask;
    private Task frameLoop = Task.CompletedTask;

    private ConsoleTerminalHost(ITerminalPlatform platform, ConsoleTerminalHostOptions options, bool processHandlers)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.InputCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MinFrameInterval, TimeSpan.Zero);
        this.platform = platform;
        this.options = options;
        this.processHandlers = processHandlers;
        Time = options.TimeProvider ?? TimeProvider.System;
        input = Channel.CreateBounded<InputEvent>(new BoundedChannelOptions(options.InputCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
        decoder = new InputDecoder(Post, options.Input, Time);
        size = Sanitize(platform.GetSize());
        renderer = new VtRenderer(size.Columns, size.Rows, new RendererOptions
        {
            SynchronizedOutput = options.SynchronizedOutput,
            Colors = options.Colors,
        });
        restoreSequence = BuildRestoreSequence(options);
    }

    /// <inheritdoc />
    public TerminalSize Size
    {
        get
        {
            lock (sizeGate) return size;
        }
    }

    /// <inheritdoc />
    public ChannelReader<InputEvent> Input => input.Reader;

    /// <inheritdoc />
    public TimeProvider Time { get; }

    /// <summary>
    /// Switches the process console into an interactive full-screen session (raw input, alternate screen,
    /// mouse/focus/paste reporting) and starts the input and frame loops.
    /// </summary>
    /// <exception cref="InvalidOperationException">Standard input or output is redirected (not a terminal).</exception>
    /// <exception cref="PlatformNotSupportedException">The console cannot process VT sequences.</exception>
    public static Task<ConsoleTerminalHost> StartAsync(ConsoleTerminalHostOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            throw new InvalidOperationException(
                "ConsoleTerminalHost requires an interactive terminal; standard input or output is redirected.");
        }

        ITerminalPlatform platform = OperatingSystem.IsWindows() ? new WindowsConsolePlatform() : new UnixTerminalPlatform();
        return Task.FromResult(Start(platform, options ?? new ConsoleTerminalHostOptions(), processHandlers: true));
    }

    internal static ConsoleTerminalHost Start(ITerminalPlatform platform, ConsoleTerminalHostOptions options, bool processHandlers)
    {
        var host = new ConsoleTerminalHost(platform, options, processHandlers);
        host.Begin();
        return host;
    }

    /// <inheritdoc />
    public void Present(IReadOnlyCellSurface surface, CursorState cursor)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        lock (snapshotGate)
        {
            if (snapshot is null || snapshot.Columns != surface.Columns || snapshot.Rows != surface.Rows)
            {
                snapshot = new SurfaceSnapshot(surface.Columns, surface.Rows);
            }

            snapshot.CopyFrom(surface);
            this.cursor = new RenderCursor(cursor.Visible, cursor.Column, cursor.Row);
            dirty = true;
        }

        wake.Writer.TryWrite(true);
    }

    /// <summary>Stops the loops and restores the terminal. The <see cref="Input"/> channel completes.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel();
        await WaitQuietly(frameLoop).ConfigureAwait(false);
        Restore();
        await WaitQuietly(readLoop).ConfigureAwait(false);
        input.Writer.TryComplete();
        foreach (PosixSignalRegistration registration in signals) registration.Dispose();
        if (processHandlers)
        {
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        }
    }

    private static async Task WaitQuietly(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Loop failures are already reported through the Input channel; disposal must still restore.
        }
    }

    private void Begin()
    {
        platform.Enter();
        try
        {
            if (processHandlers)
            {
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                foreach (PosixSignal signal in (PosixSignal[])[PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGHUP, PosixSignal.SIGQUIT])
                {
                    TryRegister(signal, _ => Restore());
                }

                if (!OperatingSystem.IsWindows())
                {
                    TryRegister(PosixSignal.SIGWINCH, context =>
                    {
                        context.Cancel = true;
                        CheckSize();
                    });
                }
            }

            lock (writeGate) platform.Write(BuildEnterSequence(options));
            readLoop = Task.Factory.StartNew(ReadLoop, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            frameLoop = Task.Run(FrameLoopAsync);
        }
        catch
        {
            stop.Cancel();
            Restore();
            throw;
        }
    }

    private void TryRegister(PosixSignal signal, Action<PosixSignalContext> handler)
    {
        try
        {
            signals.Add(PosixSignalRegistration.Create(signal, handler));
        }
        catch (PlatformNotSupportedException)
        {
            // Not every signal exists on every OS (e.g. SIGWINCH on Windows).
        }
    }

    private void OnProcessExit(object? sender, EventArgs e) => Restore();

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e) => Restore();

    /// <summary>Writes the reset sequences and restores the platform modes, exactly once.</summary>
    internal void Restore()
    {
        if (Interlocked.Exchange(ref restored, 1) != 0) return;
        bool locked = writeGate.TryEnter(TimeSpan.FromMilliseconds(500));
        try
        {
            platform.Write(restoreSequence);
        }
        catch (Exception)
        {
            // Best effort: the output may be gone (closed terminal); modes must still be restored below.
        }
        finally
        {
            if (locked) writeGate.Exit();
        }

        platform.Restore();
    }

    private void ReadLoop()
    {
        byte[] buffer = new byte[4096];
        Exception? error = null;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                TimeSpan wait = decoder.TimeUntilTimeout is { } due && due < PollInterval ? due : PollInterval;
                int count = platform.Read(buffer, wait);
                if (count < 0)
                {
                    decoder.Complete();
                    break;
                }

                if (count > 0) decoder.Feed(buffer.AsSpan(0, count));
                else decoder.ResolveTimeout();
                CheckSize();
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (ChannelClosedException)
        {
        }
        catch (Exception ex)
        {
            error = ex;
        }

        input.Writer.TryComplete(error);
    }

    private void CheckSize()
    {
        lock (sizeGate)
        {
            TerminalSize now = Sanitize(platform.GetSize());
            if (now == size) return;
            size = now;
            lock (snapshotGate)
            {
                pendingResize = now;
                dirty = true;
            }

            wake.Writer.TryWrite(true);
            Post(new ResizeEvent(now.Columns, now.Rows) { Timestamp = Time.GetTimestamp() });
        }
    }

    private void Post(InputEvent value)
    {
        if (input.Writer.TryWrite(value)) return;
        // Backpressure: block the reader rather than drop keys, paste or button transitions.
        input.Writer.WriteAsync(value, stop.Token).AsTask().GetAwaiter().GetResult();
    }

    private async Task FrameLoopAsync()
    {
        try
        {
            while (true)
            {
                await wake.Reader.ReadAsync(stop.Token).ConfigureAwait(false);
                if (lastFrame != 0)
                {
                    TimeSpan remaining = options.MinFrameInterval - Time.GetElapsedTime(lastFrame);
                    if (remaining > TimeSpan.Zero) await Task.Delay(remaining, Time, stop.Token).ConfigureAwait(false);
                }

                RenderPending();
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            input.Writer.TryComplete(new IOException("The terminal host failed to write a frame.", ex));
        }
    }

    private void RenderPending()
    {
        RenderFrame frame;
        lock (snapshotGate)
        {
            if (!dirty) return;
            dirty = false;
            if (pendingResize is { } resized)
            {
                renderer.Resize(resized.Columns, resized.Rows);
                pendingResize = null;
            }

            if (snapshot is null) return;
            frame = renderer.Prepare(snapshot, cursor);
        }

        using (frame)
        {
            if (!frame.IsEmpty)
            {
                lock (writeGate)
                {
                    // After restore nothing may be drawn; the frame is discarded.
                    if (Volatile.Read(ref restored) != 0) return;
                    platform.Write(frame.Bytes.Span);
                }
            }

            frame.Commit();
        }

        lastFrame = Time.GetTimestamp();
    }

    private static TerminalSize Sanitize(TerminalSize value) =>
        new(Math.Clamp(value.Columns, 1, 4096), Math.Clamp(value.Rows, 1, 4096));

    private static byte[] BuildEnterSequence(ConsoleTerminalHostOptions options)
    {
        var text = new StringBuilder();
        if (options.AlternateScreen) text.Append("\e[?1049h");
        text.Append("\e[?25l");
        if (options.Mouse)
        {
            text.Append("\e[?1000h\e[?1002h");
            if (options.MouseMotion) text.Append("\e[?1003h");
            text.Append("\e[?1006h");
        }

        if (options.FocusEvents) text.Append("\e[?1004h");
        if (options.BracketedPaste) text.Append("\e[?2004h");
        return Encoding.ASCII.GetBytes(text.ToString());
    }

    private static byte[] BuildRestoreSequence(ConsoleTerminalHostOptions options)
    {
        var text = new StringBuilder();
        if (options.SynchronizedOutput) text.Append("\e[?2026l");
        if (options.BracketedPaste) text.Append("\e[?2004l");
        if (options.FocusEvents) text.Append("\e[?1004l");
        if (options.Mouse)
        {
            text.Append("\e[?1006l");
            if (options.MouseMotion) text.Append("\e[?1003l");
            text.Append("\e[?1002l\e[?1000l");
        }

        text.Append("\e[m\e[?25h");
        if (options.AlternateScreen) text.Append("\e[?1049l");
        return Encoding.ASCII.GetBytes(text.ToString());
    }
}
