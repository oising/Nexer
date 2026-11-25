// Ported from Jexer TTreeItem.java, TTreeView.java and TTreeViewScrollable.java (MIT, © Autumn Lamonte).
using System.Collections.ObjectModel;
using Nexer.Input;
using Nexer.Ui.Markup;

namespace Nexer.Ui.Controls;

/// <summary>The child items of a <see cref="TreeItem"/>. Changes refresh the owning <see cref="TreeView"/>.</summary>
public sealed class TreeItemCollection : Collection<TreeItem>
{
    private readonly TreeItem owner;
    private bool suppress;

    internal TreeItemCollection(TreeItem owner) => this.owner = owner;

    /// <summary>Replaces all items with one refresh.</summary>
    public void ReplaceAll(IEnumerable<TreeItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        suppress = true;
        try
        {
            Clear();
            foreach (var item in items) Add(item);
        }
        finally
        {
            suppress = false;
        }

        owner.NotifyChanged();
    }

    /// <inheritdoc/>
    protected override void InsertItem(int index, TreeItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Parent != null) throw new InvalidOperationException("The tree item already has a parent.");
        if (item.TreeViewOwner != null) throw new InvalidOperationException("The tree item is the root of a tree view.");
        base.InsertItem(index, item);
        item.Parent = owner;
        Notify();
    }

    /// <inheritdoc/>
    protected override void SetItem(int index, TreeItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Parent != null) throw new InvalidOperationException("The tree item already has a parent.");
        this[index].Parent = null;
        base.SetItem(index, item);
        item.Parent = owner;
        Notify();
    }

    /// <inheritdoc/>
    protected override void RemoveItem(int index)
    {
        this[index].Parent = null;
        base.RemoveItem(index);
        Notify();
    }

    /// <inheritdoc/>
    protected override void ClearItems()
    {
        foreach (var item in this) item.Parent = null;
        base.ClearItems();
        Notify();
    }

    private void Notify()
    {
        if (!suppress) owner.NotifyChanged();
    }
}

/// <summary>
/// A node of a <see cref="TreeView"/> (Jexer <c>TTreeItem</c>). Content = child items. The root
/// (<see cref="TreeView.Root"/>) is always expanded and has no expander.
/// </summary>
public class TreeItem
{
    private string text = "";
    private bool expanded = true;
    private bool expandable;
    private bool selectable = true;

    /// <summary>Creates an item.</summary>
    public TreeItem()
    {
        Items = new TreeItemCollection(this);
    }

    /// <summary>Child items.</summary>
    [Content]
    public TreeItemCollection Items { get; }

    /// <summary>The displayed text.</summary>
    public string Text
    {
        get => text;
        set
        {
            text = value ?? "";
            NotifyChanged();
        }
    }

    /// <summary>Whether the children are shown (default true; the root is always expanded).</summary>
    public bool IsExpanded
    {
        get => expanded || IsViewRoot;
        set
        {
            expanded = value;
            NotifyChanged();
        }
    }

    /// <summary>Whether an expander (<c>[+]</c>/<c>[-]</c>) is drawn. True when set or when the item has children.</summary>
    public bool IsExpandable
    {
        get => expandable || Items.Count > 0 || IsViewRoot;
        set
        {
            expandable = value;
            NotifyChanged();
        }
    }

    /// <summary>Whether the item can be expanded/collapsed (false draws it with <c>"ttreeview.unreadable"</c>).</summary>
    public bool IsSelectable
    {
        get => selectable;
        set
        {
            selectable = value;
            NotifyChanged();
        }
    }

    /// <summary>True when this is the tree view's selected item.</summary>
    public bool IsSelected => TreeView?.SelectedItem == this;

    /// <summary>The parent item, or null for a root.</summary>
    public TreeItem? Parent { get; internal set; }

    /// <summary>Depth below the root (0 for the root).</summary>
    public int Level => Parent == null ? 0 : Parent.Level + 1;

    /// <summary>Arbitrary user data.</summary>
    public object? Tag { get; set; }

    /// <summary>The tree view showing this item, or null.</summary>
    public TreeView? TreeView
    {
        get
        {
            var item = this;
            while (item.Parent != null) item = item.Parent;
            return item.TreeViewOwner;
        }
    }

    internal TreeView? TreeViewOwner { get; set; }

    private bool IsViewRoot => Parent == null && TreeViewOwner != null;

    internal string Prefix { get; set; } = "";

    internal bool Last { get; set; }

    /// <summary>
    /// Called after the user expands or collapses the item (Jexer <c>onExpand</c>); override to load children lazily.
    /// </summary>
    protected internal virtual void OnExpand()
    {
    }

