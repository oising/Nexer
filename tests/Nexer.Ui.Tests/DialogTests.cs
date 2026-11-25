using Nexer.Ui.Controls;
using Nexer.Ui.Testing;

namespace Nexer.Ui.Tests;

public class DialogTests
{
    private static T Await<T>(UiDriver d, Task<T> task)
    {
        for (int i = 0; i < 500 && !task.IsCompleted; i++)
        {
            d.Pump();
            if (!task.IsCompleted) Thread.Sleep(10);
        }

        Assert.True(task.IsCompleted, "The dialog task did not complete.");
        return task.Result;
    }

    [Fact]
    public void MessageBoxIsCentredAndChosenByLetter()
    {
        var app = new Application { ConfirmExit = false };
        var d = new UiDriver(app, 40, 14);
        var task = MessageBox.ShowAsync(app, "Hi", "Hello world");
        d.Pump();

        // 15x7 centred on the 40x12 desktop below the menu bar: X = 12, Y = 3; client origin (13,4).
        var box = Assert.IsType<MessageBox>(app.ActiveWindow);
        Assert.Equal(new Rect(12, 3, 15, 7), box.Bounds);
        Assert.True(app.IsModalActive);
        Assert.Equal("╔", d.Cell(12, 3));
        Assert.Contains(" Hi ", d.Row(3, 12, 15), StringComparison.Ordinal);
        Assert.Equal("Hello world", d.Row(5, 14, 11));
        Assert.Contains("OK", d.Row(7, 13, 13), StringComparison.Ordinal);
        Assert.Equal(d.Theme("twindow.background.modal"), d.Style(14, 5));

        d.Type("o");
        Assert.Equal(MessageBoxResult.Ok, Await(d, task));
        Assert.Null(app.ActiveWindow);
    }

    [Fact]
    public void MessageBoxButtonsReturnTheirResults()
    {
        var app = new Application { ConfirmExit = false };
        var d = new UiDriver(app, 40, 14);

        var yesNo = MessageBox.ShowAsync(app, "Q", "Sure?", MessageBoxButtons.YesNo);
        d.Pump();
        var box = (MessageBox)app.ActiveWindow!;
        Assert.Equal(20, box.Width);
        Assert.Contains("Yes", d.Row(box.Y + 4, box.X, box.Width), StringComparison.Ordinal);
        d.Press("Alt+N");
        Assert.Equal(MessageBoxResult.No, Await(d, yesNo));

        var cancel = MessageBox.ShowAsync(app, "Q", "Sure?", MessageBoxButtons.YesNoCancel);
        d.Pump();
        box = (MessageBox)app.ActiveWindow!;
        Assert.Equal(31, box.Width);
        Assert.Equal(MessageBoxResult.Cancel, box.Result);
        d.Press("Ctrl+W");
        Assert.Equal(MessageBoxResult.Cancel, Await(d, cancel));

        var okCancel = MessageBox.ShowAsync(app, "Q", "Sure?", MessageBoxButtons.OkCancel);
        d.Pump();
        d.Key(Nexer.Input.Key.Enter);
        Assert.Equal(MessageBoxResult.Ok, Await(d, okCancel));
    }

    [Fact]
    public void InputBoxReturnsTextOrNull()
    {
        var app = new Application { ConfirmExit = false };
        var d = new UiDriver(app, 40, 14);
        var task = InputBox.ShowAsync(app, "Name", "Your name?", "Bob", MessageBoxButtons.OkCancel);
        d.Pump();
        var box = (InputBox)app.ActiveWindow!;
        Assert.Equal(9, box.Height);
        Assert.Equal("Bob", d.Row(box.Y + 4, box.X + 2, 3));
        d.Type("by");
        d.Key(Nexer.Input.Key.Enter);
        Assert.Equal("Bobby", Await(d, task));

        var cancelled = InputBox.ShowAsync(app, "Name", "Your name?", "x", MessageBoxButtons.OkCancel);
        d.Pump();
        d.Type("c");
        d.Press("Alt+C");
        Assert.Null(Await(d, cancelled));
    }

    [Fact]
    public void ExitAsksForConfirmation()
    {
        var app = new Application();
        var d = new UiDriver(app, 40, 14);
        app.ExecuteCommand(Command.Exit);
        d.Pump();
        var box = Assert.IsType<MessageBox>(app.ActiveWindow);
        Assert.Equal("Confirmation", box.Title);
        d.Type("n");
        d.Pump();
        Assert.True(app.IsRunning);

        app.ExecuteCommand(Command.Exit);
        d.Pump();
        d.Type("y");
        for (int i = 0; i < 100 && app.IsRunning; i++)
        {
            d.Pump();
            Thread.Sleep(10);
        }

        Assert.False(app.IsRunning);
    }

    [Fact]
    public void FileOpenBoxPicksAFile()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "dialog-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllText(Path.Combine(dir, "a.txt"), "a");
        File.WriteAllText(Path.Combine(dir, "b.txt"), "b");
        try
        {
            var app = new Application { ConfirmExit = false };
            var d = new UiDriver(app, 80, 25);
            var task = FileOpenBox.ShowAsync(app, dir);
            d.Pump();
            var box = Assert.IsType<FileOpenBox>(app.ActiveWindow);
            Assert.Equal("Open File...", box.Title);
            Assert.Contains("Open File...", d.Row(box.Y, box.X, box.Width), StringComparison.Ordinal);
            Assert.Equal("│", d.Cell(box.X + 33, box.Y + 4));
            Assert.Contains("a.txt", d.Row(box.Y + 4, box.X + 36, 26), StringComparison.Ordinal);
            Assert.Contains("sub", d.Text(box.X + 2, box.Y + 4, 30, 18), StringComparison.Ordinal);
            Assert.Equal(Path.Combine(dir, "a.txt"), box.StatusBar!.Text);

            d.Key(Nexer.Input.Key.Down);
            Assert.Equal(Path.Combine(dir, "b.txt"), box.StatusBar!.Text);
            d.Key(Nexer.Input.Key.Enter);
            Assert.Equal(Path.Combine(dir, "b.txt"), Await(d, task));

            var cancelled = FileOpenBox.ShowAsync(app, dir);
            d.Pump();
            d.Key(Nexer.Input.Key.Escape);
            Assert.Null(Await(d, cancelled));

            var save = FileOpenBox.ShowAsync(app, dir, FileOpenBoxType.Save);
            d.Pump();
            Assert.Equal("Save File...", app.ActiveWindow!.Title);
            d.Type("new.txt");
            d.Key(Nexer.Input.Key.Enter);
            Assert.Equal(Path.Combine(dir, "new.txt"), Await(d, save));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
