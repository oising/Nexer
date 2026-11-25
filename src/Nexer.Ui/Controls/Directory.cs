// Ported from Jexer TDirectoryTreeItem.java and TDirectoryList.java (MIT, © Autumn Lamonte).
using System.Globalization;
using System.Text.RegularExpressions;

namespace Nexer.Ui.Controls;

/// <summary>
/// A <see cref="TreeItem"/> for a directory whose children (sub-directories, excluding names starting with '.') are
/// loaded when it is expanded (Jexer <c>TDirectoryTreeItem</c>). Use <see cref="OpenPath"/> to show a directory
/// inside the tree of its file-system root.
/// </summary>
public class DirectoryTreeItem : TreeItem
{
    private DirectoryInfo? directory;

    /// <summary>Creates an item; set <see cref="Path"/> to load it.</summary>
    public DirectoryTreeItem()
    {
    }

    /// <summary>
    /// The directory path. Setting it shows the directory name (the full path for a file-system root) and loads the
    /// sub-directories if the item is expanded.
    /// </summary>
    public string? Path
    {
        get => directory?.FullName;
        set
        {
            directory = string.IsNullOrEmpty(value) ? null : new DirectoryInfo(System.IO.Path.GetFullPath(value));
            if (directory == null) return;
            Text = directory.Parent == null ? directory.FullName : directory.Name;
            OnExpand();
        }
    }

    /// <summary>The directory, or null.</summary>
    public DirectoryInfo? Directory => directory;

    /// <inheritdoc/>
    protected internal override void OnExpand()
    {
        if (directory == null) return;
        DirectoryInfo[] subdirectories;
        try
        {
            subdirectories = directory.GetDirectories();
            IsSelectable = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            subdirectories = [];
            IsSelectable = false;
        }

        IsExpandable = true;
        if (!IsExpanded)
        {
            Items.ReplaceAll([]);
            return;
        }

        // Jexer sorts tree items by text (String.compareTo, i.e. ordinal).
        Items.ReplaceAll(subdirectories
            .Where(d => !d.Name.StartsWith('.'))
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .Select(d => new DirectoryTreeItem { IsExpanded = false, Path = d.FullName }));
    }

    /// <summary>
    /// Makes <paramref name="view"/> show the file-system root of <paramref name="path"/> with every directory down to
    /// <paramref name="path"/> expanded, and selects (and centres) that directory (Jexer's <c>openParents</c>).
    /// </summary>
    /// <returns>The item for <paramref name="path"/>.</returns>
    public static DirectoryTreeItem OpenPath(TreeView view, string path)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(path);
        var target = new DirectoryInfo(System.IO.Path.GetFullPath(path));
        var names = new List<string>();
        var rootDir = target;
        while (rootDir.Parent != null)
        {
            names.Add(rootDir.Name);
            rootDir = rootDir.Parent;
        }

        names.Reverse();
        var root = new DirectoryTreeItem { Path = rootDir.FullName };
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var current = root;
        foreach (var name in names)
        {
            var child = current.Items.OfType<DirectoryTreeItem>().FirstOrDefault(i => string.Equals(i.Text, name, comparison));
            if (child == null) break;
            current = child;
            current.IsExpanded = true;
            current.OnExpand();
        }

        view.Root = root;
        view.Select(current, true);
        return current;
    }
}

/// <summary>
/// A <see cref="List"/> of the files (not directories, not names starting with '.') in <see cref="Path"/>, shown as
/// name and size in KB (Jexer <c>TDirectoryList</c>). <see cref="Filters"/> limits the files to names matching any
/// of the regular expressions.
/// </summary>
public class DirectoryList : List
{
    private readonly List<FileInfo> files = [];
    private string? path;
    private IReadOnlyList<string>? filters;

    /// <summary>The directory listed. Setting it reloads the list and selects the first file.</summary>
    public string? Path
    {
        get => path;
        set
        {
            path = string.IsNullOrEmpty(value) ? null : System.IO.Path.GetFullPath(value);
            Refresh();
        }
    }

    /// <summary>Regular expressions a file name must match entirely (any of them); null lists every file.</summary>
    public IReadOnlyList<string>? Filters
    {
        get => filters;
        set
        {
            filters = value;
            Refresh();
        }
    }

    /// <summary><see cref="Filters"/> as text: regular expressions separated by <c>';'</c>.</summary>
    public string? Filter
    {
        get => filters == null ? null : string.Join(';', filters);
        set => Filters = string.IsNullOrEmpty(value) ? null : value.Split(';', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>The listed files, in display order.</summary>
    public IReadOnlyList<FileInfo> Files => files;

    /// <summary>The selected file, or null.</summary>
    public FileInfo? SelectedFile => SelectedIndex >= 0 && SelectedIndex < files.Count ? files[SelectedIndex] : null;

    /// <summary>Re-reads the directory.</summary>
    public void Refresh()
    {
        files.Clear();
        if (path != null)
        {
            FileInfo[] all;
            try
            {
                all = new DirectoryInfo(path).GetFiles();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                all = [];
            }

            var patterns = filters?.Select(f => new Regex("^(?:" + f + ")$", RegexOptions.CultureInvariant)).ToList();
            // Ordinal sort for a stable order (Jexer uses the unsorted File.listFiles order).
            foreach (var file in all.OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                if (file.Name.StartsWith('.')) continue;
                if (patterns != null && !patterns.Any(p => p.IsMatch(file.Name))) continue;
                files.Add(file);
            }
        }

        Items.ReplaceAll(files.Select(Render));
        if (files.Count > 0) SelectedIndex = 0;
    }

    /// <inheritdoc/>
    public override void ReflowData()
    {
        for (int i = 0; i < files.Count && i < Items.Count; i++) Items.SetSilently(i, Render(files[i]));
        base.ReflowData();
    }

    private string Render(FileInfo file)
    {
        string name = file.Name;
        int maxWidth = Width - 8;
        if (TextMetrics.Width(name) > maxWidth) name = TextMetrics.Truncate(name, maxWidth - 3) + "...";
        long size;
        try
        {
            size = file.Length / 1024;
        }
        catch (IOException)
        {
            size = 0;
        }

        return name + new string(' ', Math.Max(0, maxWidth - TextMetrics.Width(name))) + " " +
            size.ToString(CultureInfo.InvariantCulture).PadLeft(5) + "k";
    }
}
