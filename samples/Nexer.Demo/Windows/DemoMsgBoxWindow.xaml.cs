// Ported from Jexer demo/DemoMsgBoxWindow.java (MIT, © Autumn Lamonte).
using Nexer.Ui;
using Nexer.Ui.Controls;

namespace Nexer.Demo;

/// <summary>Message boxes and input boxes, all shown with the async dialog API (Jexer <c>DemoMsgBoxWindow</c>).</summary>
public partial class DemoMsgBoxWindow : Window
{
    /// <summary>Creates the window.</summary>
    public DemoMsgBoxWindow()
    {
        InitializeComponent();
    }

    /// <summary>A description of the last dialog result.</summary>
    public string? Result => LastResult.Text;

    private Application App => Application!;

    private async void OnOk(object? sender, EventArgs e) => Report(await MessageBox.ShowAsync(App, "OK MessageBox",
        "This is an example of a OK MessageBox.  This is the\ndefault MessageBox.\n\n" +
        "Note that the MessageBox text can span multiple\nlines.\n\n" +
        "The default result (if someone hits the top-left\nclose button) is OK.\n").ConfigureAwait(true));

    private async void OnOkCancel(object? sender, EventArgs e) => Report(await MessageBox.ShowAsync(App, "OK/Cancel MessageBox",
        "This is an example of a OK/Cancel MessageBox.\n\n" +
        "Note that the MessageBox text can span multiple\nlines.\n\n" +
        "The default result (if someone hits the top-left\nclose button) is CANCEL.\n",
        MessageBoxButtons.OkCancel).ConfigureAwait(true));

    private async void OnYesNo(object? sender, EventArgs e) => Report(await MessageBox.ShowAsync(App, "Yes/No MessageBox",
        "This is an example of a Yes/No MessageBox.\n\n" +
        "Note that the MessageBox text can span multiple\nlines.\n\n" +
        "The default result (if someone hits the top-left\nclose button) is NO.\n",
        MessageBoxButtons.YesNo).ConfigureAwait(true));

    private async void OnYesNoCancel(object? sender, EventArgs e) => Report(await MessageBox.ShowAsync(App, "Yes/No/Cancel MessageBox",
        "This is an example of a Yes/No/Cancel MessageBox.\n\n" +
        "Note that the MessageBox text can span multiple\nlines.\n\n" +
        "The default result (if someone hits the top-left\nclose button) is CANCEL.\n",
        MessageBoxButtons.YesNoCancel).ConfigureAwait(true));

    private async void OnInput(object? sender, EventArgs e)
    {
        var text = await InputBox.ShowAsync(App, "Input Box",
            "This is an example of an InputBox.\n\nNote that the InputBox text can span multiple\nlines.\n",
            "some input text").ConfigureAwait(true);
        LastResult.Text = "Input: " + text;
        await MessageBox.ShowAsync(App, "Your InputBox Answer", "You entered: " + text).ConfigureAwait(true);
    }

    private async void OnCancellableInput(object? sender, EventArgs e)
    {
        var text = await InputBox.ShowAsync(App, "Input Box",
            "This is an example of an InputBox.\n\nNote that the InputBox text can span multiple\nlines.\n\n" +
            "This one has both OK and Cancel buttons.\n",
            "some input text", MessageBoxButtons.OkCancel).ConfigureAwait(true);
        string pressed = text == null ? "Cancel" : "OK";
        LastResult.Text = "Input: " + (text ?? "(cancelled)");
        await MessageBox.ShowAsync(App, "Your InputBox Answer",
            "You entered: " + (text ?? "") + " and pressed " + pressed).ConfigureAwait(true);
    }

    private void Report(MessageBoxResult result) => LastResult.Text = "Result: " + result;

    private void OnClose(object? sender, EventArgs e) => Close();
}
