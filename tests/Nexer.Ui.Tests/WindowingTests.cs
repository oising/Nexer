using Nexer.Input;
using Nexer.Ui.Menus;
using Nexer.Ui.Testing;

namespace Nexer.Ui.Tests;

public class WindowingTests
{
    private static (Application App, UiDriver Driver) Start(int columns = 40, int rows = 12, params Window[] windows)
    {
        var app = new Application { ConfirmExit = false };
        foreach (var w in windows) app.Windows.Add(w);
        return (app, new UiDriver(app, columns, rows));
    }

    [Fact]
    public void WindowChromeMatchesJexer()
    {
        var (_, d) = Start(40, 12, new Window { Title = "Hello", X = 2, Y = 1, Width = 20, Height = 6 });
        Assert.Equal(
            """
            ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░
            ░░╔═[■]═ Hello ══[↑]═╗░░░░░░░░░░░░░░░░░░
            ░░║                  ║░░░░░░░░░░░░░░░░░░
            ░░║                  ║░░░░░░░░░░░░░░░░░░
            ░░║                  ║░░░░░░░░░░░░░░░░░░
            ░░║                  ║░░░░░░░░░░░░░░░░░░
            ░░╚══════════════════╝░░░░░░░░░░░░░░░░░░
            """.ReplaceLineEndings("\n"),
            d.Text(0, 1, 40, 7));
        Assert.Equal(d.Theme("twindow.border"), d.Style(2, 2));
        Assert.Equal(d.Theme("twindow.background"), d.Style(5, 3));
        Assert.Equal(d.Theme("tdesktop.background"), d.Style(0, 1));
        Assert.Equal(DrawContext.ShadowStyle, d.Style(22, 3));
        Assert.Equal(DrawContext.ShadowStyle, d.Style(4, 8));
        Assert.Equal("░", d.Cell(22, 3));
    }

    [Fact]
    public void InactiveWindowUsesSingleBorderAndNoControls()
    {
        var back = new Window { Title = "Back", X = 0, Y = 0, Width = 14, Height = 4 };
        var front = new Window { Title = "Front", X = 20, Y = 0, Width = 14, Height = 4 };
        var (app, d) = Start(40, 12, back, front);
        Assert.Same(front, app.ActiveWindow);
        Assert.Equal("┌─── Back ───┐", d.Row(1, 0, 14));
        Assert.Equal(d.Theme("twindow.border.inactive"), d.Style(0, 1));
        Assert.StartsWith("╔═[■]", d.Row(1, 20, 14));
    }

    [Fact]
    public void ClickingAWindowActivatesItAndBringsItToFront()
    {
        var a = new Window { Title = "A", X = 0, Y = 0, Width = 20, Height = 6 };
        var b = new Window { Title = "B", X = 10, Y = 2, Width = 20, Height = 6 };
        var (app, d) = Start(40, 12, a, b);
        Assert.Same(b, app.ActiveWindow);
        d.Click(2, 3);
        Assert.Same(a, app.ActiveWindow);
        Assert.Same(a, app.Windows[0]);
        Assert.Equal("║", d.Cell(19, 4));
    }

    [Fact]
    public void DraggingTheTitleBarMovesTheWindow()
    {
        var w = new Window { Title = "Drag", X = 2, Y = 2, Width = 16, Height = 5 };
        var (_, d) = Start(40, 12, w);
        Assert.Equal(3, w.Y);
        d.Mouse(MouseEventKind.Down, 10, 3);
        Assert.True(w.IsInMovement);
        d.Mouse(MouseEventKind.Drag, 15, 5);
        d.Mouse(MouseEventKind.Up, 15, 5);
        Assert.False(w.IsInMovement);
        Assert.Equal(7, w.X);
        Assert.Equal(5, w.Y);
        Assert.Equal("╔", d.Cell(7, 5));
    }

    [Fact]
    public void DraggingTheResizeGripResizesTheWindow()
    {
        var w = new Window { Title = "Size", X = 0, Y = 0, Width = 16, Height = 5 };
        var (_, d) = Start(40, 12, w);
        d.Drag(15, 5, 20, 8);
        Assert.Equal(21, w.Width);
        Assert.Equal(8, w.Height);
    }

