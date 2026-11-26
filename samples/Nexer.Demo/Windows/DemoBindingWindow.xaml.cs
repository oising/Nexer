using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Nexer.Ui;

namespace Nexer.Demo;

/// <summary>
/// The data binding (MVVM) demo: a form, a list and commands bound to a <see cref="BindingDemoViewModel"/> with
/// compiled <c>{Binding}</c>s (see docs\binding.md).
/// </summary>
public partial class DemoBindingWindow : Window
{
    /// <summary>Creates the window with a fresh view model as its <see cref="Widget.DataContext"/>.</summary>
    public DemoBindingWindow()
        : this(new BindingDemoViewModel())
    {
    }

    /// <summary>Creates the window bound to <paramref name="viewModel"/>.</summary>
    public DemoBindingWindow(BindingDemoViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>The view model.</summary>
    public BindingDemoViewModel ViewModel => (BindingDemoViewModel)DataContext!;
}

/// <summary>Base class raising <see cref="INotifyPropertyChanged.PropertyChanged"/>.</summary>
public abstract class BindableBase : INotifyPropertyChanged
{
    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Sets <paramref name="field"/> and raises the change notification when the value changed.</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    /// <summary>Raises <see cref="PropertyChanged"/>.</summary>
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A minimal <see cref="ICommand"/> over delegates.</summary>
public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    /// <inheritdoc/>
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc/>
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    /// <inheritdoc/>
    public void Execute(object? parameter) => execute(parameter);

    /// <summary>Raises <see cref="CanExecuteChanged"/>.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>A person in the demo list.</summary>
public sealed class DemoPerson(string name, int age) : BindableBase
{
    private string name = name;

    /// <summary>The name (shown in the list).</summary>
    public string Name
    {
        get => name;
        set
        {
            if (Set(ref name, value)) OnPropertyChanged(nameof(Description));
        }
    }

    /// <summary>The age.</summary>
    public int Age { get; } = age;

    /// <summary>A one-line description.</summary>
    public string Description => string.Create(CultureInfo.InvariantCulture, $"{Name}, {Age}");
}

/// <summary>The view model behind <see cref="DemoBindingWindow"/>.</summary>
public sealed class BindingDemoViewModel : BindableBase
{
    private string name = "Grace";
    private int age = 36;
    private bool subscribed;
    private DemoPerson? selected;

    /// <summary>Creates the view model with a few people.</summary>
    public BindingDemoViewModel()
    {
        People = [new DemoPerson("Ada", 36), new DemoPerson("Alan", 41), new DemoPerson("Edsger", 72)];
        AddCommand = new RelayCommand(_ => People.Add(new DemoPerson(Name, Age)));
        RemoveCommand = new RelayCommand(
            p => People.Remove((DemoPerson)p!),
            p => p is DemoPerson);
    }

    /// <summary>The name being edited.</summary>
    public string Name
    {
        get => name;
        set
        {
            if (Set(ref name, value)) OnPropertyChanged(nameof(Summary));
        }
    }

    /// <summary>The age being edited (typing a non-number shows a validation error).</summary>
    public int Age
    {
        get => age;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Age cannot be negative.");
            if (Set(ref age, value)) OnPropertyChanged(nameof(Summary));
        }
    }

    /// <summary>Whether the person is subscribed.</summary>
    public bool Subscribed
    {
        get => subscribed;
        set
        {
            if (Set(ref subscribed, value)) OnPropertyChanged(nameof(Summary));
        }
    }

    /// <summary>A summary of the form, recomputed as it changes.</summary>
    public string Summary => string.Create(CultureInfo.InvariantCulture,
        $"{Name} ({Age}) is {(Subscribed ? "subscribed" : "not subscribed")}.");

    /// <summary>The people in the list.</summary>
    public ObservableCollection<DemoPerson> People { get; }

    /// <summary>The selected person, if any.</summary>
    public DemoPerson? Selected
    {
        get => selected;
        set => Set(ref selected, value);
    }

    /// <summary>Adds a person from the form.</summary>
    public RelayCommand AddCommand { get; }

    /// <summary>Removes the selected person (enabled only while one is selected).</summary>
    public RelayCommand RemoveCommand { get; }
}
