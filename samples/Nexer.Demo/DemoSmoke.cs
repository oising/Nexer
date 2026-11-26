using Nexer.Ui;
using Nexer.Ui.Testing;

namespace Nexer.Demo;

/// <summary>Constructs every demo window headlessly and renders one frame of each (the <c>--smoke</c> mode).</summary>
public static class DemoSmoke
{
    /// <summary>Every demo window: its title and a factory.</summary>
    public static IReadOnlyList<(string Title, Func<Window> Create)> Windows { get; } =
    [
        ("Demo Window", () => new DemoMainWindow()),
        ("Message Boxes", () => new DemoMsgBoxWindow()),
        ("Text Fields", () => new DemoTextFieldWindow()),
        ("Radiobuttons, CheckBoxes, and ComboBox", () => new DemoCheckBoxWindow()),
        ("Text Area", () => new DemoTextWindow()),
        ("Tree View", () => new DemoTreeViewWindow()),
        ("Scrollable Window", () => new DemoScrollableWindow()),
        ("Split Panes", () => new DemoSplitPaneWindow()),
        ("Data Binding", () => new DemoBindingWindow()),
    ];

    /// <summary>
    /// Runs the smoke test, writing one line per window (and the frame when <paramref name="dump"/>) to
    /// <paramref name="log"/>. Returns the exit code.
    /// </summary>
    public static int Run(TextWriter log, bool dump = false)
    {
        ArgumentNullException.ThrowIfNull(log);
        int failures = 0;
        failures += Check(log, dump, "Application", () => new DemoApplication(), "Demo Window", "File", "Demo Main Window");
        failures += Check(log, dump, "Desktop", () => new DemoApplication(openMainWindow: false), "Window", "Nexer demo");
        foreach (var (title, create) in Windows)
        {
            failures += Check(log, dump, title, () =>
            {
                var app = new DemoApplication(openMainWindow: false);
                app.Windows.Add(create());
                return app;
            }, title);
        }

        log.WriteLine(failures == 0 ? "smoke: OK" : $"smoke: {failures} failure(s)");
        return failures == 0 ? 0 : 1;
    }

    private static int Check(TextWriter log, bool dump, string name, Func<Application> create, params string[] expected)
    {
        try
        {
            var driver = new UiDriver(create(), 100, 30);
            string text = driver.Text();
            var missing = expected.Where(e => !text.Contains(e, StringComparison.Ordinal)).ToList();
            if (missing.Count == 0)
            {
                log.WriteLine($"ok    {name}");
                if (dump) log.WriteLine(text);
                return 0;
            }

            log.WriteLine($"FAIL  {name}: missing {string.Join(", ", missing.Select(m => $"'{m}'"))}");
            log.WriteLine(text);
            return 1;
        }
        catch (Exception ex)
        {
            log.WriteLine($"FAIL  {name}: {ex}");
            return 1;
        }
    }
}
