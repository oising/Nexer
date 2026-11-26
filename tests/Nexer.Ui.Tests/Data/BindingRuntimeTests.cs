using System.Globalization;
using System.Runtime.CompilerServices;
using Nexer.Ui.Data;
using Nexer.Ui.Markup;
using Nexer.Ui.Testing;
using static Nexer.Ui.Tests.Data.Paths;

namespace Nexer.Ui.Tests.Data;

public sealed class BindingRuntimeTests : IDisposable
{
    private readonly CultureInfo culture = CultureInfo.CurrentCulture;

    public BindingRuntimeTests()
    {
        // A fresh application without a loop: notifications apply inline.
        _ = new Application();
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    public void Dispose() => CultureInfo.CurrentCulture = culture;

    private static BindTarget Attached(object? dataContext)
    {
        var target = new BindTarget { DataContext = dataContext };
        target.Attach();
        return target;
    }

    [Fact]
    public void OneWayFollowsSourceChanges()
    {
        var vm = new Person { Name = "Ann" };
        var target = Attached(vm);
        target.BindText(Binding(BindingMode.Default, Name));

        Assert.Equal("Ann", target.Text);
        vm.Name = "Bob";
        Assert.Equal("Bob", target.Text);
        target.Text = "typed";
        Assert.Equal("Bob", vm.Name);
    }

    [Fact]
    public void IntermediateChangeResubscribesDownstream()
    {
        var oldAddress = new Address { City = "Cork" };
        var vm = new Person { Address = oldAddress };
        var target = Attached(vm);
        target.BindText(Binding(BindingMode.OneWay, AddressNode, City));
        Assert.Equal("Cork", target.Text);

        oldAddress.City = "Galway";
        Assert.Equal("Galway", target.Text);

        var newAddress = new Address { City = "Sligo" };
        vm.Address = newAddress;
        Assert.Equal("Sligo", target.Text);
        Assert.Equal(0, oldAddress.HandlerCount);

        oldAddress.City = "ignored";
        Assert.Equal("Sligo", target.Text);
        newAddress.City = "Derry";
        Assert.Equal("Derry", target.Text);

        vm.Raise(null);
        Assert.Equal("Derry", target.Text);
        Assert.Equal(1, newAddress.HandlerCount);
    }

    [Fact]
    public void NullIntermediateUsesFallbackOrDefault()
    {
        var vm = new Person { Address = new Address { City = "Cork" } };
        var target = Attached(vm);
        var binding = Binding(BindingMode.OneWay, AddressNode, City);
        binding.FallbackValue = "?";
        target.BindText(binding);

        vm.Address = null;
        Assert.Equal("?", target.Text);

        var other = new BindTarget { DataContext = vm, Text = "x" };
        other.Attach();
        other.BindText(Binding(BindingMode.OneWay, AddressNode, City));
        Assert.Null(other.Text);

        var number = new BindTarget { DataContext = "not a person", Number = 3 };
        number.Attach();
        var ageBinding = Binding(BindingMode.OneWay, Age);
        ageBinding.FallbackValue = "5";
        number.BindNumber(ageBinding);
        Assert.Equal(5, number.Number);
    }

    [Fact]
    public void OneTimeEvaluatesOncePerSourceWithoutSubscribing()
    {
        var vm = new Person { Name = "Ann" };
        var target = Attached(vm);
        target.BindText(Binding(BindingMode.OneTime, Name));

        Assert.Equal("Ann", target.Text);
        Assert.Equal(0, vm.HandlerCount);
        vm.Name = "Bob";
        Assert.Equal("Ann", target.Text);

        target.DataContext = new Person { Name = "Cy" };
        Assert.Equal("Cy", target.Text);
    }

    [Fact]
    public void TwoWayWritesBackWithoutPingPong()
    {
        var vm = new Person { Noisy = "a" };
        var target = Attached(vm);
        target.BindText(Binding(BindingMode.TwoWay, Noisy));
        Assert.Equal("a", target.Text);
        var sets = vm.NoisySets;

        target.Text = "b";
        Assert.Equal("b", vm.Noisy);
        Assert.Equal(sets + 1, vm.NoisySets);

        var targetSets = target.TextSets;
        vm.Noisy = "c";
        Assert.Equal("c", target.Text);
        Assert.Equal(sets + 2, vm.NoisySets);
        Assert.Equal(targetSets + 1, target.TextSets);
    }

    [Fact]
    public void OneWayToSourcePushesTargetOnly()
    {
        var vm = new Person { Name = "Ann" };
        var target = new BindTarget { DataContext = vm, Text = "init" };
        target.BindText(Binding(BindingMode.OneWayToSource, Name));
        target.Attach();
        Assert.Equal("init", vm.Name);

        vm.Name = "Bob";
        Assert.Equal("init", target.Text);
        target.Text = "q";
        Assert.Equal("q", vm.Name);
    }

    [Fact]
    public void ExplicitAndLostFocusTriggers()
    {
        var vm = new Person { Name = "Ann" };
        var target = Attached(vm);
        var binding = Binding(BindingMode.TwoWay, Name);
        binding.UpdateSourceTrigger = UpdateSourceTrigger.Explicit;
        var expression = target.BindText(binding);
        target.Text = "Bob";
        Assert.Equal("Ann", vm.Name);
        expression.UpdateSource();
        Assert.Equal("Bob", vm.Name);

        // Widgets without focus-loss support treat LostFocus like PropertyChanged.
        var lostFocus = Binding(BindingMode.TwoWay, Name);
        lostFocus.UpdateSourceTrigger = UpdateSourceTrigger.LostFocus;
        var plain = Attached(vm);
        plain.BindText(lostFocus);
        plain.Text = "Di";
        Assert.Equal("Di", vm.Name);
    }

    [Fact]
    public void ConverterStringFormatAndTargetNullValue()
    {
        var vm = new Person { Name = "bob", Age = 30 };
        var target = Attached(vm);
        var binding = Binding(BindingMode.TwoWay, Name);
        binding.Converter = new UpperConverter();
        binding.ConverterParameter = "!";
        binding.FallbackValue = "fb";
        target.BindText(binding);
        Assert.Equal("BOB!", target.Text);

        target.Text = "HI";
        Assert.Equal("hi", vm.Name);
        vm.Name = "unset";
        Assert.Equal("fb", target.Text);
        vm.Name = "skip";
        Assert.Equal("fb", target.Text);

        var formatted = Attached(vm);
        formatted.BindText(new CompiledBinding(new CompiledBindingPath(Age)) { StringFormat = "N1" });
        Assert.Equal("30.0", formatted.Text);
        formatted.BindText(new CompiledBinding(new CompiledBindingPath(Age)) { StringFormat = "Age: {0}" });
        Assert.Equal("Age: 30", formatted.Text);

        var nulls = Attached(new Person());
        nulls.BindText(new CompiledBinding(new CompiledBindingPath(Name)) { TargetNullValue = "(none)" });
        Assert.Equal("(none)", nulls.Text);
    }

    [Fact]
    public void ConversionFailuresBecomeValidationErrors()
    {
        var vm = new Person { Age = 30 };
        var target = Attached(vm);
        var raised = 0;
        Validation.AddErrorsChangedHandler(target, (_, _) => raised++);
        var expression = target.BindText(Binding(BindingMode.TwoWay, Age));
        Assert.Equal("30", target.Text);

        target.Text = "42";
        Assert.Equal(42, vm.Age);
        Assert.False(Validation.GetHasErrors(target));

        target.Text = "abc";
        Assert.Equal(42, vm.Age);
        var error = Assert.Single(Validation.GetErrors(target));
        Assert.IsType<FormatException>(error.Exception);
        Assert.Same(expression, error.BindingExpression);
        Assert.True(expression.HasError);

        target.Text = "-1";
        Assert.IsType<ArgumentOutOfRangeException>(Assert.Single(Validation.GetErrors(target)).Exception);
        Assert.Equal(42, vm.Age);

        target.Text = "7";
        Assert.Equal(7, vm.Age);
        Assert.Empty(Validation.GetErrors(target));
        Assert.Equal(3, raised);

        var nullable = Attached(vm);
        nullable.BindText(Binding(BindingMode.TwoWay, Score));
        nullable.Text = "5";
        Assert.Equal(5, vm.Score);
        nullable.Text = "";
        Assert.Null(vm.Score);
    }

    [Fact]
    public void ValidatesOnExceptionsFalseLeavesNoError()
    {
        var vm = new Person { Age = 1 };
        var target = Attached(vm);
        var binding = Binding(BindingMode.TwoWay, Age);
        binding.ValidatesOnExceptions = false;
        target.BindText(binding);
        target.Text = "abc";
        Assert.Equal(1, vm.Age);
        Assert.False(Validation.GetHasErrors(target));
    }

    [Fact]
    public void NotifyDataErrorInfoIsMirroredWhileAttached()
    {
        var vm = new Person { Name = "Ann" };
        vm.SetError(nameof(Person.Name), "required");
        var target = Attached(vm);
        target.BindText(Binding(BindingMode.TwoWay, Name));
        Assert.Equal("required", Assert.Single(Validation.GetErrors(target)).ErrorContent);

        vm.SetError(nameof(Person.Name), "a", "b");
        Assert.Equal(["a", "b"], Validation.GetErrors(target).Select(e => e.ErrorContent));
        vm.SetError(nameof(Person.Age), "other property");
        Assert.Equal(2, Validation.GetErrors(target).Count);
        vm.SetError(nameof(Person.Name));
        Assert.False(Validation.GetHasErrors(target));

        target.Detach();
        Assert.Equal(0, vm.ErrorHandlerCount);
    }

    [Fact]
    public void SelfElementNameAndFindAncestorSources()
    {
        var self = Attached(null);
        self.BindText(new CompiledBinding(new CompiledBindingPath(BindingSourceKind.Self, null, null, 0,
            [Node<BindTarget, int>("Number", w => w.Number)])));
        self.Number = 4;
        Assert.Equal("4", self.Text);

        var root = new TitledHost { Title = "outer" };
        var inner = new TitledHost { Title = "inner" };
        var source = new BindTarget { Text = "named" };
        var byName = new BindTarget();
        var byAncestor = new BindTarget();
        var byLevel2 = new BindTarget();
        root.Children.Add(inner);
        root.Children.Add(source);
        inner.Children.Add(byName);
        inner.Children.Add(byAncestor);
        inner.Children.Add(byLevel2);

        byName.BindText(new CompiledBinding(new CompiledBindingPath(BindingSourceKind.ElementName, "src", null, 0,
            [Node<BindTarget, string?>("Text", w => w.Text)])));
        var title = Node<TitledHost, string?>("Title", w => w.Title);
        byAncestor.BindText(new CompiledBinding(new CompiledBindingPath(BindingSourceKind.FindAncestor, null, typeof(TitledHost), 1, [title])));
        byLevel2.BindText(new CompiledBinding(new CompiledBindingPath(BindingSourceKind.FindAncestor, null, typeof(TitledHost), 2, [title])));

        // The name is registered later (as compiled XAML does at the end of the document); attaching resolves it.
        NameScope.GetOrCreateNameScope(root).Register("src", source);
        Assert.Null(byName.Text);
        BindTarget.SetAttached(root, true);
        Assert.Equal("named", byName.Text);
        Assert.Equal("inner", byAncestor.Text);
        Assert.Equal("outer", byLevel2.Text);

        source.Text = "renamed";
        inner.Title = "inner2";
        Assert.Equal("renamed", byName.Text);
        Assert.Equal("inner2", byAncestor.Text);
    }

    [Fact]
    public void InheritedDataContextChangesReevaluate()
    {
        var parent = new Widget { DataContext = new Person { Name = "Ann" } };
        var target = new BindTarget();
        parent.Children.Add(target);
        target.BindText(Binding(BindingMode.OneWay, Name));
        Assert.Equal("Ann", target.Text);

        parent.DataContext = new Person { Name = "Bob" };
        Assert.Equal("Bob", target.Text);
    }

    [Fact]
    public void DetachUnsubscribesAndReattachReevaluates()
    {
        var address = new Address { City = "Cork" };
        var vm = new Person { Address = address };
        var target = Attached(vm);
        target.BindText(Binding(BindingMode.TwoWay, AddressNode, City));
        Assert.Equal(1, vm.HandlerCount);
        Assert.Equal(1, address.HandlerCount);

        target.Detach();
        Assert.Equal(0, vm.HandlerCount);
        Assert.Equal(0, address.HandlerCount);
        address.City = "Galway";
        Assert.Equal("Cork", target.Text);
        target.Text = "local";
        Assert.Equal("Galway", address.City);

        target.Attach();
        Assert.Equal("Galway", target.Text);
        Assert.Equal(1, address.HandlerCount);

        BindingOperations.ClearBinding(target, nameof(BindTarget.Text));
        Assert.Equal(0, vm.HandlerCount);
        Assert.Equal(0, address.HandlerCount);
        Assert.Equal("Galway", target.Text);
        Assert.Null(BindingOperations.GetBindingExpression(target, nameof(BindTarget.Text)));
    }

    [Fact]
    public void LongLivedSourceDoesNotKeepDetachedWidgetAlive()
    {
        var vm = new Person { Name = "Ann" };
        var weak = BindAndDetach(vm);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(weak.IsAlive);
        Assert.Equal(0, vm.HandlerCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference BindAndDetach(Person vm)
    {
        var target = Attached(vm);
        target.BindText(Binding(BindingMode.TwoWay, Name));
        Assert.Equal(1, vm.HandlerCount);
        target.Detach();
        return new WeakReference(target);
    }

    [Fact]
    public void OffThreadNotificationsAreMarshalledAndCollapsed()
    {
        var app = new Application();
        var driver = new UiDriver(app);
        var vm = new Person { Name = "Ann" };
        var target = Attached(vm);
        target.BindText(Binding(BindingMode.OneWay, Name));
        var sets = target.TextSets;

        var thread = new Thread(() =>
        {
            vm.Name = "a";
            vm.Name = "b";
            vm.Name = "c";
        });
        thread.Start();
        thread.Join();
        Assert.Equal("Ann", target.Text);

        driver.Pump();
        Assert.Equal("c", target.Text);
        Assert.Equal(sets + 1, target.TextSets);
    }

    [Fact]
    public void EvaluateRunsThePipelineOnce()
    {
        var vm = new Person { Address = new Address { City = "Cork" } };
        var binding = new CompiledBinding(new CompiledBindingPath(AddressNode, City)) { StringFormat = "[{0}]" };
        Assert.Equal("[Cork]", BindingOperations.Evaluate(binding, vm, typeof(string)));
        Assert.Equal(0, vm.HandlerCount);
        Assert.Null(BindingOperations.Evaluate(binding, new Person(), typeof(string)));
    }

    private sealed class TitledHost : Widget
    {
        private string? title;

        public string? Title
        {
            get => title;
            set => SetProperty(ref title, value);
        }
    }
}
