using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Nexer.Hosting.Platform;

/// <summary>Unix terminal: termios raw mode on stdin, direct fd I/O, size via TIOCGWINSZ.</summary>
/// <remarks>
/// The termios structure is treated as an opaque buffer (its layout differs between libcs) that is captured
/// with <c>tcgetattr</c>, made raw with <c>cfmakeraw</c> and restored byte-for-byte.
/// System.Console is avoided for I/O so .NET's own terminal handling does not interfere.
/// </remarks>
[UnsupportedOSPlatform("windows")]
internal sealed partial class UnixTerminalPlatform : ITerminalPlatform
{
    private const int StdIn = 0;
    private const int StdOut = 1;
    private const int TcsaNow = 0;
    private const short PollIn = 0x1;
    private const int EIntr = 4;
    private const int EAgain = 11;
    private const int EAgainBsd = 35;

    private readonly byte[] original = new byte[512];
    private readonly Lock gate = new();
    private bool entered;

    public UnixTerminalPlatform()
    {
        if (isatty(StdIn) == 0 || isatty(StdOut) == 0)
        {
            throw new InvalidOperationException(
                "ConsoleTerminalHost requires an interactive terminal; standard input or output is redirected.");
        }
    }

    public void Enter()
    {
        lock (gate)
        {
            if (entered) return;
            if (tcgetattr(StdIn, original) != 0)
            {
                throw new IOException($"tcgetattr failed (errno {Marshal.GetLastPInvokeError()}).");
            }

            byte[] raw = (byte[])original.Clone();
            cfmakeraw(raw);
            if (tcsetattr(StdIn, TcsaNow, raw) != 0)
            {
                throw new IOException($"tcsetattr failed (errno {Marshal.GetLastPInvokeError()}).");
            }

            entered = true;
        }
    }

    public void Restore()
    {
        lock (gate)
        {
            if (!entered) return;
            entered = false;
            tcsetattr(StdIn, TcsaNow, original);
        }
    }

    public TerminalSize GetSize()
    {
        // ioctl is variadic; Apple arm64 passes variadic arguments on the stack, so only use it on Linux.
        if (OperatingSystem.IsLinux())
        {
            WinSize size = default;
            if (ioctl(StdOut, 0x5413, ref size) == 0 && size.Columns > 0 && size.Rows > 0)
            {
                return new TerminalSize(size.Columns, size.Rows);
            }
        }

        try
        {
            return new TerminalSize(Math.Max(1, Console.WindowWidth), Math.Max(1, Console.WindowHeight));
        }
        catch (IOException)
        {
            return new TerminalSize(80, 24);
        }
    }

    public unsafe void Write(ReadOnlySpan<byte> bytes)
    {
        fixed (byte* start = bytes)
        {
            int offset = 0;
            while (offset < bytes.Length)
            {
                nint written = write(StdOut, start + offset, bytes.Length - offset);
                if (written < 0)
                {
                    int errno = Marshal.GetLastPInvokeError();
                    if (errno is EIntr or EAgain or EAgainBsd) continue;
                    throw new IOException($"write failed (errno {errno}).");
                }

                offset += (int)written;
            }
        }
    }

    public unsafe int Read(Span<byte> buffer, TimeSpan timeout)
    {
        var fd = new PollFd { Fd = StdIn, Events = PollIn };
        int ready = poll(ref fd, 1, (int)Math.Clamp((long)Math.Ceiling(timeout.TotalMilliseconds), 0, 1000));
        if (ready <= 0) return 0;
        fixed (byte* start = buffer)
        {
            nint count = read(StdIn, start, buffer.Length);
            if (count > 0) return (int)count;
            if (count == 0) return -1;
            int errno = Marshal.GetLastPInvokeError();
            return errno is EIntr or EAgain or EAgainBsd ? 0 : -1;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinSize
    {
        public ushort Rows;
        public ushort Columns;
        public ushort XPixels;
        public ushort YPixels;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [LibraryImport("libc", SetLastError = true)]
    private static partial int isatty(int fd);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int tcgetattr(int fd, [Out] byte[] termios);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int tcsetattr(int fd, int optionalActions, byte[] termios);

    [LibraryImport("libc")]
    private static partial void cfmakeraw([In, Out] byte[] termios);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int ioctl(int fd, nuint request, ref WinSize size);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int poll(ref PollFd fds, nuint count, int timeout);

    [LibraryImport("libc", SetLastError = true)]
    private static unsafe partial nint read(int fd, byte* buffer, nint count);

    [LibraryImport("libc", SetLastError = true)]
    private static unsafe partial nint write(int fd, byte* buffer, nint count);
}
