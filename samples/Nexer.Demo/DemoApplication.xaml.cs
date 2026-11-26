// Ported from Jexer demo/DemoApplication.java (MIT, © Autumn Lamonte).
using Nexer.Ui;
using Nexer.Ui.Controls;

namespace Nexer.Demo;

/// <summary>
/// The demo application (Jexer <c>DemoApplication</c>): the menus and desktop status bar are authored in
/// <c>DemoApplication.xaml</c>; the constructor opens the main demo window.
/// </summary>
public partial class DemoApplication : Application
{
    /// <summary>The command that shows the file open box (File/Open, F3).</summary>
    public static readonly Command Open = new("Open");

    /// <summary>Creates the application and, unless <paramref name="openMainWindow"/> is false, the main window.</summary>
    public DemoApplication(bool openMainWindow = true)
    {
        InitializeComponent();
        if (openMainWindow) Windows.Add(new DemoMainWindow());
    }

    /// <summary>The file chosen by the last file open box, if any.</summary>
    public string? LastOpenedFile { get; private set; }

    /// <inheritdoc/>
    protected override bool OnCommand(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command == Open)
        {
            _ = OpenFileAsync();
            return true;
        }

        if (command == Command.Help)
        {
            _ = MessageBox.ShowAsync(this, "Help",
                "Nexer demo.\n\nAlt+letter or F10 opens the menus, Tab moves between controls,\n" +
                "F6 switches windows, Ctrl+W closes the active window and Alt+X exits.");
            return true;
        }

        return base.OnCommand(command);
    }

    /// <summary>Shows the file open box and reports the chosen file (Jexer's cmOpen handler).</summary>
    public async Task OpenFileAsync()
    {
        var file = await FileOpenBox.ShowAsync(this, Directory.GetCurrentDirectory()).ConfigureAwait(true);
        if (file == null) return;
        LastOpenedFile = file;
        await MessageBox.ShowAsync(this, "File Open", "You selected:\n\n" + file).ConfigureAwait(true);
    }

    private void OnNewMainWindow(object? sender, EventArgs e) => Windows.Add(new DemoMainWindow());

    private void OnBindingDemo(object? sender, EventArgs e) => Windows.Add(new DemoBindingWindow());

    private void OnTranslucence(object? sender, EventArgs e) => Translucence = TranslucenceItem.Checked;

    private void OnHideStatusBar(object? sender, EventArgs e) => HideStatusBar = StatusBarItem.Checked;

    private void OnNormal(object? sender, EventArgs e) =>
        _ = MessageBox.ShowAsync(this, "Menu", "You chose a normal menu item.");

    private void OnAbout(object? sender, EventArgs e) =>
        _ = MessageBox.ShowAsync(this, (string)Resources["aboutTitle"]!,
            "Nexer demo\n\nA .NET port of Jexer's demo, authored in XAML.\nJexer is MIT, (c) Autumn Lamonte.");
}
