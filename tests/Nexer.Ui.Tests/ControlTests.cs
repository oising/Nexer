using Nexer.Core;
using Nexer.Input;
using Nexer.Ui.Controls;
using Nexer.Ui.Testing;

namespace Nexer.Ui.Tests;

public class ControlTests
{
    // A window at desktop (0,0) lands on screen row 1 (below the menu bar); its client origin is screen (1,2).
    private static (Application App, UiDriver Driver, Window Window) Start(int width, int height, params Widget[] children)
    {
        var window = new Window { Title = "T", X = 0, Y = 0, Width = width, Height = height };
        foreach (var c in children) window.Children.Add(c);
        var app = new Application { ConfirmExit = false };
        app.Windows.Add(window);
        return (app, new UiDriver(app, 40, 14), window);
    }

    private static CellStyle ShadowOn(CellStyle background) =>
        new(TerminalColor.Indexed(0), background.Background, CellStyleFlags.None);

    [Fact]
    public void ButtonDrawsLabelMnemonicAndShadow()
    {
        var (_, d, _) = Start(20, 6, new Button { Text = "&OK", X = 1, Y = 1 });
        Assert.Equal(" OK ▄", d.Row(3, 2, 5));
        Assert.Equal(" ▀▀▀▀", d.Row(4, 2, 5));
        Assert.Equal(d.Theme("tbutton.mnemonic.highlighted"), d.Style(3, 3));
        Assert.Equal(d.Theme("tbutton.active"), d.Style(4, 3));
        Assert.Equal(ShadowOn(d.Theme("twindow.background")), d.Style(6, 3));
        Assert.Equal(ShadowOn(d.Theme("twindow.background")), d.Style(4, 4));
    }

    [Fact]
    public void ButtonClickEnterAndMnemonicRaiseClick()
    {
        var button = new Button { Text = "&Go", X = 1, Y = 1 };
        var other = new CheckBox { Text = "x", X = 1, Y = 4 };
        int clicks = 0;
        button.Click += (_, _) => clicks++;
        var (_, d, _) = Start(20, 8, button, other);
        d.Mouse(MouseEventKind.Down, 4, 3);
        Assert.True(button.IsPressed);
        Assert.Equal("  Go ", d.Row(3, 2, 5));
        d.Mouse(MouseEventKind.Up, 4, 3);
        Assert.Equal(1, clicks);
        Assert.False(button.IsPressed);
        d.Key(Key.Enter);
        Assert.Equal(2, clicks);
        other.Focus();
        d.Press("Alt+G");
        Assert.Equal(3, clicks);
    }

    [Fact]
    public void ButtonExecutesCommand()
    {
        var button = new Button { Text = "Quit", Command = Command.Exit };
        var (app, d, _) = Start(20, 6, button);
        d.Key(Key.Enter);
        Assert.False(app.IsRunning);
    }

    [Fact]
    public void CheckBoxTogglesWithSpaceClickAndEscape()
    {
        var box = new CheckBox { Text = "&Bold", X = 1, Y = 1 };
        int changes = 0;
        box.Changed += (_, _) => changes++;
        var (_, d, _) = Start(20, 6, box);
        Assert.Equal("[ ] Bold", d.Row(3, 2, 8));
        Assert.Equal(d.Theme("tcheckbox.active").Foreground, d.Style(2, 3).Foreground);
        Assert.Equal(d.Theme("twindow.background").Background, d.Style(2, 3).Background);
        d.Type(" ");
        Assert.True(box.Checked);
        Assert.Equal("[√] Bold", d.Row(3, 2, 8));
        d.Click(3, 3);
        Assert.False(box.Checked);
        d.Key(Key.Enter);
        Assert.True(box.Checked);
        d.Key(Key.Escape);
        Assert.False(box.Checked);
        d.Press("Alt+B");
        Assert.True(box.Checked);
        Assert.Equal(5, changes);
    }

