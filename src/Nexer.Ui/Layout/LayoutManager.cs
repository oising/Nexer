// Ported from Jexer layout/*.java (MIT, © Autumn Lamonte).
using System.Runtime.CompilerServices;

namespace Nexer.Ui.Layout;

/// <summary>
/// Positions a widget's children when its client area is resized (Jexer's <c>LayoutManager</c>).
/// Set as <see cref="Widget.Layout"/>; the layout attaches when the owner finishes initialisation, capturing the
/// owner's client size at that moment as the "original" size.
/// </summary>
public abstract class LayoutManager
{
    /// <summary>The widget whose children are laid out.</summary>
    public Widget? Owner { get; private set; }

    /// <summary>True when attached to an owner.</summary>
    public bool IsAttached => Owner != null;

    /// <summary>Current client width.</summary>
    protected int LayoutWidth { get; private set; }

    /// <summary>Current client height.</summary>
    protected int LayoutHeight { get; private set; }

    internal void Attach(Widget owner, int width, int height)
    {
        if (Owner != null && Owner != owner) throw new InvalidOperationException("The layout manager is already in use.");
        Owner = owner;
        LayoutWidth = width;
        LayoutHeight = height;
        OnAttached(width, height);
        foreach (var child in owner.ChildList.ToArray()) Add(child);
    }

    internal void Detach()
    {
        if (Owner == null) return;
        foreach (var child in Owner.ChildList.ToArray()) Remove(child);
        Owner = null;
    }

    /// <summary>Called when attached, before the existing children are added.</summary>
    protected virtual void OnAttached(int width, int height)
    {
    }

    /// <summary>The owner's client area changed size.</summary>
    public void OnResize(int width, int height)
    {
        LayoutWidth = width;
        LayoutHeight = height;
        LayoutChildren();
    }

    /// <summary>A child was added.</summary>
    public abstract void Add(Widget child);

    /// <summary>A child was removed.</summary>
    public abstract void Remove(Widget child);

    /// <summary>Re-reads a child's current bounds as its original bounds.</summary>
    public virtual void ResetSize(Widget child)
    {
    }

    /// <summary>Positions the children for the current size.</summary>
    protected abstract void LayoutChildren();
}

/// <summary>Scales every child's bounds proportionally to the owner's client size (Jexer <c>StretchLayoutManager</c>).</summary>
public sealed class StretchLayout : LayoutManager
{
    private readonly List<(Widget Child, Rect Original)> children = [];
    private int originalWidth, originalHeight;

    /// <inheritdoc/>
    protected override void OnAttached(int width, int height)
    {
        originalWidth = width;
        originalHeight = height;
    }

    /// <inheritdoc/>
    public override void Add(Widget child)
    {
        ArgumentNullException.ThrowIfNull(child);
        children.RemoveAll(c => c.Child == child);
        children.Add((child, child.Bounds));
        LayoutChildren();
    }

    /// <inheritdoc/>
    public override void Remove(Widget child)
    {
        children.RemoveAll(c => c.Child == child);
        LayoutChildren();
    }

    /// <inheritdoc/>
    public override void ResetSize(Widget child) => Add(child);

    /// <inheritdoc/>
    protected override void LayoutChildren()
    {
        double wr = originalWidth == 0 ? 1 : (double)LayoutWidth / originalWidth;
        double hr = originalHeight == 0 ? 1 : (double)LayoutHeight / originalHeight;
        foreach (var (child, r) in children.ToArray())
        {
            child.SetDimensions((int)(r.X * wr), (int)(r.Y * hr), (int)(r.Width * wr), (int)(r.Height * hr));
        }
    }
}

/// <summary>Direction of a <see cref="BoxLayout"/>.</summary>
public enum Orientation
{
    /// <summary>Children stacked top to bottom.</summary>
    Vertical,

    /// <summary>Children side by side.</summary>
    Horizontal,
}

/// <summary>Divides the client area evenly between the children (Jexer <c>BoxLayoutManager</c>).</summary>
public sealed class BoxLayout : LayoutManager
{
    private readonly List<Widget> children = [];

    /// <summary>Stacking direction (default vertical).</summary>
    public Orientation Orientation { get; set; } = Orientation.Vertical;

