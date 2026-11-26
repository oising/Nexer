using System.ComponentModel;
using Nexer.Ui.Controls;
using Nexer.Ui.Data;
using Nexer.Ui.Xaml.Tests.Views;
using Nexer.Xaml;
using Nexer.Xaml.Compiler;

namespace Nexer.Ui.Xaml.Tests;

/// <summary>A public view model for binding tests (the SRE loader only gets access checks waived for the root assemblies).</summary>
public sealed class BindingTestViewModel
{
    public string Name { get; set; } = "";

    public int Count { get; set; }

    public BindingTestChild Child { get; set; } = new();

    public string ReadOnlyName => Name;
}

/// <summary>A nested source object that raises <see cref="INotifyPropertyChanged"/>.</summary>
public sealed class BindingTestChild : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; set; } = "";

    public void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Compiled <c>{Binding}</c> through the SRE loader and the Cecil build step.</summary>
public class BindingCompilationTests
{
    private const string Ns = "xmlns='https://nexer.dev/ui' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' "
        + "xmlns:local='clr-namespace:Nexer.Ui.Xaml.Tests;assembly=Nexer.Ui.Xaml.Tests'";

    private static Window Load(string body, string rootAttributes = "x:DataType='local:BindingTestViewModel'") =>
        XamlLoader.Load<Window>($"<Window {Ns} {rootAttributes}>{body}</Window>");

    private static NexerXamlDiagnostic LoadError(string body, string rootAttributes = "x:DataType='local:BindingTestViewModel'")
    {
        var exception = Assert.Throws<NexerXamlCompilationException>(() => Load(body, rootAttributes));
        return exception.Diagnostics.First(d => d.Severity == NexerXamlSeverity.Error);
    }

    [Fact]
    public void LoaderBindsLabelTextToDataContext()
    {
        var window = Load("<Label x:Name='L' Text='{Binding Name}' /><Label x:Name='N' Text='{Binding Child.Title}' />");
        var label = (Label)window.Children[0];
        var nested = (Label)window.Children[1];

        window.DataContext = new BindingTestViewModel { Name = "Ada", Child = new BindingTestChild { Title = "Countess" } };

        Assert.Equal("Ada", label.Text);
        Assert.Equal("Countess", nested.Text);
        var expression = BindingOperations.GetBindingExpression(label, nameof(Label.Text));
        Assert.NotNull(expression);
        var path = Assert.IsType<CompiledBinding>(expression.Binding).Path!;
        Assert.Equal(BindingSourceKind.DataContext, path.Source);
        Assert.Equal("Child.Title", ((CompiledBinding)BindingOperations.GetBindingExpression(nested, nameof(Label.Text))!.Binding).Path!.ToString());
    }

    [Fact]
    public void LoaderEmitsPathNodesWithAccessorsAndMetadata()
    {
        var window = Load("<Label Text='{Binding Child.Title, Mode=OneTime, StringFormat=[{0}]}' />");
        var label = (Label)window.Children[0];
        var binding = (CompiledBinding)BindingOperations.GetBindingExpression(label, nameof(Label.Text))!.Binding;

        Assert.Equal(BindingMode.OneTime, binding.Mode);
        Assert.Equal("[{0}]", binding.StringFormat);
        var nodes = binding.Path!.Nodes;
        Assert.Equal(["Child", "Title"], nodes.Select(n => n.Name));
        Assert.Equal(typeof(BindingTestChild), nodes[0].ValueType);
        Assert.False(nodes[0].Notifies);
        Assert.True(nodes[1].Notifies);
        Assert.NotNull(nodes[1].Set);
        var child = new BindingTestChild();
        nodes[1].Set!(child, "set");
        Assert.Equal("set", nodes[1].Get(child));

        window.DataContext = new BindingTestViewModel { Child = new BindingTestChild { Title = "x" } };
        Assert.Equal("[x]", label.Text);
    }

    [Fact]
    public void LoaderConvertsValueTypesAndBindsToSelf()
    {
        var window = Load("<Label Text='{Binding Count}' /><Label X='7' Text='{Binding X, RelativeSource={RelativeSource Self}}' />");

        window.DataContext = new BindingTestViewModel { Count = 42 };

        Assert.Equal("42", ((Label)window.Children[0]).Text);
        Assert.Equal("7", ((Label)window.Children[1]).Text);
    }

