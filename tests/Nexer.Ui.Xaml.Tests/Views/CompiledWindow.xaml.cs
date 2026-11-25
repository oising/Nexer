using Nexer.Ui.Controls;

namespace Nexer.Ui.Xaml.Tests.Views;

/// <summary>Code-behind for <c>CompiledWindow.xaml</c>, compiled by the Nexer XAML build step.</summary>
public partial class CompiledWindow : Window
{
    /// <summary>Creates the window.</summary>
    public CompiledWindow()
    {
        InitializeComponent();
    }

    public List<string> Log { get; } = [];

    private void OnOk(object? sender, EventArgs e) => Log.Add("ok");

    private void OnCancel(object? sender, EventArgs e) => Log.Add("cancel");

    private void OnNameChanged(object? sender, EventArgs e) => Log.Add("name:" + NameField.Text);

    private void OnCheckChanged(object? sender, EventArgs e) => Log.Add("check:" + Check.Checked);

    private void OnRadioChanged(object? sender, EventArgs e) => Log.Add("radio:" + Radios.Selected);

    private void OnSpinUp(object? sender, EventArgs e) => Log.Add("spin");

    private void OnFruitActivated(object? sender, EventArgs e) => Log.Add("fruit:" + Fruit.SelectedItem);
}
