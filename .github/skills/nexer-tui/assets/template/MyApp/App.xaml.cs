using Nexer.Ui;
using Nexer.Ui.Controls;

namespace MyApp;

/// <summary>The application: menus and the desktop status bar are in App.xaml.</summary>
public partial class App : Application
{
    /// <summary>Matches <c>Command="About"</c> in XAML (commands compare by name).</summary>
    public static readonly Command About = new("About");

    public App()
    {
        InitializeComponent();
    }

    protected override bool OnCommand(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command == About)
        {
            _ = MessageBox.ShowAsync(this, "About", "MyApp\n\nBuilt with Nexer.");
            return true;
        }

        return base.OnCommand(command);
    }
}
