using System.Text;
using Microsoft.Extensions.Time.Testing;
using Nexer.Core;
using Nexer.Input;

namespace Nexer.Hosting.Tests;

public class ConsoleTerminalHostTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static ConsoleTerminalHost Start(FakePlatform platform, ConsoleTerminalHostOptions? options = null) =>
        ConsoleTerminalHost.Start(platform, options ?? new ConsoleTerminalHostOptions { MinFrameInterval = TimeSpan.Zero }, processHandlers: false);

    [Fact]
    public async Task StartEntersRawModeThenEnablesTerminalModes()
    {
        var platform = new FakePlatform();
        await using (ConsoleTerminalHost host = Start(platform))
        {
            Assert.Equal(new TerminalSize(20, 5), host.Size);
            Assert.Equal(
                ["enter", "write:\e[?1049h\e[?25l\e[?1000h\e[?1002h\e[?1006h\e[?1004h\e[?2004h"],
                platform.Log);
        }
    }

    [Fact]
    public async Task OptionalModesAreOnlyEnabledAndRestoredWhenRequested()
    {
        var platform = new FakePlatform();
        var options = new ConsoleTerminalHostOptions
        {
            AlternateScreen = false,
            Mouse = true,
            MouseMotion = true,
            FocusEvents = false,
            BracketedPaste = false,
            SynchronizedOutput = false,
        };
        await using (Start(platform, options))
        {
        }

        Assert.Equal(
            [
                "enter",
                "write:\e[?25l\e[?1000h\e[?1002h\e[?1003h\e[?1006h",
                "write:\e[?1006l\e[?1003l\e[?1002l\e[?1000l\e[m\e[?25h",
                "restore",
            ],
            platform.Log);
    }

    [Fact]
    public async Task DisposeRestoresExactlyOnceAndCompletesInput()
    {
        var platform = new FakePlatform();
        ConsoleTerminalHost host = Start(platform);
        await host.DisposeAsync();
        await host.DisposeAsync();
        host.Restore();
        IReadOnlyList<string> log = platform.Log;
        Assert.Equal(4, log.Count);
        Assert.Equal("write:\e[?2026l\e[?2004l\e[?1004l\e[?1006l\e[?1002l\e[?1000l\e[m\e[?25h\e[?1049l", log[2]);
        Assert.Equal("restore", log[3]);
        await host.Input.Completion.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Throws<ObjectDisposedException>(() => host.Present(new CellSurface(1, 1), default));
    }

    [Fact]
    public async Task PresentSnapshotsTheSurfaceBeforeReturning()
    {
        var platform = new FakePlatform();
        await using ConsoleTerminalHost host = Start(platform);
        var surface = new CellSurface(20, 5);
        Write(surface, 0, 0, "hello");
        host.Present(surface, new CursorState(true, 3, 2));
        Write(surface, 0, 0, "world");
        await WaitUntil(() => platform.ScreenRow(0) == "hello" && platform.Cursor == (true, 3, 2));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal("hello", platform.ScreenRow(0));

        host.Present(surface, CursorState.Hidden);
        await WaitUntil(() => platform.ScreenRow(0) == "world" && !platform.Cursor.Visible);
    }

    [Fact]
    public async Task PresentsAreCoalescedToTheLatestStateWithinTheFrameInterval()
    {
        var time = new FakeTimeProvider();
        var platform = new FakePlatform();
        await using ConsoleTerminalHost host = Start(platform, new ConsoleTerminalHostOptions
        {
            TimeProvider = time,
            MinFrameInterval = TimeSpan.FromMilliseconds(16),
        });
        var surface = new CellSurface(20, 5);
        Write(surface, 0, 0, "first");
        host.Present(surface, default);
        await WaitUntil(() => platform.ScreenRow(0) == "first");
        Assert.Equal(1, platform.FrameWrites);

        for (int i = 0; i < 10; i++)
        {
            Write(surface, 0, 1, $"frame {i}");
            host.Present(surface, default);
        }

        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(1, platform.FrameWrites);
        time.Advance(TimeSpan.FromMilliseconds(16));
        await WaitUntil(() => platform.ScreenRow(1) == "frame 9");
        time.Advance(TimeSpan.FromMilliseconds(100));
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(2, platform.FrameWrites);
    }

    [Fact]
    public async Task ClustersAndWideCellsSurviveTheSnapshot()
    {
        var platform = new FakePlatform();
        await using ConsoleTerminalHost host = Start(platform);
        var surface = new CellSurface(20, 5);
        surface.WriteCluster(0, 0, "e\u0301".EnumerateRunes().ToArray(), 1);
        surface.Write(1, 0, new Rune('中'), 2);
        surface.WriteCluster(3, 0, "👍🏽".EnumerateRunes().ToArray(), 2);
        host.Present(surface, default);
        await WaitUntil(() => platform.ScreenRow(0) == "e\u0301中👍🏽");
    }

    [Fact]
    public async Task UnchangedPresentWritesNothing()
    {
        var platform = new FakePlatform();
        await using ConsoleTerminalHost host = Start(platform);
        var surface = new CellSurface(20, 5);
        Write(surface, 0, 0, "x");
        host.Present(surface, default);
        await WaitUntil(() => platform.FrameWrites == 1);
        host.Present(surface, default);
        host.Present(surface, default);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(1, platform.FrameWrites);
    }

    [Fact]
    public async Task InputIsDecodedIntoTheChannel()
    {
        var platform = new FakePlatform();
        await using ConsoleTerminalHost host = Start(platform);
        platform.Type("a\e[1;5A\e[<0;3;2M\e[200~p\r\e[201~");
        Assert.Equal(new KeyEvent(Key.Character, new Rune('a')), await Next(host));
        Assert.Equal(new KeyEvent(Key.Up, default, KeyModifiers.Ctrl), await Next(host));
        Assert.Equal(new MouseEvent(MouseEventKind.Down, MouseButton.Left, 2, 1), await Next(host));
        Assert.Equal(new PasteEvent("p\r"), await Next(host));
    }

    [Fact]
    public async Task LoneEscapeIsResolvedByTheHostClock()
    {
        var time = new FakeTimeProvider();
        var platform = new FakePlatform();
        await using ConsoleTerminalHost host = Start(platform, new ConsoleTerminalHostOptions { TimeProvider = time });
        platform.Type("\e");
        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.False(host.Input.TryRead(out _));
        time.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Equal(new KeyEvent(Key.Escape), await Next(host));
    }

    [Fact]
    public async Task EndOfInputCompletesTheChannel()
    {
        var platform = new FakePlatform();
        await using ConsoleTerminalHost host = Start(platform);
        platform.Type("q\e");
        platform.EndInput();
        Assert.Equal(new KeyEvent(Key.Character, new Rune('q')), await Next(host));
        Assert.Equal(new KeyEvent(Key.Escape), await Next(host));
        await host.Input.Completion.WaitAsync(Timeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ResizeIsReportedAndTheNextFrameRepaintsEverything()
    {
        var platform = new FakePlatform();
        await using ConsoleTerminalHost host = Start(platform);
        var surface = new CellSurface(20, 5);
        Write(surface, 0, 0, "before");
        host.Present(surface, default);
        await WaitUntil(() => platform.ScreenRow(0) == "before");

        platform.ResizeTerminal(30, 6);
        Assert.Equal(new ResizeEvent(30, 6), await Next(host));
        Assert.Equal(new TerminalSize(30, 6), host.Size);

        var resized = new CellSurface(30, 6);
        Write(resized, 0, 5, "after");
        host.Present(resized, default);
        await WaitUntil(() => platform.ScreenRow(5) == "after");
        Assert.Equal("", platform.ScreenRow(0));
    }

    [Fact]
    public async Task MismatchedSurfaceIsClippedNotAnError()
    {
        var platform = new FakePlatform(10, 2);
        await using ConsoleTerminalHost host = Start(platform);
        var surface = new CellSurface(15, 4);
        Write(surface, 0, 0, "0123456789abcde");
        Write(surface, 0, 3, "hidden");
        host.Present(surface, new CursorState(true, 14, 3));
        await WaitUntil(() => platform.ScreenRow(0) == "0123456789");
        Assert.Equal("", platform.ScreenRow(1));
    }

    [Fact]
    public async Task WriteFailureFaultsInputAndDisposeStillRestores()
    {
        var platform = new FakePlatform();
        ConsoleTerminalHost host = Start(platform);
        platform.FailWrites = true;
        var surface = new CellSurface(20, 5);
        Write(surface, 0, 0, "x");
        host.Present(surface, default);
        await Assert.ThrowsAnyAsync<IOException>(() => host.Input.Completion.WaitAsync(Timeout, TestContext.Current.CancellationToken));
        await host.DisposeAsync();
        Assert.Equal("restore", platform.Log[^1]);
    }

    [Fact]
    public void FailedStartupRestoresTheTerminal()
    {
        var platform = new FakePlatform { FailWrites = true };
        Assert.Throws<IOException>(() => Start(platform));
        Assert.Equal(["enter", "restore"], platform.Log);
    }

    private static async Task<InputEvent> Next(ConsoleTerminalHost host)
    {
        InputEvent value = await host.Input.ReadAsync().AsTask().WaitAsync(Timeout);
        return value with { Timestamp = 0 };
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not reached.");
            await Task.Delay(5);
        }
    }

    private static void Write(CellSurface surface, int column, int row, string text)
    {
        foreach (char c in text) surface.Write(column++, row, new Rune(c));
    }
}
