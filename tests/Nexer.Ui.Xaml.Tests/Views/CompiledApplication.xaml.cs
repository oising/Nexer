namespace Nexer.Ui.Xaml.Tests.Views;

/// <summary>An application whose menus are authored in <c>CompiledApplication.xaml</c>.</summary>
public partial class CompiledApplication : Application
{
    /// <summary>Creates the application.</summary>
    public CompiledApplication()
    {
        InitializeComponent();
    }

    public List<string> Log { get; } = [];

    private void OnOpen(object? sender, EventArgs e) => Log.Add("open");

    private void OnRecent(object? sender, EventArgs e) => Log.Add("recent");
}
