// Ported from Jexer THScroller.java, TVScroller.java and TScrollable.java (MIT, © Autumn Lamonte).
using Nexer.Input;
using Nexer.Ui.Data;

namespace Nexer.Ui.Controls;

/// <summary>
/// Shared logic of <see cref="HScroller"/> and <see cref="VScroller"/>: a value between <see cref="Minimum"/> and
/// <see cref="Maximum"/> with arrow, page and thumb-drag interaction. Scrollers are never focused or tab stops.
/// </summary>
public abstract class ScrollerBase : Widget
{
    private int value;
    private bool inScroll;

    /// <summary>Initialises a scroller.</summary>
    protected ScrollerBase()
    {
        TabStop = false;
    }

    /// <summary>Smallest value (Jexer top/left value).</summary>
    public int Minimum
    {
        get => minimum;
        set => SetProperty(ref minimum, value);
    }

    private int minimum;

    /// <summary>Largest value (Jexer bottom/right value; default 100).</summary>
    public int Maximum
    {
        get => maximum;
        set => SetProperty(ref maximum, value);
    }

    private int maximum = 100;

    /// <summary>Current value. Not clamped when set directly (as Jexer). Binds two-way by default.</summary>
    [BindsTwoWayByDefault]
    public int Value
    {
        get => value;
        set
        {
            if (this.value == value) return;
            this.value = value;
            OnPropertyChanged();
            ValueChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    /// <summary>Arrow step (default 1).</summary>
    public int SmallChange
    {
        get => smallChange;
        set => SetProperty(ref smallChange, value);
    }

    private int smallChange = 1;

    /// <summary>Page step (default 20).</summary>
    public int BigChange
    {
        get => bigChange;
        set => SetProperty(ref bigChange, value);
    }

    private int bigChange = 20;

    /// <summary>Raised whenever <see cref="Value"/> changes.</summary>
    public event EventHandler? ValueChanged;

    /// <inheritdoc/>
    protected internal override bool CanBeActivated => false;

    /// <summary>Length of the track including both arrows.</summary>
    private protected abstract int Length { get; }

    /// <summary>Position along the track for a local mouse event, or -1 when the event is off the scroller line.</summary>
    private protected abstract int Along(UiMouseEvent mouse);

    private int Clamp(int v) => Math.Clamp(v, Minimum, Math.Max(Minimum, Maximum));

    /// <summary>Position of the thumb along the track.</summary>
    protected int BoxPosition => (Length - 3) * (value - Minimum) / (Maximum - Minimum) + 1;

    /// <summary>Moves one small step back.</summary>
    public void Decrement()
    {
        if (Maximum != Minimum) Value = Clamp(value - SmallChange);
    }

    /// <summary>Moves one small step forward.</summary>
    public void Increment()
    {
        if (Maximum != Minimum) Value = Clamp(value + SmallChange);
    }

    /// <summary>Moves one page back.</summary>
    public void BigDecrement()
    {
        if (Maximum != Minimum) Value = Clamp(value - BigChange);
    }

    /// <summary>Moves one page forward.</summary>
    public void BigIncrement()
    {
        if (Maximum != Minimum) Value = Clamp(value + BigChange);
    }

    /// <summary>Moves to <see cref="Minimum"/>.</summary>
    public void ToMinimum() => Value = Minimum;

    /// <summary>Moves to <see cref="Maximum"/>.</summary>
    public void ToMaximum() => Value = Maximum;

    /// <inheritdoc/>
    protected internal override void OnMouseUp(UiMouseEvent mouse)
    {
        if (Maximum == Minimum) return;
        if (inScroll)
        {
            inScroll = false;
            return;
        }

        int p = Along(mouse);
        if (p < 0) return;
        if (p == 0) Decrement();
        else if (p == Length - 1) Increment();
        else if (p < BoxPosition) Value = Math.Max(Minimum, value - BigChange);
        else if (p > BoxPosition) Value = Math.Min(Maximum, value + BigChange);
    }

    /// <inheritdoc/>
    protected internal override void OnMouseMotion(UiMouseEvent mouse)
    {
        if (Maximum == Minimum)
        {
            inScroll = false;
            return;
        }

        int p = Along(mouse);
        if (mouse.IsLeft && inScroll && p > 0 && p < Length - 1)
        {
            Value = Clamp((Maximum - Minimum) * p / Math.Max(1, Length - 3) + Minimum);
            return;
        }

        inScroll = false;
    }

    /// <inheritdoc/>
    protected internal override void OnMouseDown(UiMouseEvent mouse)
    {
        if (Maximum == Minimum)
        {
            inScroll = false;
            return;
        }

        if (Along(mouse) == BoxPosition) inScroll = true;
    }
}

/// <summary>A vertical scroll bar (Jexer <c>TVScroller</c>); width 1. May draw over its window's right border.</summary>
public class VScroller : ScrollerBase
{
    /// <summary>Creates a vertical scroller.</summary>
    public VScroller()
    {
        Width = 1;
    }

    private protected override int Length => Height;

    private protected override int Along(UiMouseEvent mouse) => mouse.X == 0 ? mouse.Y : -1;

    internal override bool ClipsToBorderRight => false;

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        var arrowColor = GetColor("tscroller.arrows");
        var barColor = GetColor("tscroller.bar");
        context.PutChar(0, 0, GraphicsChars.CP437[0x1E], arrowColor);
        context.PutChar(0, Height - 1, GraphicsChars.CP437[0x1F], arrowColor);
        if (Maximum > Minimum)
        {
            context.VLine(0, 1, Height - 2, GraphicsChars.CP437[0xB1], barColor);
            context.PutChar(0, BoxPosition, GraphicsChars.Box, arrowColor);
        }
        else
        {
            context.VLine(0, 1, Height - 2, GraphicsChars.Hatch, barColor);
        }
    }
}

/// <summary>A horizontal scroll bar (Jexer <c>THScroller</c>); height 1. May draw over its window's bottom border.</summary>
public class HScroller : ScrollerBase
{
    /// <summary>Creates a horizontal scroller.</summary>
    public HScroller()
    {
        Height = 1;
    }