    internal void NotifyChanged() => TreeView?.ReflowData();

    internal int ExpanderX => Level == 0 || !IsExpandable ? 0 : Prefix.Length + 3;

    internal void ExpandTree(string prefix, bool last, List<TreeItem> rows)
    {
        Last = last;
        Prefix = prefix;
        rows.Add(this);
        if (Items.Count == 0 || !IsExpanded) return;
        string newPrefix = prefix;
        if (Level > 0) newPrefix += last ? "  " : GraphicsChars.CP437[0xB3] + " ";
        for (int i = 0; i < Items.Count; i++) Items[i].ExpandTree(newPrefix, i == Items.Count - 1, rows);
    }

    /// <inheritdoc/>
    public override string ToString() => text;
}

/// <summary>
/// A scrollable tree (Jexer <c>TTreeViewScrollable</c> with its <c>TTreeView</c>). Set <see cref="Root"/> to a
/// <see cref="TreeItem"/> hierarchy. Up/Down move the selection, Left/Right/Space expand or collapse, Enter or a click
/// raises <see cref="ItemActivated"/>, PgUp/PgDn/Home/End move by pages, and Shift/Ctrl/Alt+arrows scroll.
/// </summary>
public class TreeView : ScrollableWidget
{
    private readonly List<TreeItem> rows = [];
    private TreeItem? root;
    private TreeItem? selected;
    private int topLine;
    private int maxLineWidth;
    private bool syncing;

    /// <summary>Creates an empty tree view.</summary>
    public TreeView()
    {
        VerticalScroller = new VScroller();
        HorizontalScroller = new HScroller();
        Children.Add(VerticalScroller);
        Children.Add(HorizontalScroller);
        VerticalScroller.ValueChanged += (_, _) =>
        {
            if (!syncing) topLine = VerticalValue;
            Invalidate();
        };
        HorizontalScroller.ValueChanged += (_, _) => Invalidate();
    }

    /// <summary>The root item.</summary>
    public TreeItem? Root
    {
        get => root;
        set
        {
            if (root == value) return;
            if (value?.Parent != null) throw new InvalidOperationException("The root item must not have a parent.");
            if (root != null) root.TreeViewOwner = null;
            root = value;
            if (value != null) value.TreeViewOwner = this;
            selected = null;
            topLine = 0;
            ReflowData();
        }
    }

    /// <summary>The selected item. Setting it scrolls the item to the middle of the view.</summary>
    public TreeItem? SelectedItem
    {
        get => selected;
        set => Select(value, true);
    }

    /// <summary>The visible (expanded) items in display order.</summary>
    public IReadOnlyList<TreeItem> VisibleItems => rows;

    /// <summary>Raised on Enter or when an item's text is clicked (Jexer tree action).</summary>
    public event EventHandler? ItemActivated;

    /// <summary>Raised when <see cref="SelectedItem"/> changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <inheritdoc/>
    protected internal override bool IsKeyboardLeaf => true;

    /// <inheritdoc/>
    protected internal override bool UpDownSwitchesWidget => false;

    /// <inheritdoc/>
    protected internal override bool LeftRightSwitchesWidget => false;

    private int ViewHeight => Height - 1;

