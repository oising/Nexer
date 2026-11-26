using Nexer.Ui.Controls;
using Nexer.Ui.Testing;

namespace Nexer.Ui.Tests;

public class ListTests
{
    // A window at desktop (0,0) lands on screen row 1 (below the menu bar); its client origin is screen (1,2).
    private static UiDriver Start(Window window)
    {
        var app = new Application { ConfirmExit = false };
        app.Windows.Add(window);
        return new UiDriver(app, 40, 14);
    }

    private static Window NewWindow() => new() { Title = "L", X = 0, Y = 0, Width = 30, Height = 12 };

    [Fact]
    public void ListDrawsSelectsAndScrolls()
    {
        var list = new List { X = 0, Y = 0, Width = 10, Height = 4 };
        foreach (var s in new[] { "one", "two", "three", "four", "five" }) list.Items.Add(s);
        var window = NewWindow();
        window.Children.Add(list);
        int changes = 0, activations = 0;
        list.SelectionChanged += (_, _) => changes++;
        list.ItemActivated += (_, _) => activations++;
        var d = Start(window);

        Assert.Equal("one      ▲", d.Row(2, 1, 10));
        Assert.Equal("two      ", d.Row(3, 1, 9));
        Assert.Equal("three    ", d.Row(4, 1, 9));
        Assert.Equal(-1, list.SelectedIndex);

        d.Key(Nexer.Input.Key.Down);
        Assert.Equal(0, list.SelectedIndex);
        Assert.Equal(d.Theme("tlist.selected"), d.Style(1, 2));
        Assert.Equal(d.Theme("tlist"), d.Style(1, 3));

        d.Key(Nexer.Input.Key.End);
        Assert.Equal(4, list.SelectedIndex);
        Assert.Equal("five", list.SelectedItem);
        Assert.Equal("five     ", d.Row(4, 1, 9));

        d.Key(Nexer.Input.Key.Home);
        Assert.Equal("one      ", d.Row(2, 1, 9));
        d.Key(Nexer.Input.Key.Enter);
        Assert.Equal(1, activations);

        d.Click(1, 3);
        Assert.Equal(1, list.SelectedIndex);
        d.DoubleClick(1, 4);
        Assert.Equal(2, list.SelectedIndex);
        Assert.Equal(2, activations);
        Assert.True(changes >= 4);
    }

    [Fact]
    public void ListAppliesPendingSelectionWhenItemsArrive()
    {
        var list = new List { SelectedIndex = 1, Width = 10, Height = 4 };
        list.Items.ReplaceAll(["a", "b", "c"]);
        Assert.Equal(1, list.SelectedIndex);
        Assert.Equal("b", list.SelectedItem);
    }

    [Fact]
    public void ComboBoxOpensPicksAndCloses()
    {
        var combo = new ComboBox { X = 0, Y = 0, Width = 12 };
        foreach (var s in new[] { "red", "green", "blue" }) combo.Items.Add(s);
        var other = new Button { Text = "OK", X = 0, Y = 6 };
        var window = NewWindow();
        window.Children.Add(combo);
        window.Children.Add(other);
        int changed = 0;
        combo.Changed += (_, _) => changed++;
        var d = Start(window);

        Assert.Equal("▌↓▐", d.Row(2, 10, 3));
        Assert.False(combo.IsListOpen);

        d.Click(11, 2);
        Assert.True(combo.IsListOpen);
        Assert.Equal("red", d.Row(3, 1, 3));
        Assert.Equal("green", d.Row(4, 1, 5));
        Assert.Equal("◀", d.Cell(1, 5)); // default MaxValuesHeight 3 = two rows + scroller

        d.Key(Nexer.Input.Key.Down);
        d.Key(Nexer.Input.Key.Down);
        d.Key(Nexer.Input.Key.Enter);
        Assert.False(combo.IsListOpen);
        Assert.Equal("green", combo.Text);
        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal(1, changed);
        Assert.Equal("green", d.Row(2, 1, 5));

        d.Press("Alt+Down");
        Assert.True(combo.IsListOpen);
        d.Key(Nexer.Input.Key.Escape);
        Assert.False(combo.IsListOpen);
        Assert.Equal("green", combo.Text);
    }

