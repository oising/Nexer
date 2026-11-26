using Nexer.Ui.Data;
using Nexer.Xaml.Compiler;
using Nexer.Xaml.Fixtures.Binding;
using UiWidget = Nexer.Ui.Widget;

namespace Nexer.Xaml.Tests;

/// <summary>Compiled <c>{Binding}</c> through the SRE loader against fixture widgets: path grammar, sources, modes, diagnostics.</summary>
public class BindingCompilerTests
{
    private const string Ns = "xmlns='https://nexer.dev/ui' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' "
        + "xmlns:f='https://nexer.dev/ui/test-fixtures' xmlns:fx='clr-namespace:Nexer.Xaml.Fixtures.Binding;assembly=Nexer.Xaml.Fixtures'";

    private const string PersonType = "x:DataType='f:Person'";

    // The body starts on line 2 so diagnostics can be asserted by line.
    private static string Xaml(string body, string attributes) => $"<f:BindingHost {Ns} {attributes}>\n{body}\n</f:BindingHost>";

    private static BindingHost Load(string body, string attributes = PersonType) => XamlLoader.Load<BindingHost>(Xaml(body, attributes));

    private static BindingHost At(BindingHost root, int index) => (BindingHost)root.Children[index];

    private static CompiledBinding BindingOf(UiWidget widget, string property) =>
        Assert.IsType<CompiledBinding>(BindingOperations.GetBindingExpression(widget, property)?.Binding);

    private static NexerXamlDiagnostic Error(string body, string attributes = PersonType)
    {
        var exception = Assert.Throws<NexerXamlCompilationException>(() => Load(body, attributes));
        return exception.Diagnostics.First(d => d.Severity == NexerXamlSeverity.Error);
    }

    private static Person Sample() => new()
    {
        Name = "Ada",
        Age = 36,
        Friend = new Person { Name = "Charles" },
        Pet = new Dog { Name = "Rex", Breed = "Collie" },
        Home = new Location { City = "London" },
        Tags = ["a", "b"],
        Friends = [new Person { Name = "Mary" }],
        Map = new() { ["k"] = "v", ["two words"] = "w" },
        Scores = [7, 8],
        Children = [new Person { Name = "Byron" }],
    };

    [Fact]
    public void PropertyPathsResolveAgainstDataContext()
    {
        var root = Load("<f:BindingHost Text='{Binding Name}' /><f:BindingHost Text='{Binding Friend.Name}' /><f:BindingHost Number='{Binding Age}' />");

        root.DataContext = Sample();

        Assert.Equal(("Ada", "Charles", 36), (At(root, 0).Text, At(root, 1).Text, At(root, 2).Number));
        var path = BindingOf(At(root, 1), nameof(BindingHost.Text)).Path!;
        Assert.Equal(BindingSourceKind.DataContext, path.Source);
        Assert.Equal(["Friend", "Name"], path.Nodes.Select(n => n.Name));
        Assert.Equal([typeof(Person), typeof(string)], path.Nodes.Select(n => n.ValueType));
        Assert.All(path.Nodes, n => Assert.True(n.Notifies));
        Assert.All(path.Nodes, n => Assert.NotNull(n.Set));
        Assert.Same(BindingOperations.UnsetValue, path.Nodes[1].Get(new Pet()));
    }

    [Theory]
    [InlineData("{Binding}")]
    [InlineData("{Binding .}")]
    [InlineData("{Binding Path=.}")]
    public void EmptyPathBindsTheSourceItself(string markup)
    {
        var root = Load($"<f:BindingHost Value='{markup}' />");
        var person = Sample();

        root.DataContext = person;

        Assert.Same(person, At(root, 0).Value);
        Assert.Empty(BindingOf(At(root, 0), nameof(BindingHost.Value)).Path!.Nodes);
    }

