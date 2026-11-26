// Ported from Jexer TPanel.java (MIT, © Autumn Lamonte).
namespace Nexer.Ui.Controls;

/// <summary>Where a <see cref="Panel"/> draws its title (Jexer <c>TPanel.Direction</c>).</summary>
public enum PanelTitlePosition
{
    /// <summary>Top edge, left aligned.</summary>
    TopLeft,

    /// <summary>Top edge, centred.</summary>
    Top,

    /// <summary>Top edge, right aligned.</summary>
    TopRight,

    /// <summary>Bottom edge, left aligned.</summary>
    BottomLeft,

    /// <summary>Bottom edge, centred.</summary>
    Bottom,

    /// <summary>Bottom edge, right aligned.</summary>
    BottomRight,
}

/// <summary>
/// A container that fills its area with the <c>"tpanel.border"</c> colour and optionally draws a border and a title
/// (Jexer <c>TPanel</c>). Children are positioned relative to the panel's top-left corner (the border does not
/// offset them). A panel whose only child is a <see cref="Panel"/> or <see cref="SplitPane"/> resizes that child
/// with itself; a panel that is the only child of a window fills the window.
/// </summary>
public class Panel : Widget, IFillsWindow
{
    private string? title;
    private PanelTitlePosition titlePosition = PanelTitlePosition.TopLeft;
    private BorderStyle borderStyle = BorderStyle.None;

    /// <summary>Title text (trimmed), drawn on the border; null or empty for none.</summary>
    public string? Title
    {
        get => title;
        set
        {
            title = value?.Trim();
            Invalidate();
            OnPropertyChanged();
        }
    }

    /// <summary>Where the title is drawn (default <see cref="PanelTitlePosition.TopLeft"/>).</summary>
    public PanelTitlePosition TitlePosition
    {
        get => titlePosition;
        set
        {
            titlePosition = value;
            Invalidate();
            OnPropertyChanged();
        }
    }

    /// <summary>Border style (default <see cref="Nexer.Ui.BorderStyle.None"/>, which fills the edges with spaces).</summary>
    public BorderStyle BorderStyle
    {
        get => borderStyle;
        set
        {
            borderStyle = value ?? BorderStyle.None;
            Invalidate();
            OnPropertyChanged();
        }
    }

    /// <inheritdoc/>
    protected internal override void OnSizeChanged()
    {
        var children = ChildList;
        if (children.Count == 1 && children[0] is Panel or SplitPane)
        {
            var child = children[0];
            child.SetDimensions(child.X, child.Y, Width, Height);
            return;
        }

        base.OnSizeChanged();
    }

    /// <inheritdoc/>
    protected override void OnChildAdded(Widget child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (ChildList.Count == 1 && child is Panel or SplitPane && child.Width == 0 && child.Height == 0)
        {
            child.SetDimensions(child.X, child.Y, Width, Height);
        }

        base.OnChildAdded(child);
    }

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var color = GetColor("tpanel.border");
        context.DrawBox(0, 0, Width, Height, color, color, BorderStyle, false);
        if (string.IsNullOrEmpty(title)) return;

        int titleLength = TextMetrics.Width(title) + 2;
        (int titleX, int titleY) = TitlePosition switch
        {
            PanelTitlePosition.Top => ((Width - titleLength) / 2, 0),
            PanelTitlePosition.TopRight => (Width - titleLength - 1, 0),
            PanelTitlePosition.BottomLeft => (1, Height - 1),
            PanelTitlePosition.Bottom => ((Width - titleLength) / 2, Height - 1),
            PanelTitlePosition.BottomRight => (Width - titleLength - 1, Height - 1),
            _ => (1, 0),
        };
        context.HLine(titleX, titleY, titleLength, ' ', color);
        context.PutString(BorderStyle == BorderStyle.None ? titleX : titleX + 1, titleY, title, color);
    }
}