    private protected override int Length => Width;

    private protected override int Along(UiMouseEvent mouse) => mouse.Y == 0 ? mouse.X : -1;

    internal override bool ClipsToBorderBottom => false;

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        var arrowColor = GetColor("tscroller.arrows");
        var barColor = GetColor("tscroller.bar");
        context.PutChar(0, 0, GraphicsChars.CP437[0x11], arrowColor);
        context.PutChar(Width - 1, 0, GraphicsChars.CP437[0x10], arrowColor);
        if (Maximum > Minimum)
        {
            context.HLine(1, 0, Width - 2, GraphicsChars.CP437[0xB1], barColor);
            context.PutChar(BoxPosition, 0, GraphicsChars.Box, arrowColor);
        }
        else
        {
            context.HLine(1, 0, Width - 2, GraphicsChars.Hatch, barColor);
        }
    }
}

/// <summary>
/// Base for widgets with optional internal scroll bars along the right and bottom edges (Jexer <c>TScrollable</c>).
/// </summary>
public abstract class ScrollableWidget : Widget
{
    private HScroller? horizontalScroller;
    private VScroller? verticalScroller;

    /// <summary>The horizontal scroller, if any.</summary>
    public HScroller? HorizontalScroller
    {
        get => horizontalScroller;
        protected set
        {
            if (ReferenceEquals(horizontalScroller, value)) return;
            horizontalScroller?.ValueChanged -= OnHorizontalValueChanged;
            horizontalScroller = value;
            horizontalScroller?.ValueChanged += OnHorizontalValueChanged;
            OnPropertyChanged();
        }
    }

    /// <summary>The vertical scroller, if any.</summary>
    public VScroller? VerticalScroller
    {
        get => verticalScroller;
        protected set
        {
            if (ReferenceEquals(verticalScroller, value)) return;
            verticalScroller?.ValueChanged -= OnVerticalValueChanged;
            verticalScroller = value;
            verticalScroller?.ValueChanged += OnVerticalValueChanged;
            OnPropertyChanged();
        }
    }

    private void OnHorizontalValueChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(HorizontalValue));

    private void OnVerticalValueChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(VerticalValue));

    /// <summary>Vertical scroll position (0 without a vertical scroller). Binds two-way by default.</summary>
    [BindsTwoWayByDefault]
    public int VerticalValue
    {
        get => VerticalScroller?.Value ?? 0;
        set
        {
            if (VerticalScroller != null) VerticalScroller.Value = value;
        }
    }

    /// <summary>Horizontal scroll position (0 without a horizontal scroller). Binds two-way by default.</summary>
    [BindsTwoWayByDefault]
    public int HorizontalValue
    {
        get => HorizontalScroller?.Value ?? 0;
        set
        {
            if (HorizontalScroller != null) HorizontalScroller.Value = value;
        }
    }

    /// <summary>Recomputes content metrics (lines, scroll ranges) after a size or content change.</summary>
    public virtual void ReflowData()
    {
    }

    /// <summary>Positions the scrollers along the right and bottom edges.</summary>
    protected virtual void PlaceScrollbars()
    {
        if (HorizontalScroller is { } h)
        {
            h.SetDimensions(0, Height - 1, Math.Max(1, Width - 1), 1);
            h.BigChange = Width - 1;
        }

        if (VerticalScroller is { } v)
        {
            v.SetDimensions(Width - 1, 0, 1, Math.Max(1, Height - 1));
            v.BigChange = Height - 1;
        }
    }

    /// <inheritdoc/>
    protected internal override void OnSizeChanged()
    {
        ReflowData();
        PlaceScrollbars();
        base.OnSizeChanged();
    }

    /// <summary>Scrolls to the top-left.</summary>
    public void ToHome()
    {
        HorizontalScroller?.ToMinimum();
        VerticalScroller?.ToMinimum();
    }

    /// <summary>Scrolls to the bottom-right.</summary>
    public void ToEnd()
    {
        HorizontalScroller?.ToMaximum();
        VerticalScroller?.ToMaximum();
    }

    /// <summary>Handles the common scrolling keys; returns true when handled.</summary>
    protected bool HandleScrollKey(KeyEvent key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Modifiers != KeyModifiers.None) return false;
        switch (key.Key)
        {
            case Key.Left: HorizontalScroller?.Decrement(); return true;
            case Key.Right: HorizontalScroller?.Increment(); return true;
            case Key.Up: VerticalScroller?.Decrement(); return true;
            case Key.Down: VerticalScroller?.Increment(); return true;
            case Key.PageUp: VerticalScroller?.BigDecrement(); return true;
            case Key.PageDown: VerticalScroller?.BigIncrement(); return true;
            case Key.Home: VerticalScroller?.ToMinimum(); return true;
            case Key.End: VerticalScroller?.ToMaximum(); return true;
            default: return false;
        }
    }
}