    [Fact]
    public void IndexersResolveIntegerAndStringArguments()
    {
        var root = Load("""
            <f:BindingHost Text='{Binding Tags[1]}' />
            <f:BindingHost Number='{Binding Scores[1]}' />
            <f:BindingHost Text='{Binding Friends[0].Name}' />
            <f:BindingHost Text='{Binding Map[k]}' />
            <f:BindingHost Text="{Binding 'Map[two words]'}" />
            <f:BindingHost Text='{Binding "Map[&apos;k&apos;]"}' />
            """);

        root.DataContext = Sample();

        Assert.Equal("b", At(root, 0).Text);
        Assert.Equal(8, At(root, 1).Number);
        Assert.Equal("Mary", At(root, 2).Text);
        Assert.Equal("v", At(root, 3).Text);
        Assert.Equal("w", At(root, 4).Text);
        Assert.Equal("v", At(root, 5).Text);
        var nodes = BindingOf(At(root, 0), nameof(BindingHost.Text)).Path!.Nodes;
        Assert.Equal(["Tags", "Item[]"], nodes.Select(n => n.Name));
        Assert.Equal(typeof(string), nodes[1].ValueType);
        var tags = new List<string> { "x", "x" };
        nodes[1].Set!(tags, "y");
        Assert.Equal("y", tags[1]);
        Assert.Equal(["Friends", "Item[]", "Name"], BindingOf(At(root, 2), nameof(BindingHost.Text)).Path!.Nodes.Select(n => n.Name));
        Assert.Null(BindingOf(At(root, 2), nameof(BindingHost.Text)).Path!.Nodes[1].Set);
    }

    [Fact]
    public void ValueTypeOwnersCastsAndAttachedPropertiesResolve()
    {
        var root = Load("""
            <f:BindingHost Text='{Binding Home.City}' />
            <f:BindingHost Text='{Binding ((f:Dog)Pet).Breed}' />
            <f:BindingHost f:Tag.Label='tagged' Text='{Binding (f:Tag.Label), RelativeSource={RelativeSource Self}}' />
            """);

        root.DataContext = Sample();

        Assert.Equal("London", At(root, 0).Text);
        Assert.Equal("Collie", At(root, 1).Text);
        Assert.Equal("tagged", At(root, 2).Text);
        Assert.Equal(["Pet", "Breed"], BindingOf(At(root, 1), nameof(BindingHost.Text)).Path!.Nodes.Select(n => n.Name));
        var attached = Assert.Single(BindingOf(At(root, 2), nameof(BindingHost.Text)).Path!.Nodes);
        Assert.Equal("(Tag.Label)", attached.Name);
        Assert.Null(attached.Set);
        Assert.False(attached.Notifies);
    }

    [Fact]
    public void SelfElementNameAndAncestorSourcesCompile()
    {
        var root = Load("""
            <f:BindingHost Text='{Binding Text, ElementName=Later}' />
            <f:BindingHost Number='5' Text='{Binding Number, RelativeSource={RelativeSource Self}}' />
            <f:DerivedHost Text='inner'>
              <f:BindingHost Text='{Binding Text, RelativeSource={RelativeSource FindAncestor, AncestorType=f:BindingHost, AncestorLevel=2}}' />
              <f:BindingHost Text='{Binding Text, RelativeSource={RelativeSource Mode=FindAncestor, AncestorType=f:DerivedHost}}' />
            </f:DerivedHost>
            <f:BindingHost x:Name='Later' Text='named' />
            """, "Text='outer'");

        var element = BindingOf(At(root, 0), nameof(BindingHost.Text)).Path!;
        Assert.Equal((BindingSourceKind.ElementName, "Later"), (element.Source, element.ElementName));
        Assert.Equal(BindingSourceKind.Self, BindingOf(At(root, 1), nameof(BindingHost.Text)).Path!.Source);
        Assert.Equal("5", At(root, 1).Text);
        var ancestors = At(root, 2);
        var level2 = BindingOf(At(ancestors, 0), nameof(BindingHost.Text)).Path!;
        Assert.Equal((BindingSourceKind.FindAncestor, typeof(BindingHost), 2), (level2.Source, level2.AncestorType, level2.AncestorLevel));
        var derived = BindingOf(At(ancestors, 1), nameof(BindingHost.Text)).Path!;
        Assert.Equal((typeof(DerivedHost), 1), (derived.AncestorType, derived.AncestorLevel));
    }

