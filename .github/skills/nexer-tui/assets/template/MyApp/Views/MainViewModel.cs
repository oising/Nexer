using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace MyApp.Views;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class TaskItem(string title) : ObservableObject
{
    private string title = title;

    public string Title { get => title; set => Set(ref title, value); }
}

public sealed class MainViewModel : ObservableObject
{
    private string newTask = "";
    private TaskItem? selected;

    public MainViewModel()
    {
        AddCommand = new RelayCommand(_ => Add(), _ => !string.IsNullOrWhiteSpace(NewTask));
        RemoveCommand = new RelayCommand(p => Tasks.Remove((TaskItem)p!), p => p is TaskItem);
        Tasks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Summary));
    }

    public ObservableCollection<TaskItem> Tasks { get; } = [];

    public string NewTask
    {
        get => newTask;
        set
        {
            if (Set(ref newTask, value)) AddCommand.RaiseCanExecuteChanged();
        }
    }

    public TaskItem? Selected { get => selected; set => Set(ref selected, value); }

    public string Summary => Tasks.Count == 1 ? "1 task" : $"{Tasks.Count} tasks";

    public RelayCommand AddCommand { get; }

    public RelayCommand RemoveCommand { get; }

    private void Add()
    {
        Tasks.Add(new TaskItem(NewTask.Trim()));
        NewTask = "";
    }
}
