using System.Reflection;
using Mono.Cecil;
using Nexer.Core;
using Nexer.Ui.Markup;
using Nexer.Xaml.Compiler;
using Nexer.Xaml.Fixtures;
using Nexer.Xaml.TestApp;
using Nexer.Xaml.TestApp.Dialogs;

namespace Nexer.Xaml.Tests;

/// <summary>Tests the build-time (Cecil) path through the Nexer.Xaml.TestApp fixture, compiled by Nexer.Xaml.targets.</summary>
public class CompiledXamlTests
{
    private static readonly Assembly s_app = typeof(MainWindow).Assembly;

    [Fact]
    public void CodeBehindConstructorLoadsCompiledXaml()
    {
        var window = new MainWindow();

        Assert.Equal("Compiled", window.Title);
        Assert.Equal(new Rect(0, 0, 80, 25), window.Bounds);
        Assert.Equal(new CellStyle(TerminalColor.Indexed(7), TerminalColor.Indexed(4), CellStyleFlags.None), window.Style);
        Assert.Equal(new KeyGesture("Q", true, false, false), window.CloseGesture);
        Assert.Collection(window.Children,
            ok =>
            {
                var button = Assert.IsType<Button>(ok);
                Assert.Equal("&OK", button.Text);
                Assert.Equal(new Thickness(1, 1, 1, 1), button.Margin);
                Assert.Equal(DockEdge.Bottom, Dock.GetEdge(button));
                Assert.Equal("Shared", Assert.IsType<Label>(button.Tag).Text);
            },
            quit => Assert.Same(Command.Quit, Assert.IsType<Button>(quit).Command));
    }

    [Fact]
    public void PrivateEventHandlersAreWired()
    {
        var window = new MainWindow();

        ((Button)window.Children[0]).PerformClick();
        ((Button)window.Children[1]).PerformClick();

        Assert.Equal(1, window.OkClicks);
        Assert.True(window.QuitRequested);
    }

    [Fact]
    public void NamesAreRegisteredInTheRootNameScope()
    {
        var window = new MainWindow();

        Assert.Same(window.Children[0], NameScope.FindName(window, "OkButton"));
        Assert.Equal("QuitButton", window.Children[1].Name);
    }

    [Fact]
    public void DeferredContentIsCompiled()
    {
        var window = new MainWindow();

        var factory = Assert.IsType<Func<IServiceProvider?, object>>(window.Template, exactMatch: false);
        Assert.Equal("Templated", Assert.IsType<Label>(factory(null)).Text);
    }

    [Fact]
    public void DocumentsAreRegisteredByLogicalName()
    {
        var documents = XamlComponent.GetDocuments(s_app);

        Assert.Equal(["Dialogs/ConfirmDialog.xaml", "MainWindow.xaml", "Views/About.xaml"], documents.Select(d => d.LogicalName));
        Assert.True(XamlComponent.IsRegistered(typeof(MainWindow)));
        Assert.Equal(typeof(MainWindow), documents[1].ComponentType);
    }

    [Fact]
    public void BuildsDocumentsWithoutClass()
    {
        var about = Assert.IsType<Label>(XamlComponent.Build(s_app, "Views/About.xaml"));

        Assert.Equal("About Nexer", about.Text);
        Assert.Equal(new CellStyle(TerminalColor.Indexed(3), TerminalColor.Default, CellStyleFlags.Bold), about.Style);
        Assert.Same(about, NameScope.FindName(about, "about"));
    }

    [Fact]
    public void BuildOfClassDocumentUsesConstructor()
    {
        var window = Assert.IsType<MainWindow>(XamlComponent.Build(s_app, "MainWindow.xaml"));

        Assert.Equal(2, window.Children.Count);
    }

    [Fact]
    public void GeneratedInitializeComponentAssignsTypedFields()
    {
        var dialog = new ConfirmDialog();

        Assert.Equal("Confirm", dialog.Title);
        Assert.Equal("Are you sure?", dialog.MessageLabel.Text);
        Assert.Equal("&No", dialog.NoButtonField.Text);
        Button yes = dialog.YesButton;
        yes.PerformClick();
        Assert.True(dialog.Confirmed);
        Assert.Same(dialog.Children[1].Children[0], yes);
    }

    [Fact]
    public void AssemblyKeepsSymbolsAndIsMarkedAsProcessed()
    {
        var path = s_app.Location;
        using var module = ModuleDefinition.ReadModule(path, new ReaderParameters { ReadSymbols = true });

        Assert.True(module.HasSymbols);
        Assert.NotNull(module.GetType(NexerXamlAssemblyCompiler.GeneratedNamespace, NexerXamlAssemblyCompiler.RegistrationTypeName));
        Assert.Contains(module.GetType("Nexer.Xaml.TestApp.MainWindow").Methods, m => m.Name == "!NexerXaml.Populate");
    }

    [Fact]
    public void ReprocessingIsANoOp()
    {
        var output = Path.Combine(Path.GetDirectoryName(typeof(CompiledXamlTests).Assembly.Location)!, "reprocess", "Nexer.Xaml.TestApp.dll");
        var refs = PlatformAssemblies()
            .Append(typeof(XamlComponent).Assembly.Location)
            .Append(typeof(Widget).Assembly.Location);

        var result = NexerXamlAssemblyCompiler.Compile(CopyToScratch(s_app.Location, "reprocess-in"), refs, [], output);

        Assert.True(result.Success);
        Assert.True(result.AlreadyCompiled);
    }

    [Fact]
    public void ReportsDiagnosticsWithFilePaths()
    {
        var dir = Path.Combine(Path.GetDirectoryName(typeof(CompiledXamlTests).Assembly.Location)!, "cecil-errors");
        Directory.CreateDirectory(dir);
        var xaml = Path.Combine(dir, "Broken.xaml");
        File.WriteAllText(xaml, "<Window xmlns='https://nexer.dev/ui'>\n  <Button Style='bold on nowhere' />\n</Window>");
        var refs = PlatformAssemblies()
            .Append(typeof(XamlComponent).Assembly.Location)
            .Append(typeof(Nexer.Core.CellStyle).Assembly.Location);

        var result = NexerXamlAssemblyCompiler.Compile(CopyToScratch(typeof(Widget).Assembly.Location, "cecil-errors-in"), refs,
            [new NexerXamlItem(xaml, "Broken.xaml")], Path.Combine(dir, "out.dll"));

        Assert.False(result.Success);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.StartsWith(xaml + "(2,", diagnostic.ToString());
        Assert.Contains(": error NXAML0002: ", diagnostic.ToString());
        Assert.False(File.Exists(Path.Combine(dir, "out.dll")));
    }

    private static IEnumerable<string> PlatformAssemblies() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(p) == "netstandard.dll");

    // The compiler opens its input for writing, which fails for assemblies loaded into the test process.
    private static string CopyToScratch(string assemblyPath, string folder)
    {
        var dir = Path.Combine(Path.GetDirectoryName(typeof(CompiledXamlTests).Assembly.Location)!, folder);
        Directory.CreateDirectory(dir);
        var copy = Path.Combine(dir, Path.GetFileName(assemblyPath));
        File.Copy(assemblyPath, copy, true);
        return copy;
    }
}