    [Theory]
    [InlineData("Editable='{Binding Name}'", nameof(BindingHost.Editable), BindingMode.TwoWay)]
    [InlineData("Editable='{Binding Name, Mode=Default}'", nameof(BindingHost.Editable), BindingMode.TwoWay)]
    [InlineData("Editable='{Binding Name, Mode=OneWay}'", nameof(BindingHost.Editable), BindingMode.OneWay)]
    [InlineData("Editable='{Binding ReadOnlyName, Mode=OneTime}'", nameof(BindingHost.Editable), BindingMode.OneTime)]
    [InlineData("Text='{Binding Name}'", nameof(BindingHost.Text), BindingMode.Default)]
    [InlineData("Text='{Binding Name, Mode=OneWayToSource}'", nameof(BindingHost.Text), BindingMode.OneWayToSource)]
    [InlineData("Sink='{Binding Name}'", nameof(BindingHost.Sink), BindingMode.Default)]
    public void DefaultModeIsResolvedFromBindsTwoWayByDefault(string attribute, string property, BindingMode expected)
    {
        var root = Load($"<f:BindingHost {attribute} />");

        Assert.Equal(expected, BindingOf(At(root, 0), property).Mode);
    }

    [Fact]
    public void BindingPropertiesAreCompiled()
    {
        var root = Load("""
            <f:BindingHost.Resources><f:PrefixConverter x:Key='prefix' /></f:BindingHost.Resources>
            <f:BindingHost Text='{Binding Name, Converter={StaticResource prefix}, ConverterParameter=pre-, FallbackValue=none,
                TargetNullValue=null, StringFormat=[{0}], UpdateSourceTrigger=LostFocus, ValidatesOnExceptions=False,
                ValidatesOnNotifyDataErrors=False}' />
            <f:BindingHost Number='{Binding Friend.Age, FallbackValue=-1}' />
            """);

        root.DataContext = Sample();

        var binding = BindingOf(At(root, 0), nameof(BindingHost.Text));
        Assert.IsType<PrefixConverter>(binding.Converter);
        Assert.Equal("pre-", binding.ConverterParameter);
        Assert.Equal("none", binding.FallbackValue);
        Assert.Equal("null", binding.TargetNullValue);
        Assert.Equal("[{0}]", binding.StringFormat);
        Assert.Equal(UpdateSourceTrigger.LostFocus, binding.UpdateSourceTrigger);
        Assert.False(binding.ValidatesOnExceptions);
        Assert.False(binding.ValidatesOnNotifyDataErrors);
        Assert.Equal("[pre-Ada]", At(root, 0).Text);
        Assert.Same(BindingOperations.UnsetValue, BindingOf(At(root, 1), nameof(BindingHost.Number)).TargetNullValue);
    }

    [Fact]
    public void DataTypeIsInheritedOverriddenAndResetInDeferredContent()
    {
        var root = Load("""
            <f:BindingHost x:DataType='f:Pet'><f:BindingHost Text='{Binding Name}' /></f:BindingHost>
            <f:BindingHost Text='{Binding Breed, DataType=f:Dog}' />
            <f:BindingHost><f:BindingHost.Template><f:BindingHost x:DataType='f:Dog' Text='{Binding Breed}' /></f:BindingHost.Template></f:BindingHost>
            """);

        Assert.Equal(typeof(string), BindingOf(At(At(root, 0), 0), nameof(BindingHost.Text)).Path!.ValueType);
        Assert.Equal(["Breed"], BindingOf(At(root, 1), nameof(BindingHost.Text)).Path!.Nodes.Select(n => n.Name));
        var error = Error("<f:BindingHost><f:BindingHost.Template><f:BindingHost Text='{Binding Name}' /></f:BindingHost.Template></f:BindingHost>");
        Assert.Equal((NexerXamlDiagnosticCodes.MissingDataType, 2), (error.Code, error.Line));
    }

