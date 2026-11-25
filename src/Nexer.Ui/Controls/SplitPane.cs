// Ported from Jexer TSplitPane.java (MIT, © Autumn Lamonte).
namespace Nexer.Ui.Controls;

/// <summary>
/// Two panes separated by a draggable divider (Jexer <c>TSplitPane</c>). A <see cref="Vertical"/> split (the default)
/// has a vertical divider with <see cref="Left"/>/<see cref="Right"/> panes; a horizontal split has a horizontal
/// divider with <see cref="Top"/>/<see cref="Bottom"/> panes. <see cref="Left"/> and <see cref="Top"/> name the
/// first pane, <see cref="Right"/> and <see cref="Bottom"/> the second. In markup the first two children become the
/// panes. The divider sits at column/row <see cref="Split"/>; until it is set, the split is centred. Drag the divider
/// with the left mouse button. A split pane that is the only child of a window fills the window.
/// </summary>
public class SplitPane : Widget, IFillsWindow
{
    private bool vertical = true;
    private int split;
    private bool splitSet;
    private Widget? first;
    private Widget? second;
    private bool inSplitMove;
    private (int X, int Y)? mouse;

    /// <summary>True (default) for a vertical divider (left/right panes); false for a horizontal one.</summary>
    public bool Vertical
    {
        get => vertical;
        set
        {
            if (vertical == value) return;
            vertical = value;
            if (splitSet) LayoutPanes();
            else Center();
        }
    }

    /// <summary>The divider column (vertical) or row (horizontal), relative to the split pane.</summary>
    public int Split
    {
        get => split;
        set
        {
            split = value;
            splitSet = true;
            LayoutPanes();
            Invalidate();
        }
    }

    /// <summary>When true, moving the mouse over a pane activates it.</summary>
    public bool FocusFollowsMouse { get; set; }

    /// <summary>The first pane (left of a vertical split).</summary>
    public Widget? Left
    {
        get => first;
        set => SetPane(ref first, value, 0);
    }

    /// <summary>The second pane (right of a vertical split).</summary>
    public Widget? Right
    {
        get => second;
        set => SetPane(ref second, value, -1);
    }

    /// <summary>The first pane (top of a horizontal split); same slot as <see cref="Left"/>.</summary>
    public Widget? Top
    {
        get => first;
        set => SetPane(ref first, value, 0);
    }

    /// <summary>The second pane (bottom of a horizontal split); same slot as <see cref="Right"/>.</summary>
    public Widget? Bottom
    {
        get => second;
        set => SetPane(ref second, value, -1);
    }

    private void SetPane(ref Widget? slot, Widget? value, int index)
    {
        if (slot == value) return;
        var old = slot;
        slot = value;
        if (old != null && old.Parent == this) Children.Remove(old);
        if (value != null && value.Parent != this)
        {
            Children.Insert(index < 0 ? Children.Count : index, value);
        }

        LayoutPanes();
    }

    /// <summary>Centres the divider (Jexer <c>center</c>).</summary>
    public void Center()
    {
        split = vertical ? Width / 2 : Height / 2;
        LayoutPanes();
    }

    /// <inheritdoc/>
    protected override void OnChildAdded(Widget child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child != first && child != second)
        {
            if (first == null) first = child;
            else if (second == null) second = child;
            else
            {
                Children.Remove(child);
                throw new InvalidOperationException("A SplitPane holds at most two widgets.");
            }
        }

        LayoutPanes();
        base.OnChildAdded(child);
    }

    /// <inheritdoc/>
    protected override void OnChildRemoved(Widget child)
    {
        if (child == first) first = null;
        if (child == second) second = null;
        base.OnChildRemoved(child);
    }

    /// <inheritdoc/>
    protected internal override void OnSizeChanged()
    {
        if (!splitSet || split >= (vertical ? Width : Height) - 2) Center();
        else LayoutPanes();
    }

    private void LayoutPanes()
    {
        if (vertical)
        {
            first?.SetDimensions(0, 0, Math.Max(0, split), Height);
            second?.SetDimensions(split + 1, 0, Math.Max(0, Width - split - 1), Height);
        }
        else
        {
            first?.SetDimensions(0, 0, Width, Math.Max(0, split));
            second?.SetDimensions(0, split + 1, Width, Math.Max(0, Height - split - 1));
        }
    }

    private bool OnDivider(UiMouseEvent m) => vertical ? m.X == split : m.Y == split;

    /// <inheritdoc/>
    protected internal override void OnMouseDown(UiMouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        this.mouse = (mouse.X, mouse.Y);
        inSplitMove = mouse.IsLeft && !mouse.IsWheelUp && !mouse.IsWheelDown && OnDivider(mouse);
        if (inSplitMove) return;
        base.OnMouseDown(mouse);
    }

    /// <inheritdoc/>
    protected internal override void OnMouseUp(UiMouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        this.mouse = (mouse.X, mouse.Y);
        if (inSplitMove && mouse.IsLeft)
        {
            inSplitMove = false;
            return;
        }

        base.OnMouseUp(mouse);
    }

    /// <inheritdoc/>
    protected internal override void OnMouseMotion(UiMouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        var previous = this.mouse;
        this.mouse = (mouse.X, mouse.Y);
        bool inside = mouse.X >= 0 && mouse.X < Width && mouse.Y >= 0 && mouse.Y < Height;
        if (!inside) inSplitMove = false;
        if (previous != this.mouse && (inside || (previous is { } p && OnDividerAt(p.X, p.Y)))) Invalidate();

        if (FocusFollowsMouse)
        {
            foreach (var pane in new[] { first, second })
            {
                if (pane != null && pane.MouseWouldHit(mouse))
                {
                    Activate(pane);
                    break;
                }
            }
        }

        if (inSplitMove)
        {
            split = vertical
                ? Math.Min(Math.Max(1, mouse.X), Width - 2)
                : Math.Min(Math.Max(1, mouse.Y), Height - 2);
            splitSet = true;
            LayoutPanes();
            return;
        }

        base.OnMouseMotion(mouse);
    }

    private bool OnDividerAt(int x, int y) => vertical ? x == split : y == split;

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var attr = GetColor("tsplitpane");
        if (vertical)
        {
            context.VLine(split, 0, Height, GraphicsChars.WindowSide, attr);
            var l = first as SplitPane;
            var r = second as SplitPane;
            if (l is { vertical: false } && r is { vertical: false } && l.split == r.split)
            {
                context.PutChar(split, l.split, '\u253C', attr);
            }
            else
            {
                if (l is { vertical: false }) context.PutChar(split, l.split, '\u2524', attr);
                if (r is { vertical: false }) context.PutChar(split, r.split, '\u251C', attr);
            }

            if (mouse is { } m && m.X == split && m.Y >= 0 && m.Y < Height)
            {
                context.PutChar(split, m.Y, '\u2194', attr);
            }
        }
        else
        {
            context.HLine(0, split, Width, GraphicsChars.SingleBar, attr);
            var t = first as SplitPane;
            var b = second as SplitPane;
            if (t is { vertical: true } && b is { vertical: true } && t.split == b.split)
            {
                context.PutChar(t.split, split, '\u253C', attr);
            }
            else
            {
                if (t is { vertical: true }) context.PutChar(t.split, split, '\u2534', attr);
                if (b is { vertical: true }) context.PutChar(b.split, split, '\u252C', attr);
            }

            if (mouse is { } m && m.Y == split && m.X >= 0 && m.X < Width)
            {
                context.PutChar(m.X, split, '\u2195', attr);
            }
        }
    }
}