    [Fact]
    public void RadioGroupDrawsBoxAndSelects()
    {
        var group = new RadioGroup { Text = "Size", X = 1, Y = 1 };
        var small = new RadioButton { Text = "&Small" };
        var large = new RadioButton { Text = "&Large", IsSelected = true };
        group.Children.Add(small);
        group.Children.Add(large);
        int changes = 0;
        group.SelectionChanged += (_, _) => changes++;
        var (_, d, _) = Start(24, 8, group);
        Assert.Equal(12, group.Width);
        Assert.Equal(4, group.Height);
        Assert.Equal(2, group.Selected);
        Assert.Equal(
            """
            ╒ Size ════╕
            │( ) Small │
            │(•) Large │
            ╘══════════╛
            """.ReplaceLineEndings("\n"),
            d.Text(2, 3, 12, 4));
        Assert.Equal(d.Theme("tradiogroup.active").Foreground, d.Style(2, 3).Foreground);
        d.Type(" ");
        Assert.Equal(1, group.Selected);
        Assert.Same(small, group.SelectedButton);
        d.Key(Key.Escape);
        Assert.Equal(0, group.Selected);
        d.Press("Alt+L");
        Assert.True(large.IsSelected);
        d.Click(4, 4);
        Assert.True(small.IsSelected);
        Assert.Equal(4, changes);
    }

    [Fact]
    public void RadioGroupRequiringSelectionKeepsOne()
    {
        var group = new RadioGroup { Text = "G", RequiresSelection = true };
        group.Children.Add(new RadioButton { Text = "A" });
        group.Children.Add(new RadioButton { Text = "B" });
        var (_, d, _) = Start(20, 8, group);
        Assert.Equal(1, group.Selected);
        d.Key(Key.Escape);
        Assert.Equal(1, group.Selected);
    }

    [Fact]
    public void FieldTypingEditingAndSubmit()
    {
        var field = new Field { X = 1, Y = 1, Width = 10 };
        int changes = 0, submits = 0;
        field.Changed += (_, _) => changes++;
        field.Submitted += (_, _) => submits++;
        var (_, d, _) = Start(20, 6, field);
        Assert.Equal("░░░░░░░░░░", d.Row(3, 2, 10));
        d.Type("hello");
        Assert.Equal("hello", field.Text);
        Assert.Equal("hello░░░░░", d.Row(3, 2, 10));
        Assert.Equal(d.Theme("tfield.active"), d.Style(2, 3));
        Assert.Equal((7, 3), (d.Host.Cursor.Column, d.Host.Cursor.Row));
        d.Key(Key.Backspace);
        d.Key(Key.Home);
        d.Type("x");
        Assert.Equal("xhell", field.Text);
        d.Key(Key.Insert);
        d.Type("Y");
        Assert.Equal("xYell", field.Text);
        d.Key(Key.Delete);
        Assert.Equal("xYll", field.Text);
        d.Key(Key.End);
        d.Paste("!\n?");
        Assert.Equal("xYll!?", field.Text);
        d.Key(Key.Enter);
        Assert.Equal(1, submits);
        Assert.Equal(10, changes);
    }

    [Fact]
    public void FieldScrollsWhenNotFixedAndStopsWhenFixed()
    {
        var scrolling = new Field { X = 1, Y = 1, Width = 5 };
        var fixedField = new Field { X = 1, Y = 3, Width = 5, Fixed = true };
        var (_, d, _) = Start(20, 8, scrolling, fixedField);
        d.Type("abcdefgh");
        Assert.Equal("abcdefgh", scrolling.Text);
        Assert.Equal("efgh░", d.Row(3, 2, 5));
        Assert.Equal(6, d.Host.Cursor.Column);
        d.Key(Key.Tab);
        Assert.True(fixedField.IsActive);
        d.Type("abcdefgh");
        Assert.Equal("abcde", fixedField.Text);
        Assert.Equal("abcde", d.Row(5, 2, 5));
    }

    [Fact]
    public void PasswordFieldShowsStarsWhenUnfocused()
    {
        var password = new PasswordField { X = 1, Y = 1, Width = 8, Text = "secret" };
        var other = new Field { X = 1, Y = 3, Width = 8 };
        var (_, d, _) = Start(20, 8, password, other);
        Assert.Equal("secret░░", d.Row(3, 2, 8));
        d.Key(Key.Tab);
        Assert.Equal("******░░", d.Row(3, 2, 8));
    }

