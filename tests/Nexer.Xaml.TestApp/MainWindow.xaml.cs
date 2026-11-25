using Nexer.Ui.Markup;
using Nexer.Xaml.Fixtures;

namespace Nexer.Xaml.TestApp;

/// <summary>Code-behind whose markup is compiled into this type by the Nexer XAML build step.</summary>
public partial class MainWindow : Window
{
    /// <summary>Creates the window and loads its compiled XAML.</summary>
    public MainWindow()
    {
        XamlComponent.InitializeComponent(this);
    }

    /// <summary>Number of times the OK button's private handler ran.</summary>
    public int OkClicks { get; private set; }

    /// <summary>Whether the quit handler ran.</summary>
    public bool QuitRequested { get; private set; }

    private void OnOk(object? sender, EventArgs e) => OkClicks++;

    private void OnQuit(object? sender, EventArgs e) => QuitRequested = true;
}
