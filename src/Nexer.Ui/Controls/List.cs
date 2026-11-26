// Ported from Jexer TList.java (MIT, © Autumn Lamonte).
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Nexer.Input;
using Nexer.Ui.Data;
using Nexer.Ui.Markup;

namespace Nexer.Ui.Controls;

/// <summary>
/// The strings shown by a <see cref="List"/>. Every change reflows the list (and clears its selection). While the list
/// uses <see cref="List.ItemsSource"/> the collection is read-only (changes throw <see cref="InvalidOperationException"/>).
/// </summary>
public sealed class ListItemCollection : Collection<string>
{
    private readonly List owner;
    private bool suppress;

    internal ListItemCollection(List owner) => this.owner = owner;

    /// <summary>Replaces all items with one notification.</summary>
    public void ReplaceAll(IEnumerable<string> items)
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

        owner.OnItemsChanged();
    }

    /// <summary>Replaces an item without reflowing (used when re-rendering items).</summary>
    internal void SetSilently(int index, string item)
    {
        owner.EnsureItemsWritable();
        suppress = true;
        try
        {
            this[index] = item;
        }
        finally
        {
            suppress = false;
        }
    }

    /// <inheritdoc/>
    protected override void InsertItem(int index, string item)
    {
        owner.EnsureItemsWritable();
        base.InsertItem(index, item ?? "");
        Notify();
    }

    /// <inheritdoc/>
    protected override void SetItem(int index, string item)
    {
        owner.EnsureItemsWritable();
        base.SetItem(index, item ?? "");
        Notify();
    }

    /// <inheritdoc/>
    protected override void RemoveItem(int index)
    {
        owner.EnsureItemsWritable();
        base.RemoveItem(index);
        Notify();
    }

    /// <inheritdoc/>
    protected override void ClearItems()
    {
        owner.EnsureItemsWritable();
        base.ClearItems();
        Notify();
    }

    private void Notify()
    {
        if (!suppress) owner.OnItemsChanged();
    }
}

/// <summary>
/// A scrollable list of strings with a selection (Jexer <c>TList</c>). Arrow keys, PgUp/PgDn and Home/End move the
/// selection; Enter or a double click raises <see cref="ItemActivated"/>; a single click selects and raises
/// <see cref="ItemClicked"/>. Scroll bars sit on the right and bottom edges. Either fill <see cref="Items"/> or set
/// <see cref="ItemsSource"/> (any objects, shown via <see cref="DisplayMemberBinding"/> or <c>ToString()</c>).
/// </summary>
public class List : ScrollableWidget
{
    private int selected = -1;
    private int pendingSelected = -1;
    private object? pendingSelectedItem;
    private int notifiedIndex = -1;
    private object? notifiedItem;
    private IEnumerable? itemsSource;
    private readonly List<object?> sourceItems = [];
    private IBinding? displayMemberBinding;
    private INotifyCollectionChanged? observedSource;
    private bool syncingSource;
    private bool restoringSelection;
    private object? selectionToRestore;
    private readonly HashSet<INotifyPropertyChanged> watchedItems = new(ReferenceEqualityComparer.Instance);

    /// <summary>Creates an empty list.</summary>
    public List()
    {
        Items = new ListItemCollection(this);
        HorizontalScroller = new HScroller();
        VerticalScroller = new VScroller();
        Children.Add(HorizontalScroller);
        Children.Add(VerticalScroller);
    }

    /// <summary>
    /// The items (content property). In markup: <c>&lt;List&gt;&lt;x:String&gt;One&lt;/x:String&gt;&lt;/List&gt;</c>.
    /// While <see cref="ItemsSource"/> is set this holds the display text of the source items and is read-only.
    /// </summary>
    [Content]
    public ListItemCollection Items { get; }

