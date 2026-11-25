// Ported from Jexer demo/DemoTextFieldWindow.java (MIT, © Autumn Lamonte).
using Nexer.Ui;

namespace Nexer.Demo;

/// <summary>
/// Text fields, password fields, and a day-of-week combo box with a spinner and progress bar
/// (Jexer <c>DemoTextFieldWindow</c>; the calendar is out of scope).
/// </summary>
public partial class DemoTextFieldWindow : Window
{
    /// <summary>Creates the window with Sunday selected.</summary>
    public DemoTextFieldWindow()
    {
        InitializeComponent();
        DayCombo.SelectedIndex = 0;
        UpdateProgress();
    }

    private void OnSpinUp(object? sender, EventArgs e) => Step(+1);

    private void OnSpinDown(object? sender, EventArgs e) => Step(-1);

    private void Step(int delta)
    {
        int count = DayCombo.Items.Count;
        DayCombo.SelectedIndex = ((Math.Max(DayCombo.SelectedIndex, 0) + delta) % count + count) % count;
        UpdateProgress();
    }

    private void OnDayChanged(object? sender, EventArgs e) => UpdateProgress();

    private void UpdateProgress() => WeekProgress.Value = Math.Max(DayCombo.SelectedIndex, 0) * 100 / (DayCombo.Items.Count - 1);

    private void OnClose(object? sender, EventArgs e) => Close();
}
