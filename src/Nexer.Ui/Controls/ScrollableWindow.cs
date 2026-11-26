// Ported from Jexer TScrollableWindow.java (MIT, © Autumn Lamonte).
using Nexer.Ui.Data;

namespace Nexer.Ui.Controls;

/// <summary>
/// A window with optional scroll bars drawn on its right and bottom borders (Jexer <c>TScrollableWindow</c>).
/// Set <see cref="HasVerticalScroller"/>/<see cref="HasHorizontalScroller"/> (or assign the scroller properties
/// from a subclass) and override <see cref="ReflowData"/> to update the scroll ranges when the window resizes.
/// Non-sealed so it can be a XAML root.
/// </summary>
public class ScrollableWindow : Window
{
    private HScroller? horizontalScroller;
    private VScroller? verticalScroller;

    /// <summary>The horizontal scroller (on the bottom border), if any.</summary>
    public HScroller? HorizontalScroller
    {
        get => horizontalScroller;
        protected set => ReplaceScroller(ref horizontalScroller, value);
    }

    /// <summary>The vertical scroller (on the right border), if any.</summary>
    public VScroller? VerticalScroller
    {
        get => verticalScroller;
        protected set => ReplaceScroller(ref verticalScroller, value);
    }

    /// <summary>Whether the window has a vertical scroller. Setting true creates one.</summary>
    public bool HasVerticalScroller
    {
        get => verticalScroller != null;
        set
        {
            if (value == HasVerticalScroller) return;
            VerticalScroller = value ? new VScroller() : null;
            OnPropertyChanged();
        }
    }

    /// <summary>Whether the window has a horizontal scroller. Setting true creates one.</summary>
    public bool HasHorizontalScroller
    {
        get => horizontalScroller != null;
        set
        {
            if (value == HasHorizontalScroller) return;
            HorizontalScroller = value ? new HScroller() : null;
            OnPropertyChanged();
        }
    }

    private void ReplaceScroller<T>(ref T? field, T? value)
        where T : ScrollerBase
    {
        if (field == value) return;
        var old = field;
        if (old != null && old.Parent == this) Children.Remove(old);
        old?.ValueChanged -= OnScrollerValueChanged;
        field = value;
        if (value != null)
        {
            value.ValueChanged += OnScrollerValueChanged;
            if (value.Parent == null) Children.Add(value);
            PlaceScrollbars();
        }
    }

    /// <inheritdoc/>
    protected override void OnChildRemoved(Widget child)
    {
        if (child == horizontalScroller) horizontalScroller = null;
        if (child == verticalScroller) verticalScroller = null;
        if (child is ScrollerBase scroller) scroller.ValueChanged -= OnScrollerValueChanged;
        base.OnChildRemoved(child);
    }

    private void OnScrollerValueChanged(object? sender, EventArgs e) =>
        OnPropertyChanged(ReferenceEquals(sender, verticalScroller) ? nameof(VerticalValue) : nameof(HorizontalValue));

    /// <summary>Vertical scroll position (0 without a vertical scroller). Binds two-way by default.</summary>
    [BindsTwoWayByDefault]
    public int VerticalValue
    {
        get => verticalScroller?.Value ?? 0;
        set
        {
            if (verticalScroller != null) verticalScroller.Value = value;
        }
    }

    /// <summary>Horizontal scroll position (0 without a horizontal scroller). Binds two-way by default.</summary>
    [BindsTwoWayByDefault]
    public int HorizontalValue
    {
        get => horizontalScroller?.Value ?? 0;
        set
        {
            if (horizontalScroller != null) horizontalScroller.Value = value;
        }
    }

    /// <summary>Recomputes scroll ranges after a size or content change. The default does nothing.</summary>
    public virtual void ReflowData()
    {
    }

    /// <summary>
    /// Places the scrollers on the window border: the vertical one along the right edge, the horizontal one along
    /// the bottom edge starting at column 17 (or less on narrow windows), as Jexer does.
    /// </summary>
    protected virtual void PlaceScrollbars()
    {
        if (horizontalScroller is { } h)
        {
            int x = Math.Min(Math.Max(0, Width - 17), 17);
            h.SetDimensions(x, Height - 2, Math.Max(1, Width - x - 3), 1);
            h.BigChange = Width - x - 3;
        }

        if (verticalScroller is { } v)
        {
            v.SetDimensions(Width - 2, 0, 1, Math.Max(1, Height - 2));
            v.BigChange = Height - 2;
        }
    }

    /// <inheritdoc/>
    protected internal override void OnSizeChanged()
    {
        // Jexer does not run the layout manager for scrollable windows.
        ResizeFillingChild();
        ReflowData();
        PlaceScrollbars();
    }

    /// <summary>Scrolls to the top-left.</summary>
    public void ToHome()
    {
        horizontalScroller?.ToMinimum();
        verticalScroller?.ToMinimum();
    }

    /// <summary>Scrolls to the bottom-right.</summary>
    public void ToEnd()
    {
        horizontalScroller?.ToMaximum();
        verticalScroller?.ToMaximum();
    }

    /// <summary>True when the mouse is on the vertical scroller.</summary>
    protected bool MouseOnVerticalScroller(UiMouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        return verticalScroller is { } v && mouse.AbsoluteX == v.AbsoluteX &&
            mouse.AbsoluteY >= v.AbsoluteY && mouse.AbsoluteY < v.AbsoluteY + v.Height;
    }

    /// <summary>True when the mouse is on the horizontal scroller.</summary>
    protected bool MouseOnHorizontalScroller(UiMouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        return horizontalScroller is { } h && mouse.AbsoluteY == h.AbsoluteY &&
            mouse.AbsoluteX >= h.AbsoluteX && mouse.AbsoluteX < h.AbsoluteX + h.Width;
    }
}