    /// <summary>
    /// The source of the items (any objects); null uses <see cref="Items"/>. Each item is shown as the result of
    /// <see cref="DisplayMemberBinding"/> (or its <c>ToString()</c>). An <see cref="INotifyCollectionChanged"/> source is
    /// observed while the list is attached to the tree (incremental updates keep the selected item). Setting it while
    /// <see cref="Items"/> has items throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public IEnumerable? ItemsSource
    {
        get => itemsSource;
        set
        {
            if (ReferenceEquals(itemsSource, value)) return;
            if (value != null && itemsSource == null && Items.Count > 0)
            {
                throw new InvalidOperationException("Items is in use; clear it before setting ItemsSource.");
            }

            Unobserve();
            itemsSource = value;
            if (IsAttachedToTree) Observe();
            ResetFromSource();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The binding (usually <c>{Binding Name, DataType=local:Person}</c>) evaluated against each
    /// <see cref="ItemsSource"/> item to get its display text; null uses <c>ToString()</c>.
    /// </summary>
    public IBinding? DisplayMemberBinding
    {
        get => displayMemberBinding;
        set
        {
            if (!SetProperty(ref displayMemberBinding, value)) return;
            if (itemsSource != null) ResetFromSource();
        }
    }

    internal void EnsureItemsWritable()
    {
        if (itemsSource != null && !syncingSource)
        {
            throw new InvalidOperationException("Items cannot be changed while ItemsSource is in use.");
        }
    }

    internal string ItemText(object? item)
    {
        if (displayMemberBinding != null)
        {
            return BindingOperations.Evaluate(displayMemberBinding, item, typeof(string)) as string ?? "";
        }

        return item?.ToString() ?? "";
    }

    /// <inheritdoc/>
    protected override void OnAttachedToTree()
    {
        if (itemsSource != null)
        {
            Observe();
            ResetFromSource();
        }

        base.OnAttachedToTree();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromTree()
    {
        Unobserve();
        WatchItems();
        base.OnDetachedFromTree();
    }

    /// <summary>
    /// While attached, listens to <see cref="INotifyPropertyChanged"/> on the source items so a change of the property
    /// <see cref="DisplayMemberBinding"/> reads from an item refreshes that item's text.
    /// </summary>
    private void WatchItems()
    {
        var next = IsAttachedToTree && DisplayMember.Watches(displayMemberBinding)
            ? sourceItems.OfType<INotifyPropertyChanged>().ToHashSet<INotifyPropertyChanged>(ReferenceEqualityComparer.Instance)
            : null;
        foreach (var item in watchedItems)
        {
            if (next == null || !next.Contains(item)) item.PropertyChanged -= OnItemPropertyChanged;
        }

        if (next != null)
        {
            foreach (var item in next)
            {
                if (!watchedItems.Contains(item)) item.PropertyChanged += OnItemPropertyChanged;
            }
        }

        watchedItems.Clear();
        if (next != null) watchedItems.UnionWith(next);
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!DisplayMember.Affects(displayMemberBinding, e.PropertyName)) return;
        if (Application is { } app && !app.CheckAccess())
        {
            app.Post(() => RefreshItemText(sender));
            return;
        }

        RefreshItemText(sender);
    }

    private void RefreshItemText(object? item)
    {
        if (item is not INotifyPropertyChanged watched || !watchedItems.Contains(watched)) return;
        UpdateFromSource(() =>
        {
            for (int i = 0; i < sourceItems.Count; i++)
            {
                if (!ReferenceEquals(sourceItems[i], item)) continue;
                string text = ItemText(item);
                if (Items[i] != text) Items[i] = text;
            }
        });
    }

    private void Observe()
    {
        if (observedSource != null || itemsSource is not INotifyCollectionChanged incc) return;
        observedSource = incc;
        incc.CollectionChanged += OnSourceCollectionChanged;
    }

    private void Unobserve()
    {
        if (observedSource == null) return;
        observedSource.CollectionChanged -= OnSourceCollectionChanged;
        observedSource = null;
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, itemsSource)) return;
        if (Application is { } app && !app.CheckAccess())
        {
            // The indices are stale by the time the UI thread runs: resynchronise.
            app.Post(() =>
            {
                if (ReferenceEquals(sender, itemsSource)) ResetFromSource();
            });
            return;
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems != null && e.NewStartingIndex >= 0 &&
                e.NewStartingIndex <= sourceItems.Count:
                UpdateFromSource(() =>
                {
                    for (int i = 0; i < e.NewItems.Count; i++)
                    {
                        sourceItems.Insert(e.NewStartingIndex + i, e.NewItems[i]);
                        Items.Insert(e.NewStartingIndex + i, ItemText(e.NewItems[i]));
                    }
                });
                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems != null && e.OldStartingIndex >= 0 &&
                e.OldStartingIndex + e.OldItems.Count <= sourceItems.Count:
                UpdateFromSource(() =>
                {
                    for (int i = 0; i < e.OldItems.Count; i++)
                    {
                        sourceItems.RemoveAt(e.OldStartingIndex);
                        Items.RemoveAt(e.OldStartingIndex);
                    }
                });
                break;
            case NotifyCollectionChangedAction.Replace when e.NewItems != null && e.OldItems != null &&
                e.NewItems.Count == e.OldItems.Count && e.NewStartingIndex >= 0 &&
                e.NewStartingIndex + e.NewItems.Count <= sourceItems.Count:
                UpdateFromSource(() =>
                {
                    for (int i = 0; i < e.NewItems.Count; i++)
                    {
                        sourceItems[e.NewStartingIndex + i] = e.NewItems[i];
                        Items[e.NewStartingIndex + i] = ItemText(e.NewItems[i]);
                    }
                });
                break;
            case NotifyCollectionChangedAction.Move when e.OldItems is { Count: 1 } && e.OldStartingIndex >= 0 &&
                e.OldStartingIndex < sourceItems.Count && e.NewStartingIndex >= 0 &&
                e.NewStartingIndex < sourceItems.Count:
                UpdateFromSource(() =>
                {
                    var item = sourceItems[e.OldStartingIndex];
                    string text = Items[e.OldStartingIndex];
                    sourceItems.RemoveAt(e.OldStartingIndex);
                    Items.RemoveAt(e.OldStartingIndex);
                    sourceItems.Insert(e.NewStartingIndex, item);
                    Items.Insert(e.NewStartingIndex, text);
                });
                break;
            default:
                ResetFromSource();
                break;
        }
    }