    [Fact]
    public void XDataTypeIsInheritedAndCanBeOverridden()
    {
        var window = Load(
            "<Panel><Label Text='{Binding Name}' /></Panel>"
            + "<Panel x:DataType='local:BindingTestChild'><Label Text='{Binding Title}' /></Panel>");
        var vm = new BindingTestViewModel { Name = "outer", Child = new BindingTestChild { Title = "inner" } };

        window.DataContext = vm;
        ((Panel)window.Children[1]).DataContext = vm.Child;

        Assert.Equal("outer", ((Label)((Panel)window.Children[0]).Children[0]).Text);
        Assert.Equal("inner", ((Label)((Panel)window.Children[1]).Children[0]).Text);
    }

    [Fact]
    public void OrdinaryValuesStillUseTheClrSetter()
    {
        var window = Load("<Label Text='plain' />");
        var label = (Label)window.Children[0];

        Assert.Equal("plain", label.Text);
        Assert.Null(BindingOperations.GetBindingExpression(label, nameof(Label.Text)));
    }

    [Fact]
    public void BindingWithoutDataTypeIsAnError() =>
        Assert.Equal(NexerXamlDiagnosticCodes.MissingDataType, LoadError("<Label Text='{Binding Name}' />", "").Code);

    [Fact]
    public void UnknownMemberIsAnError() =>
        Assert.Equal(NexerXamlDiagnosticCodes.UnknownMember, LoadError("<Label Text='{Binding Nope}' />").Code);

    [Fact]
    public void TwoWayToReadOnlyIsAnError() =>
        Assert.Equal(NexerXamlDiagnosticCodes.TwoWayToReadOnly, LoadError("<Label Text='{Binding ReadOnlyName, Mode=TwoWay}' />").Code);

    [Fact]
    public void UnknownElementNameIsAnError() =>
        Assert.Equal(NexerXamlDiagnosticCodes.UnknownElementName, LoadError("<Label Text='{Binding Text, ElementName=Missing}' />").Code);

    [Fact]
    public void CompiledWindowBindsThroughCecil()
    {
        var window = new BindingWindow();
        var vm = new BindingWindowViewModel { Greeting = "Hello", Child = new BindingTestChild { Title = "nested" }, Items = ["a", "b"] };

        window.DataContext = vm;

        Assert.Equal("Hello", window.GreetingLabel.Text);
        Assert.Equal("nested", window.NestedLabel.Text);
        Assert.Equal("3", window.SelfLabel.Text);
        var binding = (CompiledBinding)BindingOperations.GetBindingExpression(window.GreetingLabel, nameof(Label.Text))!.Binding;
        Assert.Equal("Greeting", binding.Path!.ToString());
    }

    [Fact]
    public void CompiledWindowSupportsEveryPathAndSourceThroughCecil()
    {
        var window = new BindingWindow();

        window.DataContext = new BindingWindowViewModel { Greeting = "Hello", Items = ["zero", "one"] };

        Assert.Equal("one", window.IndexLabel.Text);
        Assert.Equal("HELLO", window.ConverterLabel.Text);
        Assert.Equal("Right", window.AttachedLabel.Text);
        Assert.Equal(["Items", "Item[]"], PathOf(window.IndexLabel).Nodes.Select(n => n.Name));
        Assert.IsType<UpperCaseConverter>(BindingOf(window.ConverterLabel).Converter);
        Assert.Equal("(AnchorLayout.Anchor)", Assert.Single(PathOf(window.AttachedLabel).Nodes).Name);
        var element = PathOf(window.ElementLabel);
        Assert.Equal((BindingSourceKind.ElementName, "GreetingLabel"), (element.Source, element.ElementName));
        var ancestor = PathOf(window.AncestorLabel);
        Assert.Equal((BindingSourceKind.FindAncestor, typeof(Window), 1), (ancestor.Source, ancestor.AncestorType, ancestor.AncestorLevel));
        var secret = Assert.Single(PathOf(window.InternalLabel).Nodes);
        Assert.Equal("hidden", secret.Get(new InternalViewModel { Secret = "hidden" }));
    }

    private static CompiledBinding BindingOf(Label label) =>
        (CompiledBinding)BindingOperations.GetBindingExpression(label, nameof(Label.Text))!.Binding;

    private static CompiledBindingPath PathOf(Label label) => BindingOf(label).Path!;
}