    [Fact]
    public void CloseBoxClosesTheWindowAndRaisesEvents()
    {
        var w = new Window { Title = "Bye", X = 0, Y = 0, Width = 16, Height = 5 };
        var (app, d) = Start(40, 12, w);
        bool closing = false, closed = false;
        w.Closing += (_, _) => closing = true;
        w.Closed += (_, _) => closed = true;
        d.Click(3, 1);
        Assert.True(closing);
        Assert.True(closed);
        Assert.Empty(app.Windows);
        Assert.Null(w.Application);
    }

    [Fact]
    public void ClosingCanBeCancelled()
    {
        var w = new Window { Title = "Stay", Width = 16, Height = 5 };
        var (app, d) = Start(40, 12, w);
        w.Closing += (_, e) => e.Cancel = true;
        d.Press("Ctrl+W");
        Assert.Single(app.Windows);
    }

    [Fact]
    public void ZoomBoxMaximizesAndRestores()
    {
        var w = new Window { Title = "Zoom", X = 3, Y = 3, Width = 16, Height = 5 };
        var (app, d) = Start(40, 12, w);
        d.Click(3 + 16 - 4, 4);
        Assert.True(w.IsMaximized);
        Assert.Equal(new Rect(0, app.DesktopTop, 40, app.DesktopBottom - app.DesktopTop), w.Bounds);
        Assert.Equal("↕", d.Cell(36, 1));
        d.Press("F5");
        Assert.False(w.IsMaximized);
        Assert.Equal(new Rect(3, 4, 16, 5), w.Bounds);
    }

    [Fact]
    public void TabMovesFocusAndKeysGoToTheFocusedWidget()
    {
        var w = new Window { Title = "Focus", Width = 20, Height = 6 };
        var a = new TestWidget { Name = "a", X = 0, Y = 0 };
        var b = new TestWidget { Name = "b", X = 0, Y = 1 };
        w.Children.Add(a);
        w.Children.Add(b);
        var (app, d) = Start(40, 12, w);
        Assert.Same(a, app.FocusedWidget);
        d.Press("Tab");
        Assert.Same(b, app.FocusedWidget);
        d.Type("x");
        Assert.Single(b.Keys);
        d.Press("Shift+Tab");
        Assert.Same(a, app.FocusedWidget);
        d.Paste("pasted");
        Assert.Equal("pasted", a.Pasted);
    }

    [Fact]
    public void MouseClicksReachChildWidgetsInLocalCoordinates()
    {
        var w = new Window { Title = "Mouse", X = 0, Y = 0, Width = 20, Height = 6 };
        var a = new TestWidget { Name = "a", X = 2, Y = 1 };
        var b = new TestWidget { Name = "b", X = 2, Y = 2 };
        w.Children.Add(a);
        w.Children.Add(b);
        var (app, d) = Start(40, 12, w);
        d.Click(4, 4);
        Assert.Equal(1, b.Clicks);
        Assert.Equal(0, a.Clicks);
        Assert.Same(b, app.FocusedWidget);
    }

    [Fact]
    public void F6SwitchesWindows()
    {
        var a = new Window { Title = "A", Width = 12, Height = 4 };
        var b = new Window { Title = "B", Width = 12, Height = 4 };
        var (app, d) = Start(40, 12, a, b);
        Assert.Same(b, app.ActiveWindow);
        d.Press("F6");
        Assert.Same(a, app.ActiveWindow);
        d.Press("Shift+F6");
        Assert.Same(b, app.ActiveWindow);
    }

    [Fact]
    public void SmartPlacementAvoidsOverlap()
    {
        var a = new Window { Title = "A", Width = 20, Height = 5 };
        var b = new Window { Title = "B", Width = 20, Height = 5 };
        Start(60, 20, a, b);
        Assert.False(a.Bounds.IntersectsWith(b.Bounds));
    }

    [Fact]
    public void TileArrangesWindowsWithoutOverlap()
    {
        var windows = Enumerable.Range(0, 3).Select(i => new Window { Title = $"W{i}", Width = 20, Height = 5 }).ToArray();
        var (app, d) = Start(60, 20, windows);
        app.ExecuteCommand(Command.Tile);
        d.Pump();
        for (int i = 0; i < 3; i++)
        {
            for (int j = i + 1; j < 3; j++) Assert.False(windows[i].Bounds.IntersectsWith(windows[j].Bounds));
            Assert.True(windows[i].Y >= app.DesktopTop);
            Assert.True(windows[i].Y + windows[i].Height <= app.DesktopBottom);
        }
    }

    [Fact]
    public void CenteredWindowIsCentered()
    {
        var w = new Window { Title = "C", Width = 20, Height = 6, Centered = true };
        Start(40, 12, w);
        Assert.Equal(10, w.X);
        Assert.Equal(1 + (10 - 6) / 2, w.Y);
    }

