// Ported from Jexer TTreeItem.java, TTreeView.java and TTreeViewScrollable.java (MIT, © Autumn Lamonte).
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Nexer.Input;
using Nexer.Ui.Data;
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
    public bool IsSelected => TreeView?.SelectedTreeItem == this;

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
/// A tree item generated for an <see cref="TreeView.ItemsSource"/> object. Its children come from
/// <see cref="TreeView.ChildrenBinding"/>, are created when it is first expanded, and follow an
/// <see cref="INotifyCollectionChanged"/> collection while the tree view is attached.
/// </summary>
internal sealed class SourceTreeItem : TreeItem
{
    private readonly TreeView view;
    private readonly bool isRoot;
    private IEnumerable? children;
    private INotifyCollectionChanged? observed;
    private bool loaded;

    internal SourceTreeItem(TreeView view, object? source, bool isRoot)
    {
        this.view = view;
        this.isRoot = isRoot;
        Source = source;
        if (isRoot)
        {
            children = source as IEnumerable;
            Load();
            return;
        }

        Text = view.ItemText(source);
        children = view.ItemChildren(source);
        IsExpanded = false;
        IsExpandable = children is ICollection collection ? collection.Count > 0 : children != null;
    }

    internal object? Source { get; }

    protected internal override void OnExpand()
    {
        if (IsExpanded && !loaded) Load();
    }

    private void Load()
    {
        loaded = true;
        Sync();
        if (view.IsAttachedToTree) Observe();
    }

    /// <summary>Re-reads the children of this item and of its loaded descendants, reusing items by source identity.</summary>
    internal void SyncTree()
    {
        if (!loaded) return;
        Sync();
        foreach (var item in Items.OfType<SourceTreeItem>()) item.SyncTree();
    }

    private void Sync()
    {
        var existing = Items.OfType<SourceTreeItem>().ToList();
        var next = new List<TreeItem>();
        if (children != null)
        {
            foreach (var source in children)
            {
                int index = existing.FindIndex(i => ReferenceEquals(i.Source, source));
                if (index >= 0)
                {
                    next.Add(existing[index]);
                    existing.RemoveAt(index);
                }
                else
                {
                    next.Add(new SourceTreeItem(view, source, false));
                }
            }
        }

        foreach (var removed in existing) removed.Unobserve();
        if (!isRoot) IsExpandable = next.Count > 0;
        Items.ReplaceAll(next);
    }

    internal void Observe()
    {
        if (!loaded) return;
        if (observed == null && children is INotifyCollectionChanged incc)
        {
            observed = incc;
            incc.CollectionChanged += OnChildrenChanged;
        }

        foreach (var item in Items.OfType<SourceTreeItem>()) item.Observe();
    }

    internal void Unobserve()
    {
        if (observed != null)
        {
            observed.CollectionChanged -= OnChildrenChanged;
            observed = null;
        }

        foreach (var item in Items.OfType<SourceTreeItem>()) item.Unobserve();
    }

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (view.Application is { } app && !app.CheckAccess())
        {
            app.Post(() =>
            {
                if (observed != null) Sync();
            });
            return;
        }