    /// <inheritdoc/>
    public override void Add(Widget child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!children.Contains(child)) children.Add(child);
        LayoutChildren();
    }

    /// <inheritdoc/>
    public override void Remove(Widget child)
    {
        children.Remove(child);
        LayoutChildren();
    }

    /// <inheritdoc/>
    protected override void LayoutChildren()
    {
        int n = children.Count;
        if (n == 0) return;
        if (Orientation == Orientation.Vertical)
        {
            int each = Math.Max(1, LayoutHeight / n);
            int leftover = LayoutHeight % n;
            for (int i = 0; i < n; i++)
            {
                var c = children[i];
                c.SetDimensions(c.X, i * each, LayoutWidth, each + (i == n - 1 ? leftover : 0));
            }
        }
        else
        {
            int each = Math.Max(1, LayoutWidth / n);
            int leftover = LayoutWidth % n;
            for (int i = 0; i < n; i++)
            {
                var c = children[i];
                c.SetDimensions(i * each, c.Y, each + (i == n - 1 ? leftover : 0), LayoutHeight);
            }
        }
    }
}

/// <summary>Edges a child is anchored to in an <see cref="AnchorLayout"/>.</summary>
[Flags]
public enum AnchorEdges
{
    /// <summary>Not anchored (keeps its position).</summary>
    None = 0,

    /// <summary>Keep the distance to the left edge.</summary>
    Left = 1,

    /// <summary>Keep the distance to the top edge.</summary>
    Top = 2,

    /// <summary>Keep the distance to the right edge.</summary>
    Right = 4,

    /// <summary>Keep the distance to the bottom edge.</summary>
    Bottom = 8,

    /// <summary>Left and top (default).</summary>
    TopLeft = Left | Top,

    /// <summary>All four edges (stretch both ways).</summary>
    All = Left | Top | Right | Bottom,
}

/// <summary>
/// Anchors children to the owner's edges via the attached property <c>AnchorLayout.Anchor</c>
/// (e.g. <c>AnchorLayout.Anchor="Right, Bottom"</c>). Anchored to both opposite edges, a child stretches;
/// to one edge, it keeps its distance to that edge. Default is <see cref="AnchorEdges.TopLeft"/>.
/// </summary>
/// <remarks>This replaces Jexer's <c>AnchoredLayoutManager</c>, whose resize arithmetic grows widgets by one cell per pass.</remarks>
public sealed class AnchorLayout : LayoutManager
{
    private static readonly ConditionalWeakTable<Widget, StrongBox<AnchorEdges>> Anchors = new();
    private readonly List<(Widget Child, Thickness Margin, Rect Original)> children = [];

    /// <summary>Gets the anchor edges of <paramref name="widget"/>.</summary>
    public static AnchorEdges GetAnchor(Widget widget)
    {
        ArgumentNullException.ThrowIfNull(widget);
        return Anchors.TryGetValue(widget, out var box) ? box.Value : AnchorEdges.TopLeft;
    }

    /// <summary>Sets the anchor edges of <paramref name="widget"/>.</summary>
    public static void SetAnchor(Widget widget, AnchorEdges value)
    {
        ArgumentNullException.ThrowIfNull(widget);
        Anchors.AddOrUpdate(widget, new StrongBox<AnchorEdges>(value));
        if (widget.Parent?.Layout is AnchorLayout layout && layout.IsAttached) layout.ResetSize(widget);
    }

    /// <inheritdoc/>
    public override void Add(Widget child)
    {
        ArgumentNullException.ThrowIfNull(child);
        children.RemoveAll(c => c.Child == child);
        var r = child.Bounds;
        children.Add((child, new Thickness(r.X, r.Y, LayoutWidth - r.Right, LayoutHeight - r.Bottom), r));
        LayoutChildren();
    }

    /// <inheritdoc/>
    public override void Remove(Widget child)
    {
        children.RemoveAll(c => c.Child == child);
    }

    /// <inheritdoc/>
    public override void ResetSize(Widget child) => Add(child);

    /// <inheritdoc/>
    protected override void LayoutChildren()
    {
        foreach (var (child, m, r) in children.ToArray())
        {
            var a = GetAnchor(child);
            int x = r.X, w = r.Width, y = r.Y, h = r.Height;
            bool left = (a & AnchorEdges.Left) != 0, right = (a & AnchorEdges.Right) != 0;
            bool top = (a & AnchorEdges.Top) != 0, bottom = (a & AnchorEdges.Bottom) != 0;
            if (left && right) w = Math.Max(0, LayoutWidth - m.Left - m.Right);
            else if (right) x = LayoutWidth - m.Right - w;
            if (top && bottom) h = Math.Max(0, LayoutHeight - m.Top - m.Bottom);
            else if (bottom) y = LayoutHeight - m.Bottom - h;
            child.SetDimensions(x, y, w, h);
        }
    }
}
