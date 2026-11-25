// Ported from Jexer TList.java (MIT, © Autumn Lamonte).
using System.Collections.ObjectModel;
using Nexer.Input;
using Nexer.Ui.Markup;

namespace Nexer.Ui.Controls;

/// <summary>The strings shown by a <see cref="List"/>. Every change reflows the list (and clears its selection).</summary>
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
        base.InsertItem(index, item ?? "");
        Notify();
    }

    /// <inheritdoc/>
    protected override void SetItem(int index, string item)
    {
        base.SetItem(index, item ?? "");
        Notify();
    }

    /// <inheritdoc/>
    protected override void RemoveItem(int index)
    {
        base.RemoveItem(index);
        Notify();
    }

    /// <inheritdoc/>
    protected override void ClearItems()
    {
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
/// <see cref="ItemClicked"/>. Scroll bars sit on the right and bottom edges.
/// </summary>
public class List : ScrollableWidget
{
    private int selected = -1;
    private int pendingSelected = -1;

    /// <summary>Creates an empty list.</summary>
    public List()
    {
        Items = new ListItemCollection(this);
        HorizontalScroller = new HScroller();
        VerticalScroller = new VScroller();
        Children.Add(HorizontalScroller);
        Children.Add(VerticalScroller);
    }

    /// <summary>The items (content property). In markup: <c>&lt;List&gt;&lt;x:String&gt;One&lt;/x:String&gt;&lt;/List&gt;</c>.</summary>
    [Content]
    public ListItemCollection Items { get; }

    /// <summary>
    /// Index of the selected item, or -1. Setting scrolls it into view (Jexer <c>setSelectedIndex</c>); a value set
    /// while the list is empty is applied when items are added.
    /// </summary>
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

    /// <summary>The selected item, or null.</summary>
    public string? SelectedItem => selected >= 0 && selected < Items.Count ? Items[selected] : null;

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
        if (pendingSelected >= 0 && pendingSelected < Items.Count)
        {
            int index = pendingSelected;
            pendingSelected = -1;
            SelectedIndex = index;
        }

        ItemsChanged?.Invoke(this, EventArgs.Empty);
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
        var normal = GetColor(active ? "tlist" : "tlist.inactive");
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