    /// <summary>Re-reads <see cref="ItemsSource"/> (keeping the selected item when it is still there).</summary>
    private void ResetFromSource() => UpdateFromSource(() =>
    {
        sourceItems.Clear();
        if (itemsSource != null)
        {
            foreach (var item in itemsSource) sourceItems.Add(item);
        }

        Items.ReplaceAll(sourceItems.Select(ItemText).ToList());
    });

    private void UpdateFromSource(Action update)
    {
        bool wasSyncing = syncingSource, wasRestoring = restoringSelection;
        var restore = selectionToRestore;
        syncingSource = true;
        restoringSelection = itemsSource != null && selected >= 0;
        selectionToRestore = restoringSelection ? SelectedItem : null;
        try
        {
            update();
        }
        finally
        {
            syncingSource = wasSyncing;
            restoringSelection = wasRestoring;
            selectionToRestore = restore;
        }

        WatchItems();
        NotifySelection();
    }

    private int IndexOfItem(object? item)
    {
        if (item == null) return -1;
        if (itemsSource != null)
        {
            for (int i = 0; i < sourceItems.Count; i++)
            {
                if (ReferenceEquals(sourceItems[i], item)) return i;
            }

            for (int i = 0; i < sourceItems.Count; i++)
            {
                if (Equals(sourceItems[i], item)) return i;
            }

            return -1;
        }

        return item is string s ? Items.IndexOf(s) : -1;
    }

    private void NotifySelection()
    {
        if (notifiedIndex != selected)
        {
            notifiedIndex = selected;
            OnPropertyChanged(nameof(SelectedIndex));
        }

        var item = SelectedItem;
        if (!ReferenceEquals(notifiedItem, item) && !Equals(notifiedItem, item))
        {
            notifiedItem = item;
            OnPropertyChanged(nameof(SelectedItem));
        }
    }

