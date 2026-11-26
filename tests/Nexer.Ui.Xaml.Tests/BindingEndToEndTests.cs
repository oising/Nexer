using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Nexer.Input;
using Nexer.Ui.Controls;
using Nexer.Ui.Data;
using Nexer.Ui.Testing;
using Nexer.Ui.Xaml.Tests.Views;
using Nexer.Xaml;

namespace Nexer.Ui.Xaml.Tests;

/// <summary>Base class for the end-to-end view models.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

/// <summary>A list item.</summary>
public sealed class E2EPerson : ObservableObject
{
    private string name = "";

    public string Name
    {
        get => name;
        set => Set(ref name, value);
    }
}

/// <summary>An <see cref="ICommand"/> backed by delegates.</summary>
public sealed class TestRelayCommand(Action<object?> execute, Func<object?, bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute(parameter);

    public void Execute(object? parameter) => execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>The view model of the end-to-end windows (SRE loader and Cecil <c>MvvmWindow.xaml</c>).</summary>
public sealed class E2EViewModel : ObservableObject
{
    private string name = "";
    private int age;
    private bool subscribed;
    private E2EPerson? selected;
    private bool canSave;

    public E2EViewModel()
    {
        SaveCommand = new TestRelayCommand(p => Saved.Add(p), _ => CanSave);
    }

    public string Name
    {
        get => name;
        set => Set(ref name, value);
    }

    public int Age
    {
        get => age;
        set => Set(ref age, value);
    }

    public bool Subscribed
    {
        get => subscribed;
        set => Set(ref subscribed, value);
    }

    public ObservableCollection<E2EPerson> People { get; } = [];

    public E2EPerson? Selected
    {
        get => selected;
        set => Set(ref selected, value);
    }

    public bool CanSave
    {
        get => canSave;
        set
        {
            if (Set(ref canSave, value)) SaveCommand.RaiseCanExecuteChanged();
        }
    }

    public TestRelayCommand SaveCommand { get; }

    public List<object?> Saved { get; } = [];
}

/// <summary>
/// End-to-end compiled bindings on the real controls, driven through <see cref="UiDriver"/> (keyboard input, rendered
/// screen): SRE loader documents plus the Cecil-compiled <c>MvvmWindow.xaml</c>.
/// </summary>
public class BindingEndToEndTests
{
    private const string Ns = "xmlns='https://nexer.dev/ui' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' "
        + "xmlns:local='clr-namespace:Nexer.Ui.Xaml.Tests;assembly=Nexer.Ui.Xaml.Tests'";

    // The window sits at desktop (0,0) = screen row 1; its client origin is screen (1,2).
    private const string Form = """
        <Field x:Name='NameField' Width='20' Text='{Binding Name}' />
        <Field x:Name='AgeField' Y='1' Width='10' Text='{Binding Age}' />
        <CheckBox x:Name='SubscribedBox' Y='2' Text='&amp;Subscribed' Checked='{Binding Subscribed}' />
        <List x:Name='PeopleList' Y='3' Width='20' Height='4' ItemsSource='{Binding People}'
              DisplayMemberBinding='{Binding Name}' SelectedItem='{Binding Selected}' />
        <Button x:Name='SaveButton' Y='8' Text='S&amp;ave' Command='{Binding SaveCommand}' CommandParameter='{Binding Name}' />
        <Label x:Name='EchoLabel' Y='10' Width='30' Text='{Binding Text, ElementName=NameField}' />
        <Panel Y='11' Width='30' Height='1'>
          <Label x:Name='AncestorLabel' Width='30' Text='{Binding Title, RelativeSource={RelativeSource AncestorType=Window}}' />
        </Panel>
        <Label x:Name='SelectedLabel' Y='12' Width='30' Text='{Binding Selected.Name, FallbackValue=(none)}' />
        """;

    private static Window LoadForm() =>
        XamlLoader.Load<Window>($"<Window {Ns} x:DataType='local:E2EViewModel' Title='MVVM' Bounds='0,0,60,20'>{Form}</Window>");

    private static T Find<T>(Window window, string name) where T : Widget =>
        Assert.IsType<T>(Markup.NameScope.FindName(window, name));

    private static (Window Window, E2EViewModel Vm, UiDriver Driver) Start()
    {
        var vm = new E2EViewModel { Name = "Ada", Age = 36 };
        vm.People.Add(new E2EPerson { Name = "Ann" });
        vm.People.Add(new E2EPerson { Name = "Bob" });
        var window = LoadForm();
        window.DataContext = vm;
        var app = new Application { ConfirmExit = false };
        app.Windows.Add(window);
        return (window, vm, new UiDriver(app, 80, 25));
    }

    [Fact]
    public void FieldTextIsTwoWayAndShowsConversionErrors()
    {
        var (window, vm, d) = Start();
        var name = Find<Field>(window, "NameField");
        var age = Find<Field>(window, "AgeField");
        Assert.Equal("Ada", d.Row(2, 1, 3));
        Assert.Equal("36", d.Row(3, 1, 2));

        window.Activate(name);
        d.Key(Key.End);
        d.Type("m");
        Assert.Equal("Adam", vm.Name);

        vm.Name = "Grace";
        d.Pump();
        Assert.Equal("Grace", d.Row(2, 1, 5));

        window.Activate(age);
        d.Key(Key.End);
        d.Type("x");
        Assert.Equal(36, vm.Age);
        Assert.True(Validation.GetHasErrors(age));
        Assert.Equal(d.Theme("tfield.error"), d.Style(1, 3));
        var message = Assert.Single(Validation.GetErrors(age)).ErrorContent!.ToString()!;
        Assert.Contains(message[..Math.Min(message.Length, 40)], d.Row(24));

        d.Key(Key.Backspace);
        d.Type("7");
        Assert.Equal(367, vm.Age);
        Assert.False(Validation.GetHasErrors(age));
        Assert.Equal(d.Theme("tfield.active"), d.Style(1, 3));
        Assert.DoesNotContain(message[..Math.Min(message.Length, 40)], d.Row(24));
    }

    [Fact]
    public void CheckBoxAndListFollowTheViewModel()
    {
        var (window, vm, d) = Start();
        var list = Find<List>(window, "PeopleList");

        d.Press("Alt+S");
        Assert.True(vm.Subscribed);
        vm.Subscribed = false;
        d.Pump();
        Assert.False(Find<CheckBox>(window, "SubscribedBox").Checked);

        Assert.Equal("Ann", d.Row(5, 1, 3));
        Assert.Equal("Bob", d.Row(6, 1, 3));
        vm.People.Add(new E2EPerson { Name = "Cy" });
        vm.People.RemoveAt(0);
        d.Pump();
        Assert.Equal("Bob", d.Row(5, 1, 3));
        Assert.Equal("Cy ", d.Row(6, 1, 3));

        window.Activate(list);
        d.Key(Key.Down);
        Assert.Same(vm.People[0], vm.Selected);
        Assert.Equal("Bob", d.Row(14, 1, 3));
        vm.Selected = vm.People[1];
        d.Pump();
        Assert.Equal(1, list.SelectedIndex);
        Assert.Equal("Cy", d.Row(14, 1, 2));

        vm.People[1].Name = "Cyril";
        d.Pump();
        Assert.Equal("Cyril", d.Row(6, 1, 5));
        Assert.Equal("Cyril", d.Row(14, 1, 5));

        vm.Selected = null;
        d.Pump();
        Assert.Equal(-1, list.SelectedIndex);
        Assert.Equal("(none)", d.Row(14, 1, 6));
    }

    [Fact]
    public void ButtonCommandFollowsCanExecuteAndPassesTheParameter()
    {
        var (window, vm, d) = Start();
        var save = Find<Button>(window, "SaveButton");

        Assert.False(save.IsEffectivelyEnabled);
        Assert.Equal(d.Theme("tbutton.disabled"), d.Style(2, 10));
        d.Press("Alt+A");
        Assert.Empty(vm.Saved);

        vm.CanSave = true;
        d.Pump();
        Assert.True(save.IsEffectivelyEnabled);
        Assert.NotEqual(d.Theme("tbutton.disabled"), d.Style(2, 10));
        d.Press("Alt+A");
        Assert.Equal(["Ada"], vm.Saved);

        window.Activate(save);
        vm.Name = "Bea";
        d.Key(Key.Enter);
        Assert.Equal(["Ada", "Bea"], vm.Saved);

        vm.CanSave = false;
        d.Key(Key.Enter);
        Assert.Equal(["Ada", "Bea"], vm.Saved);
    }

    [Fact]
    public void ElementNameAndAncestorBindingsUpdateFromInput()
    {
        var (window, _, d) = Start();

        Assert.Equal("Ada", d.Row(12, 1, 3));
        Assert.Equal("MVVM", d.Row(13, 1, 4));

        window.Activate(Find<Field>(window, "NameField"));
        d.Key(Key.End);
        d.Type("!");
        Assert.Equal("Ada!", d.Row(12, 1, 4));

        window.Title = "Renamed";
        d.Pump();
        Assert.Equal("Renamed", Find<Label>(window, "AncestorLabel").Text);
    }

    [Fact]
    public void DataContextIsInheritedFromWindowAndApplication()
    {
        var window = LoadForm();
        var app = new Application { ConfirmExit = false, DataContext = new E2EViewModel { Name = "FromApp" } };
        app.Windows.Add(window);
        var d = new UiDriver(app, 80, 25);
        Assert.Equal("FromApp", d.Row(2, 1, 7));

        window.DataContext = new E2EViewModel { Name = "FromWindow" };
        d.Pump();
        Assert.Equal("FromWindow", d.Row(2, 1, 10));

        window.DataContext = null;
        d.Pump();
        Assert.Equal("FromApp", Find<Field>(window, "NameField").Text);
    }

    [Fact]
    public void ChangesFromOtherThreadsAreMarshalledToTheUiThread()
    {
        var (window, vm, d) = Start();
        var name = Find<Field>(window, "NameField");

        // Join (rather than await) so the test thread, which is the UI thread, stays the same.
        var worker = new Thread(() => vm.Name = "Background");
        worker.Start();
        worker.Join();
        Assert.Equal("Ada", name.Text);

        d.Pump();
        Assert.Equal("Background", name.Text);
        Assert.Equal("Background", d.Row(2, 1, 10));
    }

    [Fact]
    public void CecilCompiledMvvmWindowRunsEndToEnd()
    {
        var vm = new E2EViewModel { Name = "Ada", Age = 36, CanSave = true };
        vm.People.Add(new E2EPerson { Name = "Ann" });
        var window = new MvvmWindow { DataContext = vm };
        var app = new Application { ConfirmExit = false };
        app.Windows.Add(window);
        var d = new UiDriver(app, 80, 25);

        Assert.Equal("Ada", d.Row(2, 1, 3));
        Assert.Equal("Ann", d.Row(5, 1, 3));
        window.Activate(window.NameField);
        d.Key(Key.End);
        d.Type("m");
        Assert.Equal("Adam", vm.Name);
        Assert.Equal("Adam", d.Row(12, 1, 4));

        window.Activate(window.AgeField);
        d.Type("x");
        Assert.True(Validation.GetHasErrors(window.AgeField));

        vm.People.Add(new E2EPerson { Name = "Bob" });
        d.Pump();
        Assert.Equal("Bob", d.Row(6, 1, 3));

        d.Press("Alt+A");
        Assert.Equal(["Adam"], vm.Saved);
    }
}