    [Fact]
    public void ItemBindingsInferTheItemTypeFromItemsSource()
    {
        var root = Load("""
            <f:BindingHost DisplayMemberBinding='{Binding Name}' ItemsSource='{Binding Friends}' ChildrenBinding='{Binding Children}' />
            <f:BindingHost ItemsSource='{Binding Children}' DisplayMemberBinding='{Binding Friend.Name}' />
            <f:BindingHost DisplayMemberBinding='{Binding Breed, DataType=f:Dog}' />
            """);
        var person = new Person { Name = "Mary", Friend = new Person { Name = "Percy" }, Children = [new Person()] };

        Assert.Equal("Mary", BindingOperations.Evaluate(At(root, 0).DisplayMemberBinding!, person, typeof(string)));
        Assert.Same(person.Children, BindingOperations.Evaluate(At(root, 0).ChildrenBinding!, person, typeof(object)));
        Assert.Equal("Percy", BindingOperations.Evaluate(At(root, 1).DisplayMemberBinding!, person, typeof(string)));
        Assert.Equal("Collie", BindingOperations.Evaluate(At(root, 2).DisplayMemberBinding!, new Dog { Breed = "Collie" }, typeof(string)));
        Assert.Null(BindingOperations.GetBindingExpression(At(root, 0), nameof(BindingHost.DisplayMemberBinding)));
    }

    [Fact]
    public void NonPublicViewModelsWorkThroughTheLoader()
    {
        var root = Load("<f:BindingHost Text='{Binding Secret}' />", "x:DataType='fx:SecretModel'");
        var model = Activator.CreateInstance(typeof(Person).Assembly.GetType("Nexer.Xaml.Fixtures.Binding.SecretModel")!)!;
        model.GetType().GetProperty("Secret")!.SetValue(model, "hidden");

        root.DataContext = model;

        Assert.Equal("hidden", At(root, 0).Text);
    }

