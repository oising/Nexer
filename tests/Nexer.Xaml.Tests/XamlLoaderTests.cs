using Nexer.Core;
using Nexer.Ui.Markup;
using Nexer.Xaml.Compiler;
using Nexer.Xaml.Fixtures;

namespace Nexer.Xaml.Tests;

public class XamlLoaderTests
{
    private const string Ns = "xmlns='https://nexer.dev/ui/test-fixtures' xmlns:n='https://nexer.dev/ui' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'";

    [Fact]
    public void BuildsObjectTreeWithPropertiesAndContent()
    {
        var window = XamlLoader.Load<Window>($"""
            <Window {Ns} Title='Main' Width='80' Height='25' Visibility='Hidden'>
              <Button Text='&amp;OK' X='2' />
              <Label>Hello</Label>
            </Window>
            """);

        Assert.Equal("Main", window.Title);
        Assert.Equal((80, 25), (window.Width, window.Height));
        Assert.Equal(Visibility.Hidden, window.Visibility);
        Assert.Collection(window.Children,
            c => Assert.Equal("&OK", Assert.IsType<Button>(c).Text),
            c => Assert.Equal("Hello", Assert.IsType<Label>(c).Text));
        Assert.All(window.Children, c => Assert.Same(window, c.Parent));
    }

    [Fact]
    public void ConvertsNexerValueTypesAtCompileTime()
    {
        var before = Rect.ParseCalls;
        var widget = XamlLoader.Load<Widget>($"""
            <Widget {Ns} Bounds='1, 2, 30, 4' Margin='1,2' Style='bold white on blue'
                    FocusStyle='#102030 on default' Foreground='#FF8000' />
            """);

        Assert.Equal(new Rect(1, 2, 30, 4), widget.Bounds);
        Assert.Equal(before, Rect.ParseCalls);
        Assert.Equal(new Thickness(1, 2, 1, 2), widget.Margin);
        Assert.Equal(new CellStyle(TerminalColor.Indexed(7), TerminalColor.Indexed(4), CellStyleFlags.Bold), widget.Style);
        Assert.Equal(new CellStyle(TerminalColor.Rgb(0x10, 0x20, 0x30), TerminalColor.Default, CellStyleFlags.None), widget.FocusStyle);
        Assert.Equal(TerminalColor.Rgb(0xFF, 0x80, 0x00), widget.Foreground);
    }

    [Fact]
    public void ParsesKeyGestureCommandAndEnums()
    {
        var window = XamlLoader.Load<Window>($"""
            <Window {Ns} Anchors='left, Top' CloseGesture='Ctrl+Q'>
              <Button Command='Save' Shortcut='Alt+Shift+F10' />
            </Window>
            """);

        Assert.Equal(Anchors.Left | Anchors.Top, window.Anchors);
        Assert.Equal(new KeyGesture("Q", true, false, false), window.CloseGesture);
        var button = Assert.IsType<Button>(window.Children[0]);
        Assert.Equal(new Command("Save"), button.Command);
        Assert.Equal(new KeyGesture("F10", false, true, true), button.Shortcut);
    }

    [Fact]
    public void SetsAttachedProperties()
    {
        var window = XamlLoader.Load<Window>($"<Window {Ns}><Button Dock.Edge='Bottom' /></Window>");

        Assert.Equal(DockEdge.Bottom, Dock.GetEdge(window.Children[0]));
    }

    [Fact]
    public void PopulatesRootInstanceAndBindsEventHandlers()
    {
        var window = new HandlerWindow();
        var result = XamlLoader.Load($"""
            <Window {Ns} Title='Handlers'>
              <Button Click='OnClick' />
              <Button Click='OnPrivateClick' />
            </Window>
            """, window);

        Assert.Same(window, result);
        Assert.Equal("Handlers", window.Title);
        ((Button)window.Children[0]).PerformClick();
        ((Button)window.Children[1]).PerformClick();
        ((Button)window.Children[1]).PerformClick();
        Assert.Equal(1, window.Clicks);
        Assert.Equal(2, window.PrivateClicks);
    }