    /// <summary>
    /// Index of the selected item, or -1. Setting scrolls it into view (Jexer <c>setSelectedIndex</c>); a value set
    /// while the list is empty is applied when items are added. Binds two-way by default.
    /// </summary>
    [BindsTwoWayByDefault]
    public int SelectedIndex
    {
        get => selected;
        set
        {
            if (Items.Count == 0 && value >= 0)
            {
                pendingSelected = value;
                return;
            }

            int old = selected;
            SelectIndexCore(value);
            if (old != selected) SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// The selected item (the source item when <see cref="ItemsSource"/> is used, else the string), or null. Setting it
    /// selects the first equal item (none when absent); a value set while the list is empty is applied when items are
    /// added. Binds two-way by default.
    /// </summary>
    [BindsTwoWayByDefault]
    public object? SelectedItem
    {
        get
        {
            if (selected < 0 || selected >= Items.Count) return null;
            return itemsSource != null && selected < sourceItems.Count ? sourceItems[selected] : Items[selected];
        }

        set
        {
            pendingSelectedItem = null;
            int index = IndexOfItem(value);
            if (index < 0 && value != null && Items.Count == 0)
            {
                pendingSelectedItem = value;
                return;
            }

            SelectedIndex = index;
        }
    }

    /// <summary>The display text of the selected item, or null.</summary>
    public string? SelectedText => selected >= 0 && selected < Items.Count ? Items[selected] : null;

    /// <summary>Raised on Enter or double click on an item (Jexer enter action).</summary>
    public event EventHandler? ItemActivated;

    /// <summary>Raised when the selection moves (keyboard, click, or <see cref="SelectedIndex"/>).</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised when an item is clicked once (Jexer single-click action).</summary>
    public event EventHandler? ItemClicked;

    /// <summary>Raised after <see cref="Items"/> changes.</summary>
    internal event EventHandler? ItemsChanged;

    /// <inheritdoc/>
    protected internal override bool IsKeyboardLeaf => true;

    internal void OnItemsChanged()
    {
        ReflowData();
        if (restoringSelection)
        {
            int index = IndexOfItem(selectionToRestore);
            if (index >= 0) SelectIndexCore(index);
        }

        if (pendingSelected >= 0 && pendingSelected < Items.Count)
        {
            int index = pendingSelected;
            pendingSelected = -1;
            SelectedIndex = index;
        }

        if (pendingSelectedItem != null && Items.Count > 0)
        {
            var item = pendingSelectedItem;
            pendingSelectedItem = null;
            int index = IndexOfItem(item);
            if (index >= 0) SelectedIndex = index;
        }

        ItemsChanged?.Invoke(this, EventArgs.Empty);
        NotifySelection();
        Invalidate();
    }

    private void SelectIndexCore(int index)
    {
        if (Items.Count == 0 || index < 0)
        {
            VerticalScroller?.ToMinimum();
            selected = -1;
        }
        else if (index > Items.Count - 1)
        {
            VerticalScroller?.ToMaximum();
            selected = Items.Count - 1;
        }
        else
        {
            VerticalScroller?.ToMinimum();
            selected = 0;
            while (index > selected)
            {
                selected++;
                while (selected - VerticalValue >= Height - 1 && VerticalValue < (VerticalScroller?.Maximum ?? 0))
                {
                    VerticalScroller?.Increment();
                }
            }
        }

        Invalidate();
        NotifySelection();
    }

    /// <inheritdoc/>
    public override void ReflowData()
    {
        selected = -1;
        int maxLineWidth = 0;
        foreach (var line in Items) maxLineWidth = Math.Max(maxLineWidth, TextMetrics.Width(line));
        if (VerticalScroller is { } v)
        {
            v.Maximum = Math.Max(0, Items.Count - Height + 1);
            if (v.Value > v.Maximum) v.Value = v.Maximum;
        }

        if (HorizontalScroller is { } h)
        {
            h.Maximum = Math.Max(0, maxLineWidth - Width + 1);
            if (h.Value > h.Maximum) h.Value = h.Maximum;
        }
    }

    /// <inheritdoc/>
    protected internal override void OnSizeChanged()
    {
        int index = selected;
        base.OnSizeChanged();
        SelectIndexCore(index);
    }

    private void Move()
    {
        Invalidate();
        NotifySelection();
        if (selected >= 0) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    protected internal override void OnMouseDown(UiMouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        if (mouse.IsWheelUp)
        {
            VerticalScroller?.Decrement();
            return;
        }

        if (mouse.IsWheelDown)
        {
            VerticalScroller?.Increment();
            return;
        }

        if (mouse.X < Width - 1 && mouse.Y < Height - 1)
        {
            int index = VerticalValue + mouse.Y;
            if (index < Items.Count)
            {
                bool changed = index != selected;
                selected = index;
                Invalidate();
                NotifySelection();
                if (changed) SelectionChanged?.Invoke(this, EventArgs.Empty);
                ItemClicked?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        base.OnMouseDown(mouse);
    }

    /// <inheritdoc/>
    protected internal override void OnMouseDoubleClick(UiMouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        if (mouse.X < Width - 1 && mouse.Y < Height - 1)
        {
            int index = VerticalValue + mouse.Y;
            if (index < Items.Count)
            {
                selected = index;
                Invalidate();
                NotifySelection();
                ItemActivated?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        base.OnMouseDoubleClick(mouse);
    }

    /// <inheritdoc/>
    protected internal override void OnKeypress(KeyEvent key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Modifiers != KeyModifiers.None && key.Key != Key.BackTab &&
            !(key.Key == Key.Tab && key.Modifiers == KeyModifiers.Shift))
        {
            base.OnKeypress(key);
            return;
        }

        int count = Items.Count;
        switch (key.Key)
        {
            case Key.Left:
                HorizontalScroller?.Decrement();
                break;
            case Key.Right:
                HorizontalScroller?.Increment();
                break;
            case Key.Up:
                if (count > 0)
                {
                    if (selected >= 0)
                    {
                        if (selected > 0)
                        {
                            if (selected - VerticalValue == 0) VerticalScroller?.Decrement();
                            selected--;
                        }
                    }
                    else
                    {
                        selected = count - 1;
                    }
                }

                Move();
                break;
            case Key.Down:
                if (count > 0)
                {
                    if (selected >= 0)
                    {
                        if (selected < count - 1)
                        {
                            selected++;
                            if (selected - VerticalValue == Height - 1) VerticalScroller?.Increment();
                        }
                    }
                    else
                    {
                        selected = 0;
                    }
                }

                Move();
                break;
            case Key.PageUp:
                VerticalScroller?.BigDecrement();
                if (selected >= 0) selected = Math.Max(0, selected - (Height - 1));
                Move();
                break;
            case Key.PageDown:
                VerticalScroller?.BigIncrement();
                if (selected >= 0) selected = Math.Min(count - 1, selected + (Height - 1));
                Move();
                break;
            case Key.Home:
                VerticalScroller?.ToMinimum();
                if (count > 0) selected = 0;
                Move();
                break;
            case Key.End:
                VerticalScroller?.ToMaximum();
                if (count > 0) selected = count - 1;
                Move();
                break;
            case Key.Enter:
                if (selected >= 0) ItemActivated?.Invoke(this, EventArgs.Empty);
                break;
            default:
                base.OnKeypress(key);
                break;
        }
    }

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        bool active = IsAbsoluteActive;
        var normal = GetColor(Validation.GetHasErrors(this) ? "tlist.error" : active ? "tlist" : "tlist.inactive");
        var highlight = GetColor(active ? "tlist.selected" : "tlist.selected.inactive");
        int columns = Width - 1;
        int topY = 0;
        for (int i = VerticalValue; i < Items.Count && topY < Height - 1; i++, topY++)
        {
            var color = i == selected ? highlight : normal;
            context.HLine(0, topY, columns, ' ', color);
            context.PutString(0, topY, TextMetrics.Truncate(TextMetrics.SkipColumns(Items[i], HorizontalValue), columns), color);
        }

        for (; topY < Height - 1; topY++) context.HLine(0, topY, columns, ' ', normal);
    }
}
