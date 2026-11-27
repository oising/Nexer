using MyApp.Views;
using Nexer.Ui.Testing;

namespace MyApp.Tests;

public class MainWindowTests
{
    private static (App App, MainWindow Window, UiDriver Driver) Start()
    {
        var app = new App();
        var window = new MainWindow();
        app.Windows.Add(window);
        return (app, window, new UiDriver(app, 80, 25));
    }

    [Fact]
    public void ShowsMenuBarWindowAndStatusBar()
    {
        var (_, _, d) = Start();

        Assert.StartsWith("  File  Window", d.Row(0), StringComparison.Ordinal);
        Assert.Contains("Tasks", d.Text(), StringComparison.Ordinal);
        Assert.Contains("Type a task", d.Row(24), StringComparison.Ordinal);
    }

    [Fact]
    public void TypingAndEnterAddsATask()
    {
        var (_, window, d) = Start();

        d.Type("Write tests");
        Assert.Equal("Write tests", window.ViewModel.NewTask);   // two-way binding
        d.Press("Enter");

        var task = Assert.Single(window.ViewModel.Tasks);
        Assert.Equal("Write tests", task.Title);
        Assert.Equal("", window.NewTaskField.Text);
        Assert.Contains("1 task", d.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveIsDisabledUntilATaskIsSelected()
    {
        var (_, window, d) = Start();
        window.ViewModel.Tasks.Add(new TaskItem("One"));
        d.Pump();
        Assert.False(window.RemoveButton.IsEffectivelyEnabled);

        window.TaskList.SelectedIndex = 0;
        d.Pump();
        Assert.True(window.RemoveButton.IsEffectivelyEnabled);

        d.Press("Alt+R");
        Assert.Empty(window.ViewModel.Tasks);
    }

    [Fact]
    public void AboutCommandShowsAMessageBox()
    {
        var (app, _, d) = Start();

        d.Press("Alt+F");
        d.Press("A");
        Assert.IsType<Nexer.Ui.Controls.MessageBox>(app.ActiveWindow);
        Assert.Contains("Built with Nexer.", d.Text(), StringComparison.Ordinal);

        d.Press("Enter");
        Assert.IsType<MainWindow>(app.ActiveWindow);
    }
}