    [Fact]
    public void XNameSetsNameAndRegistersInRootNameScope()
    {
        var window = XamlLoader.Load<Window>($"""
            <Window {Ns} x:Name='root'>
              <Button x:Name='ok' />
              <Label Name='caption' />
            </Window>
            """);

        Assert.Equal("ok", window.Children[0].Name);
        Assert.Same(window.Children[0], NameScope.FindName(window, "ok"));
        Assert.Same(window.Children[1], NameScope.FindName<Label>(window, "caption"));
        Assert.Same(window, NameScope.FindName(window, "root"));
        Assert.Equal("root", window.Name);
    }

    [Fact]
    public void StaticResourceResolvesFromEnclosingResources()
    {
        var window = XamlLoader.Load<Window>($"""
            <Window {Ns} xmlns:sys='clr-namespace:System;assembly=System.Runtime'>
              <Window.Resources>
                <sys:String x:Key='title'>From resources</sys:String>
                <Label x:Key='quit' Text='Quit' />
              </Window.Resources>
              <Widget>
                <Button Text='{"{n:StaticResource title}"}' Tag='{"{n:StaticResource ResourceKey=quit}"}' />
              </Widget>
            </Window>
            """);

        var button = (Button)window.Children[0].Children[0];
        Assert.Equal("From resources", button.Text);
        Assert.Equal("Quit", Assert.IsType<Label>(button.Tag).Text);
    }

    [Fact]
    public void StaticResourceFallsBackToServiceProvider()
    {
        var app = new Application();
        app.Resources["accent"] = "Accent";
        var button = XamlLoader.Load<Button>($"<Button {Ns} Text='{{n:StaticResource accent}}' />", services: new Services(app));

        Assert.Equal("Accent", button.Text);
    }

    [Fact]
    public void MissingStaticResourceThrowsClearError()
    {
        var error = Assert.Throws<KeyNotFoundException>(() =>
            XamlLoader.Load<Button>($"<Button {Ns} Text='{{n:StaticResource missing}}' />"));

        Assert.Contains("'missing'", error.Message);
        Assert.Contains("Text", error.Message);
    }

    [Fact]
    public void SupportsXNullXTypeAndXStatic()
    {
        var window = XamlLoader.Load<Window>($"""
            <Window {Ns} Title='{"{x:Null}"}' Tag='{"{x:Type Button}"}'>
              <Button Text='{"{x:Static Constants.Greeting}"}' Command='{"{x:Static Command.Quit}"}' Tag='{"{x:Static Constants.Answer}"}' />
            </Window>
            """);

        Assert.Null(window.Title);
        Assert.Equal(typeof(Button), window.Tag);
        var button = (Button)window.Children[0];
        Assert.Equal("Hello", button.Text);
        Assert.Same(Command.Quit, button.Command);
        Assert.Equal(42, button.Tag);
    }

    [Fact]
    public void DeferredContentBecomesFactory()
    {
        var window = XamlLoader.Load<Window>($"""
            <Window {Ns}>
              <Window.Template>
                <Widget><Button Text='Templated' /></Widget>
              </Window.Template>
            </Window>
            """);

        var factory = Assert.IsType<Func<IServiceProvider?, object>>(window.Template, exactMatch: false);
        var first = Assert.IsType<Widget>(factory(null));
        var second = factory(null);
        Assert.NotSame(first, second);
        Assert.Equal("Templated", ((Button)first.Children[0]).Text);
    }

    [Fact]
    public void InitializesTopDownWithSupportInitialize()
    {
        var window = XamlLoader.Load<Window>($"<Window {Ns}><Button Bounds='0,0,1,1' /></Window>");

        var button = window.Children[0];
        Assert.True(button.ParentKnownWhenBoundsSet);
        Assert.Equal(["BeginInit", "EndInit"], button.InitLog);
    }