    private static TreeItem Item(string text, params TreeItem[] children)
    {
        var item = new TreeItem { Text = text };
        foreach (var c in children) item.Items.Add(c);
        return item;
    }

    [Fact]
    public void TreeViewDrawsExpandsAndSelects()
    {
        var root = Item("root", Item("a", Item("a1"), Item("a2")), Item("b"));
        var tree = new TreeView { X = 0, Y = 0, Width = 20, Height = 7, Root = root };
        var window = NewWindow();
        window.Children.Add(tree);
        int activated = 0;
        tree.ItemActivated += (_, _) => activated++;
        var d = Start(window);

        Assert.Equal("root", d.Row(2, 1, 4));
        Assert.Equal("├─[-] a", d.Row(3, 1, 7));
        Assert.Equal("│ ├─ a1", d.Row(4, 1, 7));
        Assert.Equal("│ └─ a2", d.Row(5, 1, 7));
        Assert.Equal("└─ b", d.Row(6, 1, 4));
        Assert.Equal(d.Theme("ttreeview.expandbutton"), d.Style(4, 3));

        d.Key(Nexer.Input.Key.Down);
        Assert.Same(root, tree.SelectedItem);
        d.Key(Nexer.Input.Key.Down);
        Assert.Equal("a", tree.SelectedTreeItem!.Text);
        Assert.Equal(d.Theme("ttreeview.selected"), d.Style(7, 3));

        d.Press("Space");
        Assert.False(tree.SelectedTreeItem!.IsExpanded);
        Assert.Equal("├─[+] a", d.Row(3, 1, 7));
        Assert.Equal("└─ b", d.Row(4, 1, 4));

        d.Click(4, 3);
        Assert.True(tree.SelectedTreeItem!.IsExpanded);
        Assert.Equal("│ ├─ a1", d.Row(4, 1, 7));

        d.Click(6, 5);
        Assert.Equal("a2", tree.SelectedTreeItem!.Text);
        Assert.True(tree.SelectedTreeItem!.IsSelected);
        Assert.Equal(1, activated);
        d.Key(Nexer.Input.Key.Enter);
        Assert.Equal(2, activated);
    }

    [Fact]
    public void DirectoryControlsListTheFileSystem()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "list-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "sub", "deep"));
        Directory.CreateDirectory(Path.Combine(dir, "other"));
        Directory.CreateDirectory(Path.Combine(dir, ".hidden"));
        File.WriteAllBytes(Path.Combine(dir, "b.txt"), new byte[3000]);
        File.WriteAllText(Path.Combine(dir, "a.log"), "x");
        File.WriteAllText(Path.Combine(dir, ".dot"), "x");
        try
        {
            var files = new DirectoryList { Width = 20, Height = 5, Path = dir };
            Assert.Equal(["a.log", "b.txt"], files.Files.Select(f => f.Name));
            Assert.Equal("b.txt            2k", files.Items[1]);
            Assert.Equal("a.log", files.SelectedFile!.Name);
            files.Filter = @".*\.txt";
            Assert.Equal("b.txt", Assert.Single(files.Files).Name);

            var tree = new TreeView { Width = 30, Height = 10 };
            var item = DirectoryTreeItem.OpenPath(tree, Path.Combine(dir, "sub"));
            Assert.Equal("sub", item.Text);
            Assert.Same(item, tree.SelectedItem);
            Assert.Equal(["deep"], item.Items.Select(i => i.Text));
            Assert.Equal(["other", "sub"], item.Parent!.Items.Select(i => i.Text));
            Assert.False(item.Items[0].IsExpanded);
            Assert.True(item.Items[0].IsExpandable);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
