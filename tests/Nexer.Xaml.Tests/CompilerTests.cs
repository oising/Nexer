using Nexer.Xaml.Compiler;
using XamlX.IL;

namespace Nexer.Xaml.Tests;

public class CompilerTests
{
    private const string Ns = "xmlns='https://nexer.dev/ui/test-fixtures' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'";

    private static NexerXamlCompiler CreateCompiler()
    {
        _ = typeof(Fixtures.Widget).Assembly;
        _ = typeof(Ui.Markup.NameScope).Assembly;
        var ts = new SreTypeSystem();
        return new NexerXamlCompiler(ts, ts.FindAssembly(typeof(Fixtures.Widget).Assembly.GetName().Name!));
    }

    [Fact]
    public void ConfigurationFindsMarkupContracts()
    {
        var compiler = CreateCompiler();

        Assert.True(NexerXamlConfiguration.HasMarkupContracts(compiler.TypeSystem));
        Assert.NotNull(compiler.Configuration.TypeMappings.ContentAttributes.SingleOrDefault());
        Assert.NotNull(compiler.Configuration.TypeMappings.RootObjectProvider);
    }

    [Fact]
    public void ParseResolvesXClassAsRootType()
    {
        var compiler = CreateCompiler();

        var document = compiler.Parse($"<Window {Ns} x:Class='Nexer.Xaml.Fixtures.HandlerWindow' />", "Main.xaml");

        Assert.NotNull(document);
        Assert.Equal("Nexer.Xaml.Fixtures.HandlerWindow", document.ClassType?.FullName);
        Assert.Equal("Nexer.Xaml.Fixtures.HandlerWindow", document.RootType.FullName);
        Assert.False(compiler.HasErrors);
    }

    [Fact]
    public void ParseReportsFileBasedDiagnostics()
    {
        var compiler = CreateCompiler();

        var document = compiler.Parse($"<Widget {Ns}\n Style='bold on nowhere' />", @"C:\app\Bad.xaml");

        Assert.Null(document);
        var diagnostic = Assert.Single(compiler.Diagnostics);
        Assert.StartsWith(@"C:\app\Bad.xaml(2,", diagnostic.ToString());
        Assert.Contains("error NXAML0002", diagnostic.ToString());
    }
}