    [Fact]
    public void LabelIsNotFocusableAndDispatchesMnemonic()
    {
        var label = new Label { Text = "&Name:", X = 1, Y = 1 };
        var field = new Field { X = 8, Y = 1, Width = 6 };
        int clicks = 0;
        label.Click += (_, _) => { clicks++; field.Focus(); };
        var (_, d, _) = Start(20, 6, label, field);
        Assert.Equal(5, label.Width);
        Assert.True(field.IsActive);
        Assert.False(label.IsActive);
        Assert.Equal("Name:", d.Row(3, 2, 5));
        Assert.Equal(d.Theme("tlabel.mnemonic"), d.Style(2, 3));
        Assert.Equal(d.Theme("tlabel"), d.Style(3, 3));
        d.Press("Alt+N");
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void TabCyclesThroughControls()
    {
        var field = new Field { X = 1, Y = 1, Width = 6 };
        var check = new CheckBox { Text = "C", X = 1, Y = 2 };
        var button = new Button { Text = "B", X = 1, Y = 3 };
        var (_, d, _) = Start(20, 8, field, check, button);
        Assert.True(field.IsActive);
        d.Key(Key.Tab);
        Assert.True(check.IsActive);
        d.Key(Key.Tab);
        Assert.True(button.IsActive);
        d.Key(Key.Tab);
        Assert.True(field.IsActive);
        d.Key(Key.Tab, KeyModifiers.Shift);
        Assert.True(button.IsActive);
    }

    [Fact]
    public void SpinnerRaisesUpAndDown()
    {
        var spinner = new Spinner { X = 1, Y = 1 };
        int value = 0;
        spinner.SpinUp += (_, _) => value++;
        spinner.SpinDown += (_, _) => value--;
        var (_, d, _) = Start(20, 6, spinner);
        Assert.Equal("↑↓", d.Row(3, 2, 2));
        d.Key(Key.Up);
        d.Key(Key.Up);
        d.Click(3, 3);
        Assert.Equal(1, value);
        d.Click(2, 3);
        Assert.Equal(2, value);
    }

    [Fact]
    public void ProgressBarDrawsJexerGlyphs()
    {
        var bar = new ProgressBar { X = 1, Y = 1, Width = 12, Value = 50 };
        var (_, d, _) = Start(20, 6, bar);
        Assert.Equal("├■■■■──────┤", d.Row(3, 2, 12));
        Assert.Equal(d.Theme("tprogressbar.complete"), d.Style(3, 3));
        bar.Value = 100;
        d.Pump();
        Assert.Equal("├■■■■■■■■■■┤", d.Row(3, 2, 12));
    }

    [Fact]
    public void TextWrapsAndScrolls()
    {
        var text = new Text { X = 1, Y = 1, Width = 12, Height = 4, Content = "The quick brown fox jumps over the lazy dog" };
        var (_, d, _) = Start(20, 8, text);
        Assert.Equal(["The quick", "brown fox", "jumps over", "the lazy", "dog", ""], text.Lines);
        Assert.Equal(
            """
            The quick  ▲
            brown fox  ■
            jumps over ▼
            """.ReplaceLineEndings("\n"),
            d.Text(2, 3, 12, 3));
        Assert.Equal("◀░░░░░░░░░▶", d.Row(6, 2, 11));
        d.Key(Key.Down);
        Assert.Equal(1, text.VerticalValue);
        Assert.Equal("brown fox  ", d.Row(3, 2, 11));
        d.Key(Key.End);
        Assert.Equal(3, text.VerticalValue);
        d.Mouse(MouseEventKind.WheelUp, 4, 4);
        Assert.Equal(2, text.VerticalValue);
    }

    [Fact]
    public void ScrollerArrowsAndPagesChangeValue()
    {
        var scroller = new VScroller { X = 1, Y = 0, Height = 5, Maximum = 10, BigChange = 4 };
        int changes = 0;
        scroller.ValueChanged += (_, _) => changes++;
        var (_, d, _) = Start(20, 8, scroller);
        Assert.False(scroller.IsActive);
        d.Click(2, 6);
        Assert.Equal(1, scroller.Value);
        d.Click(2, 5);
        Assert.Equal(5, scroller.Value);
        d.Click(2, 2);
        Assert.Equal(4, scroller.Value);
        Assert.Equal(3, changes);
    }
}