    [Fact]
    public void CachesCompiledDocuments()
    {
        var xaml = $"<Label {Ns}>Cached {Guid.NewGuid()}</Label>";
        XamlLoader.Load(xaml);
        var count = XamlLoader.CacheCount;
        var second = XamlLoader.Load<Label>(xaml);

        Assert.Equal(count, XamlLoader.CacheCount);
        Assert.StartsWith("Cached", second.Text);
    }

    [Fact]
    public void ReportsDiagnosticsInMsBuildFormat()
    {
        var error = Assert.Throws<NexerXamlCompilationException>(() => XamlLoader.Load(
            $"<Window {Ns}>\n  <Button Nope='1' />\n</Window>", baseUri: new Uri("file:///c:/app/Main.xaml")));

        var diagnostic = Assert.Single(error.Diagnostics, d => d.Severity == NexerXamlSeverity.Error);
        Assert.Equal(NexerXamlDiagnosticCodes.General, diagnostic.Code);
        Assert.Equal(2, diagnostic.Line);
        Assert.Matches(@"^file:///c:/app/Main\.xaml\(2,\d+\): error NXAML0001: .*Nope", diagnostic.ToString());
    }

    [Theory]
    [InlineData("<Widget {0} Style='bold purple on blue' />", "NXAML0002", "Invalid CellStyle value")]
    [InlineData("<Widget {0} Bounds='1,2,3' />", "NXAML0002", "Invalid Rect value")]
    [InlineData("<Widget {0} Visibility='Gone' />", "NXAML0002", "Invalid Visibility value")]
    [InlineData("<Window {0} CloseGesture='Hyper+Q' />", "NXAML0002", "Invalid KeyGesture value")]
    [InlineData("<Window {0}><Button x:Name='a' /><Button x:Name='a' /></Window>", "NXAML0004", "'a' is already used")]
    [InlineData("<Window {0} x:Class='Nope.Missing' />", "NXAML0003", "Nope.Missing")]
    [InlineData("<Window {0}><Button x:Class='Nexer.Xaml.Fixtures.Button' /></Window>", "NXAML0006", "x:Class")]
    [InlineData("<Window {0}><Button></Window>", "NXAML0005", "")]
    public void ReportsNexerDiagnosticCodes(string template, string code, string message)
    {
        var diagnostics = XamlLoader.Validate(string.Format(System.Globalization.CultureInfo.InvariantCulture, template, Ns));

        var diagnostic = Assert.Single(diagnostics, d => d.Severity == NexerXamlSeverity.Error);
        Assert.Equal(code, diagnostic.Code);
        Assert.Contains(message, diagnostic.Message);
        Assert.True(diagnostic.Line > 0);
    }

    [Fact]
    public void CollectsMultipleErrors()
    {
        var diagnostics = XamlLoader.Validate($"<Widget {Ns} Style='nope' Bounds='x' Missing='1' />");

        Assert.Equal(3, diagnostics.Count(d => d.Severity == NexerXamlSeverity.Error));
    }

    [Fact]
    public void XClassMustMatchRootInstance()
    {
        var diagnostics = XamlLoader.Validate($"<Window {Ns} x:Class='Nexer.Xaml.Fixtures.HandlerWindow' />", typeof(Window));

        Assert.Equal(NexerXamlDiagnosticCodes.ClassNotFound, Assert.Single(diagnostics).Code);
    }

    [Fact]
    public void XClassWithoutInstanceBuildsClassType()
    {
        var window = XamlLoader.Load<HandlerWindow>($"""
            <Window {Ns} x:Class='Nexer.Xaml.Fixtures.HandlerWindow' x:ClassModifier='internal' Title='X'>
              <Button Click='OnClick' x:FieldModifier='public' />
            </Window>
            """);

        Assert.Equal("X", window.Title);
        ((Button)window.Children[0]).PerformClick();
        Assert.Equal(1, window.Clicks);
    }

    private sealed class Services(IResourceProvider resources) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IResourceProvider) ? resources : null;
    }
}
