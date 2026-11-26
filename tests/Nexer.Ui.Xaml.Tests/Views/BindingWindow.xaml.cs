using Nexer.Ui.Controls;

namespace Nexer.Ui.Xaml.Tests.Views;

/// <summary>Code-behind for <c>BindingWindow.xaml</c> (compiled bindings through the Cecil build step).</summary>
public partial class BindingWindow : Window
{
    /// <summary>Creates the window.</summary>
    public BindingWindow()
    {
        InitializeComponent();
    }
}

/// <summary>The x:DataType of <c>BindingWindow.xaml</c>.</summary>
public sealed class BindingWindowViewModel
{
    public string Greeting { get; set; } = "";

    public BindingTestChild Child { get; set; } = new();
}