        Sync();
        if (view.IsAttachedToTree) Observe();
    }
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
    private TreeItem? notifiedSelected;
    private IEnumerable? itemsSource;
    private IBinding? displayMemberBinding;
    private IBinding? childrenBinding;
    private SourceTreeItem? sourceRoot;

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

    /// <summary>
    /// The root item (content property). Mutually exclusive with <see cref="ItemsSource"/> (setting it while
    /// <see cref="ItemsSource"/> is in use throws <see cref="InvalidOperationException"/>).
    /// </summary>
    [Content]
    public TreeItem? Root
    {
        get => sourceRoot != null ? null : root;
        set
        {
            if (sourceRoot != null) throw new InvalidOperationException("Root cannot be set while ItemsSource is in use.");
            SetRoot(value);
        }
    }

    /// <summary>
    /// The top-level items as any objects (shown without a root row); null uses <see cref="Root"/>. Each item is
    /// shown as the result of <see cref="DisplayMemberBinding"/> (or <c>ToString()</c>) and its children are the
    /// result of <see cref="ChildrenBinding"/>, read when the item is first expanded. <see cref="INotifyCollectionChanged"/>
    /// collections are observed while the view is attached to the tree. Setting it while <see cref="Root"/> is set
    /// throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public IEnumerable? ItemsSource
    {
        get => itemsSource;
        set
        {
            if (ReferenceEquals(itemsSource, value)) return;
            if (value != null && sourceRoot == null && root != null)
            {
                throw new InvalidOperationException("Root is in use; clear it before setting ItemsSource.");
            }

            itemsSource = value;
            ResetSource();
            OnPropertyChanged();
        }
    }

    /// <summary>The binding evaluated against each <see cref="ItemsSource"/> item to get its text; null uses <c>ToString()</c>.</summary>
    public IBinding? DisplayMemberBinding
    {
        get => displayMemberBinding;
        set
        {
            if (SetProperty(ref displayMemberBinding, value) && itemsSource != null) ResetSource();
        }
    }

    /// <summary>
    /// The binding evaluated against each <see cref="ItemsSource"/> item to get its children (an
    /// <see cref="IEnumerable"/>); null makes every item a leaf.
    /// </summary>
    public IBinding? ChildrenBinding
    {
        get => childrenBinding;
        set
        {
            if (SetProperty(ref childrenBinding, value) && itemsSource != null) ResetSource();
        }
    }

    /// <summary>
    /// The selected item: the source object when <see cref="ItemsSource"/> is used, else the <see cref="TreeItem"/>.
    /// Setting it selects the matching (already created) item and scrolls it to the middle of the view. Binds two-way by
    /// default.
    /// </summary>
    [BindsTwoWayByDefault]
    public object? SelectedItem
    {
        get => selected is SourceTreeItem s ? s.Source : selected;
        set => Select(sourceRoot != null ? FindSourceItem(sourceRoot, value) : value as TreeItem, true);
    }

    /// <summary>The selected <see cref="TreeItem"/> (generated for a source object when <see cref="ItemsSource"/> is used).</summary>
    public TreeItem? SelectedTreeItem
    {
        get => selected;
        set => Select(value, true);
    }

    internal string ItemText(object? item)
    {
        if (displayMemberBinding != null)
        {
            return BindingOperations.Evaluate(displayMemberBinding, item, typeof(string)) as string ?? "";
        }

        return item?.ToString() ?? "";
    }

    internal IEnumerable? ItemChildren(object? item) =>
        childrenBinding == null ? null : BindingOperations.Evaluate(childrenBinding, item, typeof(IEnumerable)) as IEnumerable;

    private void SetRoot(TreeItem? value)
    {
        if (root == value) return;
        if (value?.Parent != null) throw new InvalidOperationException("The root item must not have a parent.");
        if (root != null) root.TreeViewOwner = null;
        root = value;
        if (value != null) value.TreeViewOwner = this;
        selected = null;
        topLine = 0;
        ReflowData();
        OnPropertyChanged(nameof(Root));
    }

    private void ResetSource()
    {
        var previous = SelectedItem;
        sourceRoot?.Unobserve();
        sourceRoot = null;
        if (itemsSource == null)
        {
            SetRoot(null);
            return;
        }

        var newRoot = new SourceTreeItem(this, itemsSource, true);
        sourceRoot = newRoot;
        SetRoot(newRoot);
        if (IsAttachedToTree) newRoot.Observe();
        if (previous != null && FindSourceItem(newRoot, previous) is { } item) Select(item, false);
    }

    private static SourceTreeItem? FindSourceItem(TreeItem parent, object? source)
    {
        if (source == null) return null;
        foreach (var item in parent.Items.OfType<SourceTreeItem>())
        {
            if (ReferenceEquals(item.Source, source) || Equals(item.Source, source)) return item;
            if (FindSourceItem(item, source) is { } found) return found;
        }

        return null;
    }

    /// <inheritdoc/>
    protected override void OnAttachedToTree()
    {
        if (sourceRoot != null)
        {
            // Changes made while detached were not observed.
            sourceRoot.SyncTree();
            sourceRoot.Observe();
        }

        base.OnAttachedToTree();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromTree()
    {
        sourceRoot?.Unobserve();
        base.OnDetachedFromTree();
    }

    private void NotifySelection()
    {
        if (notifiedSelected == selected) return;
        notifiedSelected = selected;
        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(SelectedTreeItem));
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

        // An ItemsSource has no root row: its items are the top level.
        if (root != null && root == sourceRoot && rows.Count > 0) rows.RemoveAt(0);
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
        NotifySelection();
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
