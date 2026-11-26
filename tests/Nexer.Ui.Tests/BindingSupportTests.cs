using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;
using Nexer.Input;
using Nexer.Ui.Controls;
using Nexer.Ui.Data;
using Nexer.Ui.Menus;
using Nexer.Ui.Testing;

namespace Nexer.Ui.Tests;

public class BindingSupportTests
{
    // A window at desktop (0,0) lands on screen row 1 (below the menu bar); its client origin is screen (1,2).
    private static UiDriver Start(Window window, Application? app = null)
    {
        app ??= new Application { ConfirmExit = false };
        app.Windows.Add(window);
        return new UiDriver(app, 40, 14);
    }

    private static Window NewWindow(params Widget[] children)
    {
        var window = new Window { Title = "B", X = 0, Y = 0, Width = 30, Height = 12 };
        foreach (var child in children) window.Children.Add(child);
        return window;
    }

    private static List<string> Track(params Widget[] widgets)
    {
        var log = new List<string>();
        foreach (var w in widgets)
        {
            w.AttachedToTree += (_, _) => log.Add("+" + w.Name);
            w.DetachedFromTree += (_, _) => log.Add("-" + w.Name);
        }

        return log;
    }

    [Fact]
    public void WindowAddAndCloseAttachParentsFirstAndDetachChildrenFirst()
    {
        var button = new Button { Name = "button", Text = "B" };
        var panel = new Panel { Name = "panel" };
        panel.Children.Add(button);
        var window = NewWindow(panel);
        window.Name = "window";
        var log = Track(window, panel, button);
        var app = new Application { ConfirmExit = false };

        Assert.False(button.IsAttachedToTree);
        app.Windows.Add(window);
        Assert.Equal(["+window", "+panel", "+button"], log);
        Assert.True(button.IsAttachedToTree);

        log.Clear();
        app.Windows.Remove(window);
        Assert.Equal(["-button", "-panel", "-window"], log);
        Assert.False(window.IsAttachedToTree);
    }

    [Fact]
    public void ReparentingASubtreeDetachesAndReattachesOnce()
    {
        var label = new Label { Name = "label", Text = "L" };
        var panel = new Panel { Name = "panel" };
        panel.Children.Add(label);
        var first = NewWindow(panel);
        var second = NewWindow();
        var app = new Application { ConfirmExit = false };
        app.Windows.Add(first);
        app.Windows.Add(second);
        var log = Track(panel, label);

        first.Children.Remove(panel);
        Assert.Equal(["-label", "-panel"], log);
        second.Children.Add(panel);
        Assert.Equal(["-label", "-panel", "+panel", "+label"], log);

        // Adding to a detached parent does not attach; inserting a detached widget into an attached tree does.
        var loose = new Panel { Name = "loose" };
        var inner = new Label { Name = "inner" };
        var looseLog = Track(loose, inner);
        loose.Children.Add(inner);
        Assert.Empty(looseLog);
        second.Children.Add(loose);
        Assert.Equal(["+loose", "+inner"], looseLog);
        second.Children.Clear();
        Assert.Equal(["+loose", "+inner", "-inner", "-loose"], looseLog);
    }

    [Fact]
    public void DataContextInheritsFromAncestorsWindowAndApplication()
    {
        var field = new Field();
        var panel = new Panel();
        panel.Children.Add(field);
        var window = NewWindow(panel);
        var app = new Application { ConfirmExit = false, DataContext = "app" };
        int changes = 0;
        field.DataContextChanged += (_, _) => changes++;

        Assert.Null(field.DataContext);
        app.Windows.Add(window);
        Assert.Equal("app", field.DataContext);
        Assert.Equal(1, changes);

        app.DataContext = "app2";
        Assert.Equal("app2", field.DataContext);
        Assert.Equal(2, changes);

        window.DataContext = "window";
        Assert.Equal("window", field.DataContext);
        panel.DataContext = "panel";
        Assert.Equal("panel", field.DataContext);
        Assert.Equal(4, changes);

        app.DataContext = "app3"; // shadowed by the panel
        Assert.Equal(4, changes);

        panel.DataContext = null;
        window.DataContext = null;
        Assert.Equal("app3", field.DataContext);
        Assert.Equal(6, changes);

        var other = new Panel { DataContext = "other" };
        window.Children.Add(other);
        panel.Children.Remove(field);
        Assert.Null(field.DataContext);
        other.Children.Add(field);
        Assert.Equal("other", field.DataContext);
        Assert.Equal(8, changes);
    }

