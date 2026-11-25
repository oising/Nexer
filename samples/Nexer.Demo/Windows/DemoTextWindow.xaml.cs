// Ported from Jexer demo/DemoTextWindow.java (MIT, © Autumn Lamonte).
using Nexer.Ui;
using Nexer.Ui.Controls;

namespace Nexer.Demo;

/// <summary>A reflowable text area with justification buttons (Jexer <c>DemoTextWindow</c>).</summary>
public partial class DemoTextWindow : Window
{
    private const string SampleText =
        "This is an example of a reflowable text field.  Some example text follows.\n" +
        "\n" +
        "This library implements a text-based windowing system loosely reminiscent of Borland's Turbo Vision " +
        "library.  It is a .NET port of Jexer, whose UI is authored here in XAML.\n" +
        "\n" +
        "Resize the window to see the text reflow; the buttons change the justification.\n" +
        "\n" +
        "Jexer is licensed MIT.  See the file LICENSE for the full license for the details.\n";

    /// <summary>Creates the window.</summary>
    public DemoTextWindow()
    {
        InitializeComponent();
        Body.Content = SampleText;
    }

    private void OnLeft(object? sender, EventArgs e) => Body.Justification = Justification.Left;

    private void OnCenter(object? sender, EventArgs e) => Body.Justification = Justification.Center;

    private void OnRight(object? sender, EventArgs e) => Body.Justification = Justification.Right;

    private void OnFull(object? sender, EventArgs e) => Body.Justification = Justification.Full;
}