    /// <summary>Selects an item, optionally centring it vertically (Jexer <c>setSelected</c>).</summary>
    public void Select(TreeItem? item, bool centerWindow)
    {
        bool changed = selected != item;
        selected = item;
        if (centerWindow)
        {
            RebuildRows();
            int y = item == null ? rows.Count : rows.IndexOf(item);
            if (y < 0) y = rows.Count;
            topLine = y - (ViewHeight - 1) / 2;
            if (topLine > rows.Count - ViewHeight) topLine = rows.Count - ViewHeight;
            if (topLine < 0) topLine = 0;
        }

        ReflowData();
        if (changed) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildRows()
    {
        rows.Clear();
        root?.ExpandTree("", true, rows);
        foreach (var item in rows)
        {
            maxLineWidth = Math.Max(maxLineWidth, TextMetrics.Width(item.Text) + item.Prefix.Length + 4);
        }
    }

    /// <inheritdoc/>
    public override void ReflowData()
    {
        RebuildRows();
        if (selected != null && selected.TreeView != this) selected = null;
        syncing = true;
        try
        {
            if (VerticalScroller is { } v)
            {
                v.Maximum = Math.Max(0, rows.Count - ViewHeight);
                topLine = Math.Clamp(topLine, 0, v.Maximum);
                v.Value = topLine;
            }

            if (HorizontalScroller is { } h)
            {
                h.Maximum = Math.Max(0, maxLineWidth - 2);
                if (h.Value > h.Maximum) h.Value = h.Maximum;
            }
        }
        finally
        {
            syncing = false;
        }

        Invalidate();
    }

    private void Toggle(TreeItem item)
    {
        if (item.Level == 0) return;
        if (item.IsSelectable)
        {
            item.IsExpanded = !item.IsExpanded;
            Select(item, false);
        }

        item.OnExpand();
        ReflowData();
    }

    private void ActivateItem(TreeItem item)
    {
        Select(item, false);
        ItemActivated?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    protected internal override void OnMouseDown(UiMouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        if (mouse.IsWheelUp) VerticalScroller?.Decrement();
        else if (mouse.IsWheelDown) VerticalScroller?.Increment();
        else base.OnMouseDown(mouse);
    }

    /// <inheritdoc/>
    protected internal override void OnMouseUp(UiMouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        if (mouse.X >= 0 && mouse.X < Width - 1 && mouse.Y >= 0 && mouse.Y < ViewHeight)
        {
            int index = topLine + mouse.Y;
            if (index < rows.Count)
            {
                var item = rows[index];
                if (mouse.X == item.ExpanderX - HorizontalValue) Toggle(item);
                else ActivateItem(item);
            }

            return;
        }

        base.OnMouseUp(mouse);
    }

    /// <inheritdoc/>
    protected internal override void OnKeypress(KeyEvent key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var mods = key.Modifiers;
        bool scrollMod = mods is KeyModifiers.Shift or KeyModifiers.Ctrl or KeyModifiers.Alt;
        if (scrollMod)
        {
            switch (key.Key)
            {
                case Key.Left: HorizontalScroller?.Decrement(); return;
                case Key.Right: HorizontalScroller?.Increment(); return;
                case Key.Up: VerticalScroller?.Decrement(); return;
                case Key.Down: VerticalScroller?.Increment(); return;
                case Key.PageUp: VerticalScroller?.BigDecrement(); return;
                case Key.PageDown: VerticalScroller?.BigIncrement(); return;
            }
        }

        if (mods != KeyModifiers.None)
        {
            base.OnKeypress(key);
            return;
        }

        switch (key.Key)
        {
            case Key.PageDown:
                for (int i = 0; i < Height - 2; i++) MoveSelection(1);
                break;
            case Key.PageUp:
                for (int i = 0; i < Height - 2; i++) MoveSelection(-1);
                break;
            case Key.Home:
                if (rows.Count > 0) Select(rows[0], false);
                topLine = 0;
                ReflowData();
                break;
            case Key.End:
                if (rows.Count > 0) Select(rows[^1], true);
                break;
            case Key.Up:
                MoveSelection(-1);
                break;
            case Key.Down:
                MoveSelection(1);
                break;
            case Key.Left or Key.Right:
                if (selected != null) Toggle(selected);
                break;
            case Key.Character when key.Char.Value == ' ':
                if (selected != null) Toggle(selected);
                break;
            case Key.Enter:
                if (selected != null) ItemActivated?.Invoke(this, EventArgs.Empty);
                break;
            default:
                base.OnKeypress(key);
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (rows.Count == 0) return;
        int index = selected == null ? -1 : rows.IndexOf(selected);
        // Deviation: Jexer ignores Up/Down until something is selected; select the first row instead.
        int next = index < 0 ? 0 : index + delta;
        if (next < 0 || next >= rows.Count) return;
        Select(rows[next], true);
    }

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        bool active = IsAbsoluteActive;
        var color = GetColor(active ? "ttreeview" : "ttreeview.inactive");
        var expanderColor = GetColor("ttreeview.expandbutton");
        var selectedColor = GetColor(active ? "ttreeview.selected" : "ttreeview.selected.inactive");
        var unreadable = GetColor("ttreeview.unreadable");
        int columns = Width - 1;
        int offset = -HorizontalValue;
        for (int y = 0; y < ViewHeight; y++)
        {
            context.HLine(0, y, columns, ' ', color);
            int index = topLine + y;
            if (index >= rows.Count) continue;
            var item = rows[index];
            string line = item.Prefix;
            if (item.Level > 0)
            {
                line += item.Last ? GraphicsChars.CP437[0xC0] : GraphicsChars.CP437[0xC3];
                line += GraphicsChars.CP437[0xC4];
                line += item.IsExpandable ? "[ ] " : " ";
            }

            context.PutString(offset, y, line, color);
            var textColor = item == selected ? selectedColor : item.IsSelectable ? color : unreadable;
            context.PutString(offset + TextMetrics.Width(line), y, item.Text, textColor);
            if (item.Level > 0 && item.IsExpandable)
            {
                context.PutChar(offset + item.ExpanderX, y, item.IsExpanded ? '-' : '+', expanderColor);
            }
        }
    }
}