    [Fact]
    public async Task ModalDialogReturnsItsResultAndBlocksOtherWindows()
    {
        var main = new Window { Title = "Main", Width = 20, Height = 6 };
        var (app, d) = Start(60, 20, main);
        var dialog = new Window { Title = "Ask", Width = 20, Height = 6 };
        var button = new TestWidget { Name = "ok" };
        dialog.Children.Add(button);
        dialog.Modal = true;
        app.Windows.Add(dialog);
        var task = dialog.ShowModalAsync();
        d.Pump();
        Assert.Same(dialog, app.ActiveWindow);
        Assert.True(app.IsModalActive);

        d.Click(main.X + 2, main.Y + 2);
        Assert.Same(dialog, app.ActiveWindow);
        d.Press("F6");
        Assert.Same(dialog, app.ActiveWindow);

        d.Advance(TimeSpan.FromSeconds(1));
        Assert.False(task.IsCompleted);
        dialog.Close("yes");
        d.Pump();
        Assert.Equal("yes", await task);
        Assert.Same(main, app.ActiveWindow);
    }

    [Fact]
    public async Task ShowModalAsyncAddsTheWindowToTheCurrentApplication()
    {
        var (app, d) = Start();
        var dialog = new Window { Title = "Ask", Width = 20, Height = 6 };
        var task = dialog.ShowModalAsync<string>();
        d.Pump();
        Assert.Contains(dialog, app.Windows);
        Assert.True(dialog.Modal);
        d.Press("Ctrl+W");
        Assert.DoesNotContain(dialog, app.Windows);
        // The generic overload's continuation runs asynchronously, so await rather than checking IsCompleted.
        Assert.Null(await task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void TimersFireOnTheUiLoop()
    {
        var (app, d) = Start();
        int fired = 0;
        using var timer = app.AddTimer(TimeSpan.FromMilliseconds(100), () => fired++, recurring: true);
        d.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Equal(0, fired);
        d.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Equal(3, fired);
        timer.Dispose();
        d.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(3, fired);
    }

    [Fact]
    public void AwaitContinuationsRunOnTheUiLoop()
    {
        var (app, d) = Start();
        int? thread = null;
        var gate = new TaskCompletionSource();
        app.Post(async () =>
        {
            await gate.Task;
            thread = Environment.CurrentManagedThreadId;
        });
        d.Pump();
        gate.SetResult();
        Assert.Null(thread);
        d.Pump();
        Assert.Equal(Environment.CurrentManagedThreadId, thread);
    }

    [Fact]
    public void ResizeCreatesANewScreenAndKeepsMaximizedWindowsFull()
    {
        var w = new Window { Title = "Max", Width = 20, Height = 6 };
        var (app, d) = Start(40, 12, w);
        w.Maximize();
        d.Resize(50, 15);
        Assert.Equal(50, d.Frame.Columns);
        Assert.Equal(15, d.Frame.Rows);
        Assert.Equal(new Rect(0, app.DesktopTop, 50, app.DesktopBottom - app.DesktopTop), w.Bounds);
    }

    [Fact]
    public void ExitCommandStopsTheLoop()
    {
        var (app, d) = Start();
        Assert.True(app.IsRunning);
        app.PostCommand(Command.Exit);
        d.Pump();
        Assert.False(app.IsRunning);
    }

    [Fact]
    public async Task RunAsyncCompletesWhenInputEnds()
    {
        var app = new Application();
        var host = new HeadlessHost(20, 5, TimeProvider.System);
        var run = app.RunAsync(host, TestContext.Current.CancellationToken);
        host.CompleteInput();
        await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.NotNull(host.Frame);
    }

    [Fact]
    public void StatusBarShowsKeysAndRunsCommands()
    {
        var w = new Window { Title = "S", Width = 20, Height = 5 };
        var bar = new StatusBar { Text = "Ready" };
        bool helped = false;
        var help = new StatusKey { Key = KeyGesture.Parse("F1"), Text = "Help" };
        help.Click += (_, _) => helped = true;
        bar.Keys.Add(help);
        w.StatusBar = bar;
        var (_, d) = Start(40, 12, w);
        Assert.Equal(" F1 Help │ Ready", d.Row(11).TrimEnd());
        d.Press("F1");
        Assert.True(helped);
        helped = false;
        d.Click(2, 11);
        Assert.True(helped);
    }
}
