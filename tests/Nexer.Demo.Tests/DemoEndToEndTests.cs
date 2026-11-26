using Nexer.Ui;
using Nexer.Ui.Controls;
using Nexer.Ui.Testing;

namespace Nexer.Demo.Tests;

/// <summary>The XAML-compiled demo application driven headlessly with scripted keyboard and mouse input.</summary>
public class DemoEndToEndTests
{
    private static (DemoApplication App, UiDriver Driver) Start(bool openMainWindow = true)
    {
        var app = new DemoApplication(openMainWindow);
        return (app, new UiDriver(app, 80, 25));
    }

    /// <summary>Finds <paramref name="text"/> on screen and returns its first cell.</summary>
    private static (int X, int Y) Find(UiDriver driver, string text)
    {
        for (int y = 0; y < driver.Frame.Rows; y++)
        {
            int x = driver.Row(y).IndexOf(text, StringComparison.Ordinal);
            if (x >= 0) return (x, y);
        }

        Assert.Fail($"'{text}' is not on screen:\n{driver.Text()}");
        return default;
    }

    [Fact]
    public void SmokeModeRendersEveryWindow()
    {
        using var log = new StringWriter();
        Assert.True(DemoSmoke.Run(log) == 0, log.ToString());
        Assert.Contains("smoke: OK", log.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MainScreenShowsMenuBarDesktopMainWindowAndStatusBar()
    {
        var (app, d) = Start();

        Assert.Equal("  File  Edit  Demo  Window  Help", d.Row(0).TrimEnd());
        Assert.Contains("Demo Window", d.Row(1), StringComparison.Ordinal);
        Assert.Equal("░", d.Cell(0, 1));
        Assert.Equal("░", d.Cell(79, 23));
        Assert.StartsWith(" F1 Help  F3 Open  F10 Exit │ Demo Main Window", d.Row(24), StringComparison.Ordinal);
        Assert.Contains("Message Boxes", d.Text(), StringComparison.Ordinal);
        Assert.Contains("MessageBoxes", d.Text(), StringComparison.Ordinal);
        Assert.IsType<DemoMainWindow>(app.ActiveWindow);

        // Closing the main window reveals the desktop and its status bar.
        d.Press("Ctrl+W");
        Assert.Empty(app.Windows);
        Assert.Equal(new string('░', 80), d.Row(12));
        Assert.StartsWith(" F1 Help  F3 Open  F10 Menu  Alt+X Exit │ Nexer demo", d.Row(24), StringComparison.Ordinal);
    }

    [Fact]
    public void TimersDriveTheMainWindowProgressBars()
    {
        var (app, d) = Start();
        var main = Assert.IsType<DemoMainWindow>(app.ActiveWindow);

        for (int i = 0; i < 10; i++) d.Advance(TimeSpan.FromMilliseconds(250));

        Assert.Equal(10, main.Timer1Value);
        Assert.Equal(10, main.Progress1.Value);
        Assert.Equal(20, main.Progress2.Value);
        Assert.Contains("Timer: 9", d.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptedSessionDrivesMenusWindowsControlsDialogsAndExit()
    {
        var (app, d) = Start();

        // Alt+mnemonic opens the Demo menu; its item mnemonic opens a new main window.
        d.Press("Alt+D");
        Assert.Equal("Demo", app.ActiveMenu?.Title?.Replace("&", "", StringComparison.Ordinal));
        Assert.Contains("New Main Window...", d.Text(), StringComparison.Ordinal);
        d.Press("e");
        Assert.Null(app.ActiveMenu);
        Assert.Equal(2, app.Windows.Count(w => w is DemoMainWindow));

        // F10 opens the first menu; Escape closes it.
        d.Press("F10");
        Assert.Same(app.Menus[0], app.ActiveMenu);
        Assert.Contains("Open...", d.Text(), StringComparison.Ordinal);
        d.Press("Escape");
        Assert.Null(app.ActiveMenu);

        // A button mnemonic opens the check box window; Tab moves between its controls.
        d.Press("Alt+C");
        var checkBoxes = Assert.IsType<DemoCheckBoxWindow>(app.ActiveWindow);
        Assert.Contains("Radiobuttons, CheckBoxes, and ComboBox", d.Text(), StringComparison.Ordinal);
        Assert.Same(checkBoxes.CheckBox1, app.FocusedWidget);
        d.Press("Tab");
        Assert.Same(checkBoxes.CheckBox2, app.FocusedWidget);
        Assert.True(checkBoxes.CheckBox2.Checked);
        d.Type(" ");
        Assert.False(checkBoxes.CheckBox2.Checked);
        Assert.Contains("[ ] CheckBox 2", d.Text(), StringComparison.Ordinal);
        d.Press("Tab");
        Assert.True(checkBoxes.Group.IsActive);
        Assert.Equal(2, checkBoxes.Group.Selected);
        d.Press("Shift+Tab");
        Assert.Same(checkBoxes.CheckBox2, app.FocusedWidget);

        // Ctrl+W closes it.
        d.Press("Ctrl+W");
        Assert.DoesNotContain(checkBoxes, app.Windows);
        Assert.IsType<DemoMainWindow>(app.ActiveWindow);

        // Open the message box window and click a button with the mouse: the OK message box is shown with ShowAsync.
        d.Press("Alt+M");
        var boxes = Assert.IsType<DemoMsgBoxWindow>(app.ActiveWindow);
        var (bx, by) = Find(d, "Open OK MB");
        d.Click(bx + 2, by);
        var ok = Assert.IsType<MessageBox>(app.ActiveWindow);
        Assert.True(ok.Modal);
        Assert.Contains("OK MessageBox", d.Text(), StringComparison.Ordinal);
        Assert.Contains("This is an example of a OK MessageBox.", d.Text(), StringComparison.Ordinal);

        // Keys go to the modal box only; Enter dismisses it and the awaiting handler reports the result.
        d.Press("Alt+D");
        Assert.Null(app.ActiveMenu);
        d.Press("Enter");
        Assert.DoesNotContain(ok, app.Windows);
        Assert.Same(boxes, app.ActiveWindow);
        Assert.Equal("Result: Ok", boxes.Result);
        Assert.Contains("Result: Ok", d.Text(), StringComparison.Ordinal);

        // A Yes/No box answered with its letter.
        d.Press("Alt+Y");
        Assert.IsType<MessageBox>(app.ActiveWindow);
        Assert.Contains("Yes/No MessageBox", d.Text(), StringComparison.Ordinal);
        d.Type("n");
        Assert.Equal("Result: No", boxes.Result);

        // Close the window with its Close button (mouse).
        var (cx, cy) = Find(d, "Close Window");
        d.Click(cx + 2, cy);
        Assert.DoesNotContain(boxes, app.Windows);

        // Exit asks for confirmation: No keeps running, Yes exits.
        d.Press("Alt+X");
        Assert.Contains("Exit application?", d.Text(), StringComparison.Ordinal);
        d.Type("n");
        Assert.True(app.IsRunning);
        Assert.DoesNotContain("Exit application?", d.Text(), StringComparison.Ordinal);
        d.Press("Alt+X");
        d.Type("y");
        Assert.False(app.IsRunning);
    }

    [Fact]
    public void InputBoxAnswerIsShownInAFollowUpMessageBox()
    {
        var (app, d) = Start(openMainWindow: false);
        var boxes = new DemoMsgBoxWindow();
        app.Windows.Add(boxes);
        d.Pump();

        d.Press("Alt+I");
        Assert.IsType<InputBox>(app.ActiveWindow);
        Assert.Contains("This is an example of an InputBox.", d.Text(), StringComparison.Ordinal);
        Assert.Contains("some input text", d.Text(), StringComparison.Ordinal);
        d.Type("!");
        d.Press("Enter");

        Assert.Equal("Input: some input text!", boxes.Result);
        Assert.IsType<MessageBox>(app.ActiveWindow);
        Assert.Contains("You entered: some input text!", d.Text(), StringComparison.Ordinal);
        d.Press("Enter");
        Assert.Same(boxes, app.ActiveWindow);
    }

    [Fact]
    public void WindowMenuTilesCascadesAndSwitchesWindows()
    {
        var (app, d) = Start(openMainWindow: false);
        var text = new DemoTextWindow();
        var split = new DemoSplitPaneWindow();
        app.Windows.Add(text);
        app.Windows.Add(split);
        d.Pump();
        Assert.Same(split, app.ActiveWindow);

        d.Press("Alt+W");
        d.Press("t");
        Assert.Null(app.ActiveMenu);
        // Jexer tiles two windows as one column of two rows covering the desktop.
        Assert.Equal((0, 80), (text.X, text.Width));
        Assert.Equal((0, 80), (split.X, split.Width));
        Assert.Equal((11, 11), (text.Height, split.Height)); // 23 desktop rows / 2, as in Jexer
        Assert.NotEqual(text.Y, split.Y);

        d.Press("Alt+W");
        d.Press("a");
        Assert.NotEqual(text.X, split.X);
        Assert.NotEqual(text.Y, split.Y);

        d.Press("F6");
        Assert.Same(text, app.ActiveWindow);
        d.Press("Shift+F6");
        Assert.Same(split, app.ActiveWindow);

        d.Press("Alt+W");
        d.Press("c");
        Assert.DoesNotContain(split, app.Windows);
        Assert.Same(text, app.ActiveWindow);
    }

    [Fact]
    public void DemoMenuTogglesTranslucenceAndStatusBar()
    {
        var (app, d) = Start();
        Assert.False(app.Translucence);

        d.Press("Alt+D");
        d.Press("t");
        Assert.True(app.Translucence);
        Assert.True(app.TranslucenceItem.Checked);

        d.Press("Alt+D");
        d.Press("s");
        Assert.True(app.HideStatusBar);
        Assert.DoesNotContain("Demo Main Window", d.Row(24), StringComparison.Ordinal);

        d.Press("Alt+D");
        d.Press("t");
        Assert.False(app.Translucence);
    }

    [Fact]
    public void F3OpensTheFileOpenBoxAsynchronously()
    {
        var (app, d) = Start();

        d.Press("F3");
        var box = Assert.IsType<FileOpenBox>(app.ActiveWindow);
        Assert.True(box.Modal);
        d.Press("Escape");
        Assert.DoesNotContain(box, app.Windows);
        Assert.Null(app.LastOpenedFile);
        Assert.IsType<DemoMainWindow>(app.ActiveWindow);
    }

    [Fact]
    public void ScrollableWindowScrollsItsCanvas()
    {
        var (app, d) = Start(openMainWindow: false);
        var window = new DemoScrollableWindow();
        app.Windows.Add(window);
        d.Pump();
        Assert.Contains(DemoScrollableWindow.CanvasLine(0)[..20], d.Text(), StringComparison.Ordinal);

        d.Press("Down");
        d.Press("Down");
        d.Press("Right");
        Assert.Equal(2, window.VerticalValue);
        Assert.Equal(1, window.HorizontalValue);
        Assert.Contains(DemoScrollableWindow.CanvasLine(2)[1..20], d.Text(), StringComparison.Ordinal);
        Assert.DoesNotContain(DemoScrollableWindow.CanvasLine(0)[1..20], d.Text(), StringComparison.Ordinal);

        d.Press("End");
        Assert.Equal(window.VerticalScroller!.Maximum, window.VerticalValue);
        Assert.Contains(DemoScrollableWindow.CanvasLine(DemoScrollableWindow.CanvasHeight - 1)[^10..], d.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public void SplitPaneWindowShowsTheSelectedPlanet()
    {
        var (app, d) = Start(openMainWindow: false);
        var window = new DemoSplitPaneWindow();
        app.Windows.Add(window);
        d.Pump();

        var (x, y) = Find(d, "Mars");
        d.Click(x, y);
        Assert.Equal("Mars", window.Planets.SelectedItem);
        Assert.Contains("Olympus", d.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public void DataBindingWindowEditsTheViewModelAndRunsCommands()
    {
        var (app, d) = Start(openMainWindow: false);

        d.Press("Alt+D");
        d.Press("d");
        var window = Assert.IsType<DemoBindingWindow>(app.ActiveWindow);
        var vm = window.ViewModel;
        Assert.Contains("Grace (36) is not subscribed.", d.Text(), StringComparison.Ordinal);
        Assert.Contains("(nobody)", d.Text(), StringComparison.Ordinal);
        Assert.False(window.RemoveButton.IsEffectivelyEnabled);

        // Two-way: typing in the Name field updates the view model and every binding that depends on it.
        Assert.Same(window.NameField, window.ActiveChild);
        d.Press("End");
        d.Type("!");
        Assert.Equal("Grace!", vm.Name);
        Assert.Contains("Grace! (36) is not subscribed.", d.Text(), StringComparison.Ordinal);
        Assert.Contains("Echo:      Grace!", d.Text(), StringComparison.Ordinal);

        // The Add command appends to the ObservableCollection, which the list shows.
        d.Press("Alt+A");
        Assert.Equal(4, vm.People.Count);
        Assert.Equal("Grace!", vm.People[3].Name);
        Find(d, "Grace!  ");

        // Selecting enables Remove (CanExecute) and updates the selection label; removing clears it.
        vm.Selected = vm.People[0];
        d.Pump();
        Assert.Contains("Ada, 36", d.Text(), StringComparison.Ordinal);
        Assert.True(window.RemoveButton.IsEffectivelyEnabled);
        d.Press("Alt+R");
        Assert.DoesNotContain(vm.People, p => p.Name == "Ada");
    }
}