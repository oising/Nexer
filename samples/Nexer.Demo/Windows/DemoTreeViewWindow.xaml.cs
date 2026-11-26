// Ported from Jexer demo/DemoTreeViewWindow.java (MIT, © Autumn Lamonte).
using Nexer.Ui;
using Nexer.Ui.Controls;

namespace Nexer.Demo;

/// <summary>
/// A tree authored in XAML, a directory tree with the files of the selected directory, and a list
/// (Jexer <c>DemoTreeViewWindow</c>).
/// </summary>
public partial class DemoTreeViewWindow : Window
{
    /// <summary>Creates the window showing the current directory.</summary>
    public DemoTreeViewWindow()
        : this(Directory.GetCurrentDirectory())
    {
    }

    /// <summary>Creates the window showing <paramref name="path"/>.</summary>
    public DemoTreeViewWindow(string path)
    {
        InitializeComponent();
        DirectoryTreeItem.OpenPath(DirectoryTree, path);
        Files.Path = path;
    }

    private void OnDirectoryChanged(object? sender, EventArgs e)
    {
        if (DirectoryTree.SelectedItem is DirectoryTreeItem { Path: { } path })
        {
            Files.Path = path;
            Selection.Text = "Directory: " + path;
        }
    }

    private void OnTreeActivated(object? sender, EventArgs e) => Selection.Text = "Tree: " + Tree.SelectedTreeItem?.Text;

    private void OnFruitChanged(object? sender, EventArgs e) => Selection.Text = "List: " + Fruit.SelectedItem;
}
