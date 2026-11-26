using System.Globalization;
using Nexer.Ui.Controls;
using Nexer.Ui.Data;

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

    public List<string> Items { get; set; } = [];
}

/// <summary>A non-public view model: fine for the Cecil path (the accessors are emitted into this assembly).</summary>
internal sealed class InternalViewModel
{
    public string Secret { get; set; } = "";
}

/// <summary>Upper-cases strings.</summary>
public sealed class UpperCaseConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value?.ToString()?.ToUpperInvariant();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value;
}
