using Nexer.Ui;

namespace MyApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
        : this(new MainViewModel())
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public MainViewModel ViewModel => (MainViewModel)DataContext!;

    // Enter in the field adds the task (the binding has already pushed the text to the view model).
    private void OnSubmitted(object? sender, EventArgs e)
    {
        if (ViewModel.AddCommand.CanExecute(null)) ViewModel.AddCommand.Execute(null);
    }
}