    [Theory]
    [InlineData("<f:BindingHost Text='{Binding Name}' />", "", NexerXamlDiagnosticCodes.MissingDataType)]
    [InlineData("<f:BindingHost DisplayMemberBinding='{Binding Name}' ItemsSource='{x:Null}' />", PersonType, NexerXamlDiagnosticCodes.MissingDataType)]
    [InlineData("<f:BindingHost Text='{Binding Nope}' />", PersonType, NexerXamlDiagnosticCodes.UnknownMember)]
    [InlineData("<f:BindingHost Text='{Binding Friend.Nope}' />", PersonType, NexerXamlDiagnosticCodes.UnknownMember)]
    [InlineData("<f:BindingHost Text='{Binding Hidden}' />", PersonType, NexerXamlDiagnosticCodes.InaccessibleMember)]
    [InlineData("<f:BindingHost Text='{Binding Shared}' />", PersonType, NexerXamlDiagnosticCodes.InaccessibleMember)]
    [InlineData("<f:BindingHost Text='{Binding ReadOnlyName, Mode=TwoWay}' />", PersonType, NexerXamlDiagnosticCodes.TwoWayToReadOnly)]
    [InlineData("<f:BindingHost Editable='{Binding ReadOnlyName}' />", PersonType, NexerXamlDiagnosticCodes.TwoWayToReadOnly)]
    [InlineData("<f:BindingHost Text='{Binding (f:Tag.Label), Mode=OneWayToSource, RelativeSource={RelativeSource Self}}' />", PersonType, NexerXamlDiagnosticCodes.TwoWayToReadOnly)]
    [InlineData("<f:BindingHost.Template><f:PlainTarget Text='{Binding Name}' /></f:BindingHost.Template>", PersonType, NexerXamlDiagnosticCodes.InvalidBindingTarget)]
    [InlineData("<f:BindingHost Computed='{Binding Name}' />", PersonType, NexerXamlDiagnosticCodes.InvalidBindingTarget)]
    [InlineData("<f:BindingHost Sink='{Binding Name, Mode=TwoWay}' />", PersonType, NexerXamlDiagnosticCodes.InvalidBindingTarget)]
    [InlineData("<f:BindingHost DisplayMemberBinding='{Binding Text, ElementName=X}' />", PersonType, NexerXamlDiagnosticCodes.InvalidBindingTarget)]
    [InlineData("<f:BindingHost Text='{Binding Text, ElementName=Missing}' />", PersonType, NexerXamlDiagnosticCodes.UnknownElementName)]
    [InlineData("<f:BindingHost Text='{Binding Name, RelativeSource={RelativeSource FindAncestor, AncestorType=f:Person}}' />", PersonType, NexerXamlDiagnosticCodes.InvalidRelativeSource)]
    [InlineData("<f:BindingHost Text='{Binding Name, RelativeSource={RelativeSource FindAncestor}}' />", PersonType, NexerXamlDiagnosticCodes.InvalidRelativeSource)]
    [InlineData("<f:BindingHost Text='{Binding Tags[0}' />", PersonType, NexerXamlDiagnosticCodes.InvalidBindingPath)]
    [InlineData("<f:BindingHost Text='{Binding Name..Length}' />", PersonType, NexerXamlDiagnosticCodes.InvalidBindingPath)]
    [InlineData("<f:BindingHost Text='{Binding ((f:Dog)Name).Breed}' />", PersonType, NexerXamlDiagnosticCodes.InvalidBindingPath)]
    [InlineData("<f:BindingHost Text='{Binding (f:Nope.Label)}' />", PersonType, NexerXamlDiagnosticCodes.InvalidBindingPath)]
    [InlineData("<f:BindingHost Text='{Binding Age[0]}' />", PersonType, NexerXamlDiagnosticCodes.UnknownMember)]
    public void ReportsBindingErrorsWithLocation(string body, string attributes, string code)
    {
        var error = Error(body, attributes);

        Assert.Equal((code, 2), (error.Code, error.Line));
        Assert.True(error.Column > 0);
    }

    [Theory]
    [InlineData("<f:BindingHost When='{Binding Name}' />", true)]
    [InlineData("<f:BindingHost Number='{Binding Friend}' />", true)]
    [InlineData("<f:BindingHost Editable='{Binding Age}' />", false)]
    [InlineData("<f:BindingHost When='{Binding Name, Converter={x:Null}}' />", false)]
    [InlineData("<f:BindingHost Number='{Binding Name}' />", false)]
    [InlineData("<f:BindingHost Text='{Binding Age}' />", false)]
    [InlineData("<f:BindingHost Value='{Binding Age}' />", false)]
    [InlineData("<f:BindingHost Number='{Binding Anything}' />", false)]
    public void WarnsAboutIncompatibleTypes(string body, bool warns)
    {
        var diagnostics = XamlLoader.Validate(Xaml(body, PersonType));

        Assert.DoesNotContain(diagnostics, d => d.Severity == NexerXamlSeverity.Error);
        var warning = diagnostics.SingleOrDefault(d => d.Code == NexerXamlDiagnosticCodes.BindingTypeMismatch);
        Assert.Equal(warns, warning is not null);
        if (warning is not null)
        {
            Assert.Equal((NexerXamlSeverity.Warning, 2), (warning.Severity, warning.Line));
            Assert.StartsWith("XAML(2,", warning.ToString(), StringComparison.Ordinal);
        }
    }
}
