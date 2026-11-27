using Nexer.Ui;
using Nexer.Ui.Controls;
using Nexer.Ui.Testing;

namespace Nexer.Demo;

/// <summary>Renders the README screenshots headlessly as SVG (the <c>--screenshots &lt;dir&gt;</c> mode).</summary>
public static class DemoScreenshots
{
    private const int Columns = 100;
    private const int Rows = 30;

    /// <summary>The screenshots: file name, alt text and a function producing the driven application.</summary>
    public static IReadOnlyList<(string File, string Title, Func<UiDriver> Create)> Shots { get; } =
    [
        ("desktop.svg", "Nexer demo: desktop, menu bar and the main demo window with the File menu open", () =>
        {
            var driver = new UiDriver(new DemoApplication(), Columns, Rows);
            driver.Press("Alt+F");
            return driver;
        }),
        ("controls.svg", "Nexer demo: radio buttons, check boxes and a combo box", () => Single(new DemoCheckBoxWindow())),
        ("binding.svg", "Nexer demo: an MVVM form with data binding and validation", () => Single(new DemoBindingWindow())),
        ("treeview.svg", "Nexer demo: tree views, a directory list and a list", () => TreeViewShot()),
    ];

    /// <summary>Writes every screenshot to <paramref name="directory"/>, logging one line per file. Returns the exit code.</summary>
    public static int Run(string directory, TextWriter log)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(log);
        Directory.CreateDirectory(directory);
        foreach (var (file, title, create) in Shots)
        {
            var driver = create();
            string path = Path.Combine(directory, file);
            File.WriteAllText(path, ScreenDump.Svg(driver.Frame, new SvgOptions { Title = title }));
            log.WriteLine($"wrote {path}");
        }

        return 0;
    }

    private static UiDriver Single(Window window)
    {
        var app = new DemoApplication(openMainWindow: false);
        app.Windows.Add(window);
        return new UiDriver(app, Columns, Rows);
    }

    // The tree view browses a small throwaway project rather than the real file system, so the image is the same on
    // every machine and shows no local paths.
    private static UiDriver TreeViewShot()
    {
        string root = Path.Combine(Path.GetTempPath(), "nexer-screenshot-" + Guid.NewGuid().ToString("N"));
        string project = Path.Combine(root, "MyApp");
        try
        {
            foreach (string dir in (string[])["docs", "src/Views", "src/ViewModels", "tests"])
            {
                Directory.CreateDirectory(Path.Combine(project, dir));
            }

            foreach (var (name, size) in (ReadOnlySpan<(string, int)>)[("App.xaml", 1200), ("App.xaml.cs", 900),
                         ("MyApp.csproj", 600), ("Program.cs", 1500), ("README.md", 4200), ("MainWindow.xaml", 3100)])
            {
                File.WriteAllText(Path.Combine(project, name), new string('x', size));
            }

            var window = new DemoTreeViewWindow(project);
            window.DirectoryTree.Root = new DirectoryTreeItem { IsExpanded = true, Path = project };
            window.DirectoryTree.Select(window.DirectoryTree.Root, true);
            window.Selection.Text = "Directory: MyApp";
            return Single(window);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
