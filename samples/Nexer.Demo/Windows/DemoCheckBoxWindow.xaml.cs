// Ported from Jexer demo/DemoCheckBoxWindow.java (MIT, © Autumn Lamonte).
using Nexer.Ui;
using Nexer.Ui.Controls;

namespace Nexer.Demo;

/// <summary>Check boxes, a radio group and a combo box (Jexer <c>DemoCheckBoxWindow</c>).</summary>
public partial class DemoCheckBoxWindow : Window
{
    /// <summary>Creates the window with the second radio option selected.</summary>
    public DemoCheckBoxWindow()
    {
        InitializeComponent();
        Group.Selected = 2;
    }

    private async void OnComboChanged(object? sender, EventArgs e)
    {
        if (Application is not { } app) return;
        await MessageBox.ShowAsync(app, "ComboBox", "You selected the following value:\n\n" + Combo.Text + "\n")
            .ConfigureAwait(true);
    }

    private void OnClose(object? sender, EventArgs e) => Close();
}
