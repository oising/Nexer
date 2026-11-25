using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Nexer.Xaml.Generators;

namespace Nexer.Xaml.Tests;

public class GeneratorTests
{
    private const string Ns = "xmlns='https://nexer.dev/ui/test-fixtures' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'";

    private const string CodeBehind = """
        namespace App;
        public partial class MainView : Nexer.Xaml.Fixtures.Window
        {
            public MainView() => InitializeComponent();
        }
        """;

    [Fact]
    public void GeneratesTypedFieldsAndInitializeComponent()
    {
        var (output, diagnostics, generated) = Run(CodeBehind, $"""
            <Window {Ns} x:Class='App.MainView' xmlns:local='clr-namespace:App'>
              <Button x:Name='Ok' x:FieldModifier='public' />
              <Label Name='Caption' />
              <local:Custom x:Name='Mine' />
              <Unknown x:Name='Other' />
              <Window.Template><Label x:Name='InTemplate' /></Window.Template>
            </Window>
            """, "namespace App { public class Custom : Nexer.Xaml.Fixtures.Widget { } }");

        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("public global::Nexer.Xaml.Fixtures.Button Ok = default!;", generated);
        Assert.Contains("internal global::Nexer.Xaml.Fixtures.Label Caption = default!;", generated);
        Assert.Contains("internal global::App.Custom Mine = default!;", generated);
        Assert.Contains("internal object Other = default!;", generated);
        Assert.DoesNotContain("InTemplate", generated);
        Assert.Contains("global::Nexer.Ui.Markup.XamlComponent.InitializeComponent<global::App.MainView>(this, services);", generated);
        Assert.Contains("this.Ok = (global::Nexer.Xaml.Fixtures.Button)global::Nexer.Ui.Markup.NameScope.FindName(this, \"Ok\")!;", generated);
    }

    [Fact]
    public void SupportsNestedAndGlobalNamespaceTypes()
    {
        var (output, diagnostics, generated) = Run("""
            public partial class Outer
            {
                public partial class Inner : Nexer.Xaml.Fixtures.Widget { }
            }
            """, $"<Widget {Ns} x:Class='Outer+Inner'><Button x:Name='B' /></Widget>");

        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("partial class Outer", generated);
        Assert.Contains("partial class Inner", generated);
    }

    [Fact]
    public void KeepsUserDefinedInitializeComponent()
    {
        var (output, diagnostics, generated) = Run("""
            namespace App;
            public partial class MainView : Nexer.Xaml.Fixtures.Window
            {
                private void InitializeComponent() => Nexer.Ui.Markup.XamlComponent.InitializeComponent(this);
            }
            """, $"<Window {Ns} x:Class='App.MainView'><Button x:Name='Ok' /></Window>");

        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(" Ok = default!;", generated);
        Assert.DoesNotContain("void InitializeComponent", generated);
    }

    [Theory]
    [InlineData("namespace App; public class MainView : Nexer.Xaml.Fixtures.Window { }", "<Window {0} x:Class='App.MainView' />", "NXAMLG002")]
    [InlineData("namespace App; public class Other { }", "<Window {0} x:Class='App.MainView' />", "NXAMLG001")]
    [InlineData("namespace App; public partial class MainView : Nexer.Xaml.Fixtures.Window { }", "<Window {0} x:Class='App.MainView'><Button x:Name='Title' /></Window>", "NXAMLG003")]
    [InlineData("namespace App; public partial class MainView : Nexer.Xaml.Fixtures.Window { }", "<Window {0} x:Class='App.MainView'><Button x:Name='1st' /></Window>", "NXAMLG003")]
    public void ReportsGeneratorDiagnostics(string source, string xamlTemplate, string id)
    {
        var (_, diagnostics, _) = Run(source, string.Format(System.Globalization.CultureInfo.InvariantCulture, xamlTemplate, Ns));

        Assert.Equal(id, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public void IgnoresXamlWithoutClassOrInvalidXml()
    {
        var (_, diagnostics, generated) = Run(CodeBehind, $"<Window {Ns}><Button x:Name='A' /></Window>", xaml2: "<Window");

        Assert.Empty(diagnostics);
        Assert.Equal(string.Empty, generated);
    }

    [Fact]
    public void CachesUnchangedDocuments()
    {
        var xaml = new InMemoryAdditionalText("MainView.xaml", $"<Window {Ns} x:Class='App.MainView'><Button x:Name='Ok' /></Window>");
        var compilation = CreateCompilation(CodeBehind);
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new XamlCodeBehindGenerator().AsSourceGenerator()], [xaml],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);
        driver = driver.RunGenerators(compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("class Unrelated { }")));

        var steps = driver.GetRunResult().Results[0].TrackedSteps;
        Assert.Contains(steps.SelectMany(s => s.Value).SelectMany(s => s.Outputs), o => o.Reason == IncrementalStepRunReason.Cached);
    }

    private static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics, string Generated) Run(string source, string xaml, string? extraSource = null, string? xaml2 = null)
    {
        var compilation = CreateCompilation(source, extraSource);
        var texts = new List<AdditionalText> { new InMemoryAdditionalText("Views/MainView.xaml", xaml) };
        if (xaml2 is not null)
        {
            texts.Add(new InMemoryAdditionalText("Views/Broken.xaml", xaml2));
        }

        texts.Add(new InMemoryAdditionalText("readme.txt", "<Window x:Class='Nope' />"));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new XamlCodeBehindGenerator().AsSourceGenerator()], texts);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var generated = string.Concat(driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()));
        return (output, diagnostics, generated);
    }

    private static CSharpCompilation CreateCompilation(string source, string? extraSource = null)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(p) is "netstandard.dll" or "mscorlib.dll")
            .Append(typeof(Fixtures.Widget).Assembly.Location)
            .Append(typeof(Ui.Markup.XamlComponent).Assembly.Location)
            .Append(typeof(Core.CellStyle).Assembly.Location)
            .Select(p => MetadataReference.CreateFromFile(p));
        var trees = new[] { source, extraSource }.Where(s => s is not null).Select(s => CSharpSyntaxTree.ParseText(s!));
        return CSharpCompilation.Create("App", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }
}