    [Fact]
    public void SubMenuItemsInheritTheMenuDataContext()
    {
        var sub = new SubMenu { Text = "&More" };
        var item = new MenuItem { Text = "&Inner" };
        sub.Children.Add(item);
        var menu = new Menu { Title = "&File" };
        menu.Children.Add(sub);
        var app = new Application { ConfirmExit = false, DataContext = "vm" };
        app.Menus.Add(menu);

        Assert.Equal("vm", item.DataContext);
        Assert.True(item.IsAttachedToTree);
        menu.DataContext = "menu";
        Assert.Equal("menu", item.DataContext);
    }

    private sealed class TestCommand : ICommand
    {
        public bool Enabled { get; set; }

        public List<object?> Executed { get; } = [];

        public int Subscribers => CanExecuteChanged?.GetInvocationList().Length ?? 0;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => Enabled;

        public void Execute(object? parameter) => Executed.Add(parameter);

        public void Raise(bool enabled)
        {
            Enabled = enabled;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [Fact]
    public void CommandCanExecuteDrivesEffectiveEnabledWhileAttached()
    {
        var command = new TestCommand();
        var button = new Button { Text = "Go", Command = command, CommandParameter = 42 };
        var window = NewWindow(button);
        var d = Start(window);

        Assert.True(button.Enabled);
        Assert.False(button.IsEffectivelyEnabled);
        Assert.Equal(1, command.Subscribers);
        d.Key(Key.Enter);
        Assert.Empty(command.Executed);

        command.Raise(true);
        Assert.True(button.IsEffectivelyEnabled);
        window.Activate(button);
        d.Key(Key.Enter);
        Assert.Equal([42], command.Executed);

        button.Enabled = false;
        Assert.False(button.IsEffectivelyEnabled);
        button.Enabled = true;

        d.Application.Windows.Remove(window);
        Assert.Equal(0, command.Subscribers);
    }

    [Fact]
    public void NamedCommandsStillDispatchThroughTheApplication()
    {
        var button = new Button { Text = "Quit", Command = Command.Exit };
        var d = Start(NewWindow(button));
        Assert.True(button.IsEffectivelyEnabled);
        d.Key(Key.Enter);
        Assert.False(d.Application.IsRunning);
    }

    private static readonly Dictionary<string, Func<(Window Window, Widget Target, Action<UiDriver> Input)>> s_userInput = new()
    {
        ["Field.Text"] = () =>
        {
            var field = new Field { Width = 10 };
            return (NewWindow(field), field, d => d.Type("ab"));
        },
        ["CheckBox.Checked"] = () =>
        {
            var box = new CheckBox { Text = "C" };
            return (NewWindow(box), box, d => d.Type(" "));
        },
        ["RadioGroup.Selected"] = () =>
        {
            var group = new RadioGroup { Text = "G" };
            group.Children.Add(new RadioButton { Text = "A" });
            group.Children.Add(new RadioButton { Text = "B" });
            return (NewWindow(group), group, d => d.Type(" "));
        },
        ["List.SelectedIndex"] = () => NewList(d => d.Key(Key.Down)),
        ["List.SelectedItem"] = () => NewList(d => d.Key(Key.Down)),
        ["ScrollableWidget.VerticalValue"] = () => NewList(d => d.Key(Key.End)),
        ["ScrollableWidget.HorizontalValue"] = () => NewList(d => d.Key(Key.Right)),
        ["ComboBox.Text"] = () => NewCombo(),
        ["ComboBox.SelectedIndex"] = () => NewCombo(),
        ["ComboBox.SelectedItem"] = () => NewCombo(),
        ["TreeView.SelectedItem"] = () =>
        {
            var root = new TreeItem { Text = "root" };
            root.Items.Add(new TreeItem { Text = "a" });
            var tree = new TreeView { Width = 20, Height = 6, Root = root };
            return (NewWindow(tree), tree, d => d.Key(Key.Down));
        },
        ["ScrollerBase.Value"] = () =>
        {
            var scroller = new VScroller { Height = 5, Maximum = 10 };
            return (NewWindow(scroller), scroller, d => d.Click(1, 6));
        },
        ["ScrollableWindow.VerticalValue"] = () => NewScrollableWindow(d => d.Click(19, 7)),
        ["ScrollableWindow.HorizontalValue"] = () => NewScrollableWindow(d => d.Click(17, 8)),
        ["SplitPane.Split"] = () =>
        {
            var split = new SplitPane();
            split.Children.Add(new Panel());
            split.Children.Add(new Panel());
            var window = new Window { Title = "S", X = 0, Y = 0, Width = 21, Height = 8 };
            window.Children.Add(split);
            return (window, split, d => d.Drag(10, 4, 6, 4));
        },
    };

    private static (Window, Widget, Action<UiDriver>) NewList(Action<UiDriver> input)
    {
        var list = new List { Width = 8, Height = 4 };
        foreach (var s in new[] { "one", "two", "three", "four", "a very long item" }) list.Items.Add(s);
        return (NewWindow(list), list, input);
    }

    private static (Window, Widget, Action<UiDriver>) NewCombo()
    {
        var combo = new ComboBox { Width = 12 };
        foreach (var s in new[] { "red", "green", "blue" }) combo.Items.Add(s);
        return (NewWindow(combo), combo, d =>
        {
            d.Click(11, 2);
            d.Key(Key.Down);
            d.Key(Key.Enter);
        }
        );
    }

    private static (Window, Widget, Action<UiDriver>) NewScrollableWindow(Action<UiDriver> input)
    {
        var window = new ScrollableWindow
        {
            Title = "S",
            X = 0,
            Y = 0,
            Width = 20,
            Height = 8,
            HasVerticalScroller = true,
            HasHorizontalScroller = true,
        };
        return (window, window, input);
    }

    [Fact]
    public void EveryTwoWayByDefaultPropertyNotifiesOnUserInput()
    {
        var attributed = typeof(Widget).Assembly.GetTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(p => p.IsDefined(typeof(BindsTwoWayByDefaultAttribute), true))
            .Select(p => p.DeclaringType!.Name + "." + p.Name)
            .Order()
            .ToList();
        Assert.Equal(s_userInput.Keys.Order(), attributed);

        foreach (var (key, scenario) in s_userInput)
        {
            var (window, target, input) = scenario();
            string property = key[(key.IndexOf('.') + 1)..];
            var d = Start(window);
            var before = target.GetType().GetProperty(property)!.GetValue(target);
            var raised = new List<string?>();
            PropertyChangedEventHandler handler = (_, e) => raised.Add(e.PropertyName);
            target.PropertyChanged += handler;
            input(d);
            target.PropertyChanged -= handler;
            var after = target.GetType().GetProperty(property)!.GetValue(target);
            Assert.True(!Equals(before, after), $"{key}: input did not change the value");
            Assert.True(raised.Contains(property), $"{key}: PropertyChanged not raised");
        }
    }

    private sealed record Person(string Name)
    {
        public ObservableCollection<Person> Children { get; } = [];
    }

    private static CompiledBinding NameBinding() =>
        new(new CompiledBindingPath(new PathNode("Name", typeof(string), o => ((Person)o).Name, null, false)));

    private static CompiledBinding ChildrenBinding() =>
        new(new CompiledBindingPath(new PathNode("Children", typeof(ObservableCollection<Person>), o => ((Person)o).Children, null, false)));

    [Fact]
    public void ListFollowsItemsSourceChangesAndKeepsTheSelectedItem()
    {
        var ann = new Person("Ann");
        var bob = new Person("Bob");
        var people = new ObservableCollection<Person> { ann, bob };
        var list = new List { Width = 10, Height = 6, DisplayMemberBinding = NameBinding(), ItemsSource = people };
        Assert.Equal(["Ann", "Bob"], list.Items);
        Assert.Throws<InvalidOperationException>(() => list.Items.Add("x"));

        var d = Start(NewWindow(list));
        list.SelectedItem = bob;
        Assert.Equal(1, list.SelectedIndex);
        var raised = new List<string?>();
        list.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        people.Insert(0, new Person("Cy"));
        Assert.Equal(["Cy", "Ann", "Bob"], list.Items);
        Assert.Same(bob, list.SelectedItem);
        Assert.Equal(2, list.SelectedIndex);
        Assert.Equal([nameof(List.SelectedIndex)], raised);

        people.Move(2, 0);
        Assert.Equal(["Bob", "Cy", "Ann"], list.Items);
        Assert.Same(bob, list.SelectedItem);

        people[1] = new Person("Dee");
        Assert.Equal(["Bob", "Dee", "Ann"], list.Items);
        people.RemoveAt(2);
        Assert.Equal(["Bob", "Dee"], list.Items);
        d.Pump();
        Assert.Equal("Bob", d.Row(2, 1, 3));

        people.Remove(bob);
        Assert.Null(list.SelectedItem);
        Assert.Equal(-1, list.SelectedIndex);

        people.Clear();
        Assert.Empty(list.Items);

        // Detached lists stop observing and resynchronise when attached again.
        d.Application.Windows.Remove(list.Window!);
        people.Add(ann);
        Assert.Empty(list.Items);
        d.Application.Windows.Add(list.Window!);
        Assert.Equal(["Ann"], list.Items);

        list.ItemsSource = null;
        Assert.Empty(list.Items);
        list.Items.Add("plain");
        Assert.Throws<InvalidOperationException>(() => list.ItemsSource = people);
    }

    [Fact]
    public void ComboBoxShowsAndSelectsSourceItems()
    {
        var ann = new Person("Ann");
        var bob = new Person("Bob");
        var people = new ObservableCollection<Person> { ann };
        var combo = new ComboBox { Width = 12, DisplayMemberBinding = NameBinding(), ItemsSource = people };
        var d = Start(NewWindow(combo));

        people.Add(bob);
        Assert.Equal(["Ann", "Bob"], combo.Items);
        combo.SelectedItem = bob;
        Assert.Equal("Bob", combo.Text);
        Assert.Equal(1, combo.SelectedIndex);

        d.Click(11, 2);
        d.Key(Key.Up);
        d.Key(Key.Enter);
        Assert.Same(ann, combo.SelectedItem);
        Assert.Equal("Ann", combo.Text);
    }

    [Fact]
    public void TreeViewBuildsItemsFromSourceAndChildrenBinding()
    {
        var a = new Person("a");
        var a1 = new Person("a1");
        a.Children.Add(a1);
        var b = new Person("b");
        var roots = new ObservableCollection<Person> { a, b };
        var tree = new TreeView
        {
            Width = 20,
            Height = 7,
            DisplayMemberBinding = NameBinding(),
            ChildrenBinding = ChildrenBinding(),
            ItemsSource = roots,
        };
        Assert.Throws<InvalidOperationException>(() => tree.Root = new TreeItem());
        var d = Start(NewWindow(tree));

        Assert.Equal(["a", "b"], tree.VisibleItems.Select(i => i.Text));
        Assert.True(tree.VisibleItems[0].IsExpandable);
        Assert.False(tree.VisibleItems[1].IsExpandable);
        Assert.Equal("├─[+] a", d.Row(2, 1, 7));

        d.Key(Key.Down);
        Assert.Same(a, tree.SelectedItem);
        d.Press("Space");
        Assert.Equal(["a", "a1", "b"], tree.VisibleItems.Select(i => i.Text));

        a.Children.Add(new Person("a2"));
        Assert.Equal(["a", "a1", "a2", "b"], tree.VisibleItems.Select(i => i.Text));
        roots.Insert(0, new Person("z"));
        Assert.Equal(["z", "a", "a1", "a2", "b"], tree.VisibleItems.Select(i => i.Text));
        Assert.Same(a, tree.SelectedItem);

        tree.SelectedItem = a1;
        Assert.Same(a1, tree.SelectedItem);
        Assert.Equal("a1", tree.SelectedTreeItem!.Text);
        a.Children.Remove(a1);
        Assert.Null(tree.SelectedItem);
    }

    [Fact]
    public void ValidationErrorsUseErrorColoursAndShowInTheStatusBar()
    {
        var field = new Field { Width = 10 };
        var box = new CheckBox { Text = "C", Y = 2 };
        var d = Start(NewWindow(field, box));
        Assert.Equal(d.Theme("tfield.active"), d.Style(1, 2));

        var expression = BindingOperations.Bind(field, "Tag", (_, _) => { }, typeof(object), new CompiledBinding());
        Validation.SetErrors(expression, [new ValidationError("Required", null, expression)]);
        d.Pump();
        Assert.Equal(d.Theme("tfield.error"), d.Style(1, 2));
        Assert.Equal(" Required", d.Row(13, 0, 9));
        Assert.Equal(d.Theme("tstatusbar.error"), d.Style(0, 13));

        var boxExpression = BindingOperations.Bind(box, "Tag", (_, _) => { }, typeof(object), new CompiledBinding());
        Validation.SetErrors(boxExpression, [new ValidationError("No", null, boxExpression)]);
        d.Pump();
        Assert.Equal(d.Theme("tcheckbox.error"), d.Style(1, 4));

        Validation.ClearErrors(expression);
        d.Pump();
        Assert.Equal(d.Theme("tfield.active"), d.Style(1, 2));
        Assert.NotEqual(d.Theme("tstatusbar.error"), d.Style(0, 13));
    }
}
