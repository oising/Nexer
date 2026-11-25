// Ported from Jexer demo/DemoMainWindow.java (MIT, © Autumn Lamonte).
using System.Globalization;
using Nexer.Ui;

namespace Nexer.Demo;

/// <summary>
/// The main demo window (Jexer <c>DemoMainWindow</c>): a button per demo plus two progress bars driven by timers.
/// </summary>
public partial class DemoMainWindow : Window
{
    private UiTimer? timer1;
    private UiTimer? timer2;
    private int timer1Value;
    private int timer2Value;

    /// <summary>Creates the window.</summary>
    public DemoMainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Value of the first (250 ms) timer.</summary>
    public int Timer1Value => timer1Value;

    /// <inheritdoc/>
    protected override void OnAddedToApplication()
    {
        base.OnAddedToApplication();
        var app = Application!;
        timer1 ??= app.AddTimer(TimeSpan.FromMilliseconds(250), OnTimer1, recurring: true);
        timer2 ??= app.AddTimer(TimeSpan.FromMilliseconds(125), OnTimer2, recurring: true);
    }

    /// <inheritdoc/>
    protected override void OnClosed()
    {
        timer1?.Dispose();
        timer2?.Dispose();
        timer1 = timer2 = null;
        base.OnClosed();
    }

    private void OnTimer1()
    {
        TimerLabel.Text = string.Format(CultureInfo.InvariantCulture, "Timer: {0}", timer1Value);
        TimerLabel.Width = TimerLabel.Text.Length;
        if (timer1Value < 100) timer1Value++;
        else timer1?.Dispose();
        Progress1.Value = timer1Value;
    }

    private void OnTimer2()
    {
        if (timer2Value < 100) timer2Value++;
        else timer2?.Dispose();
        Progress2.Value = timer2Value;
    }

    private void Open(Window window) => Application!.Windows.Add(window);

    private void OnMessageBoxes(object? sender, EventArgs e) => Open(new DemoMsgBoxWindow());

    private void OnModal(object? sender, EventArgs e)
    {
        var window = new DemoMainWindow { Title = "Modal Demo Window", Modal = true };
        Open(window);
        _ = window.ShowModalAsync();
    }

    private void OnFields(object? sender, EventArgs e) => Open(new DemoTextFieldWindow());

    private void OnCheckBoxes(object? sender, EventArgs e) => Open(new DemoCheckBoxWindow());

    private void OnText(object? sender, EventArgs e) => Open(new DemoTextWindow());

    private void OnTreeView(object? sender, EventArgs e) => Open(new DemoTreeViewWindow());

    private void OnScrollable(object? sender, EventArgs e) => Open(new DemoScrollableWindow());

    private void OnSplitPane(object? sender, EventArgs e) => Open(new DemoSplitPaneWindow());

    private void OnOpenFile(object? sender, EventArgs e) => Application!.ExecuteCommand(DemoApplication.Open);
}
