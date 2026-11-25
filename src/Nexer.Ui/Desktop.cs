// Ported from Jexer TDesktop.java (MIT, © Autumn Lamonte).
using System.Text;

namespace Nexer.Ui;

/// <summary>
/// The background behind all windows (Jexer <c>TDesktop</c>): a hatched area covering the screen between the menu
/// bar and the status bar. Assign to <see cref="Application.Desktop"/>. Children are not bordered; a single
/// non-window child is resized to fill the desktop.
/// </summary>
public class Desktop : Window
{
    /// <summary>Creates a desktop.</summary>
    public Desktop()
    {
        CloseBox = false;
        ZoomBox = false;
        Resizable = false;
    }

    /// <inheritdoc/>
    public override bool IsDesktop => true;

    /// <summary>The fill character (default <see cref="GraphicsChars.Hatch"/>).</summary>
    public Rune BackgroundChar { get; set; } = new(GraphicsChars.Hatch);

    /// <inheritdoc/>
    protected internal override int ChildOffset => 0;

    /// <inheritdoc/>
    protected override bool DrawsShadow => false;

    /// <inheritdoc/>
    protected internal override void OnSizeChanged()
    {
        var children = ChildList;
        if (children.Count == 1)
        {
            children[0].SetDimensions(children[0].X, children[0].Y, Width, Height);
            return;
        }

        LayoutOnResize();
    }

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        context.PutAll(BackgroundChar, GetColor("tdesktop.background"));
    }

    /// <inheritdoc/>
    protected internal override void OnMouseDown(UiMouseEvent mouse)
    {
        if (StatusBar != null && StatusBar.HandleMouseDown(mouse)) return;
        RouteToHitChild(mouse, static (w, m) => w.OnMouseDown(m));
    }

    /// <inheritdoc/>
    protected internal override void OnMouseUp(UiMouseEvent mouse)
    {
        if (StatusBar != null && StatusBar.HandleMouseUp(mouse)) return;
        RouteToHitChild(mouse, static (w, m) => w.OnMouseUp(m));
    }

    /// <inheritdoc/>
    protected internal override void OnMouseDoubleClick(UiMouseEvent mouse) =>
        RouteToHitChild(mouse, static (w, m) => w.OnMouseDoubleClick(m));

    /// <inheritdoc/>
    protected internal override void OnMouseMotion(UiMouseEvent mouse)
    {
        StatusBar?.HandleMouseMotion(mouse);
        foreach (var w in ChildList.ToArray()) w.OnMouseMotion(mouse.RelativeTo(w));
    }

    private void RouteToHitChild(UiMouseEvent mouse, Action<Widget, UiMouseEvent> handler)
    {
        foreach (var w in ChildList.ToArray())
        {
            if (w.MouseWouldHit(mouse))
            {
                Activate(w);
                handler(w, mouse.RelativeTo(w));
                return;
            }
        }
    }
}
