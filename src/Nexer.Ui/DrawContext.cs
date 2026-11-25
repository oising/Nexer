// Ported from Jexer backend/LogicalScreen.java drawing primitives (MIT, © Autumn Lamonte).
using System.Text;
using Nexer.Core;

namespace Nexer.Ui;

/// <summary>
/// Draws onto the application back buffer in a widget's local coordinates. The context holds the widget's
/// absolute origin and an absolute clip rectangle (intersection of the ancestors' clips), Jexer's
/// clip/offset contract. Writes outside the clip are silently dropped.
/// </summary>
public sealed class DrawContext
{
    private readonly CellSurface surface;

    /// <summary>Creates a context for the whole surface.</summary>
    public DrawContext(CellSurface surface)
        : this(surface, 0, 0, surface.Columns, surface.Rows, new Rect(0, 0, surface.Columns, surface.Rows))
    {
    }

    /// <summary>Creates a context with an explicit origin, size and absolute clip.</summary>
    public DrawContext(CellSurface surface, int offsetX, int offsetY, int width, int height, Rect clip)
    {
        ArgumentNullException.ThrowIfNull(surface);
        this.surface = surface;
        OffsetX = offsetX;
        OffsetY = offsetY;
        Width = width;
        Height = height;
        Clip = clip.Intersect(new Rect(0, 0, surface.Columns, surface.Rows));
    }

    /// <summary>The back buffer.</summary>
    public IReadOnlyCellSurface Surface => surface;

    /// <summary>Absolute column of local x = 0.</summary>
    public int OffsetX { get; }

    /// <summary>Absolute row of local y = 0.</summary>
    public int OffsetY { get; }

    /// <summary>Width of the widget being drawn.</summary>
    public int Width { get; }

    /// <summary>Height of the widget being drawn.</summary>
    public int Height { get; }

    /// <summary>Absolute clip rectangle.</summary>
    public Rect Clip { get; }

    internal CellSurface MutableSurface => surface;

    /// <summary>Creates a context for a nested area at local (x, y), clipped to this context.</summary>
    public DrawContext CreateChild(int x, int y, int width, int height) =>
        new(surface, OffsetX + x, OffsetY + y, width, height, Clip.Intersect(new Rect(OffsetX + x, OffsetY + y, width, height)));

    /// <summary>Returns a copy of this context with a different absolute clip (intersected with the screen only).</summary>
    public DrawContext WithClip(Rect absoluteClip) => new(surface, OffsetX, OffsetY, Width, Height, absoluteClip);

    /// <summary>True when local (x, y) is inside the clip.</summary>
    public bool IsVisible(int x, int y) => Clip.Contains(x + OffsetX, y + OffsetY);

    /// <summary>Writes one character (wide characters occupy two cells; a wide char cut by the clip becomes a space).</summary>
    public void PutChar(int x, int y, Rune ch, CellStyle style)
    {
        if (!IsVisible(x, y)) return;
        if (Rune.IsControl(ch)) ch = new Rune(' ');
        int w = TextMetrics.RuneWidth(ch);
        int ax = x + OffsetX, ay = y + OffsetY;
        if (w == 2)
        {
            if (Clip.Contains(ax + 1, ay)) surface.Write(ax, ay, ch, 2, style);
            else surface.Write(ax, ay, new Rune(' '), 1, style);
            return;
        }

        surface.Write(ax, ay, w == 0 ? new Rune(' ') : ch, 1, style);
    }

    /// <summary>Writes one character.</summary>
    public void PutChar(int x, int y, char ch, CellStyle style) => PutChar(x, y, new Rune(ch), style);

    /// <summary>Writes a grapheme cluster with a known width (1 or 2).</summary>
    public void PutCluster(int x, int y, ReadOnlySpan<Rune> cluster, int width, CellStyle style)
    {
        if (!IsVisible(x, y) || cluster.IsEmpty) return;
        int ax = x + OffsetX, ay = y + OffsetY;
        if (width <= 0) return;
        foreach (var r in cluster)
        {
            if (Rune.IsControl(r))
            {
                surface.Write(ax, ay, new Rune(' '), 1, style);
                return;
            }
        }

        if (width >= 2 && !Clip.Contains(ax + 1, ay))
        {
            surface.Write(ax, ay, new Rune(' '), 1, style);
            return;
        }

        if (cluster.Length > CellSurface.MaxClusterScalars) cluster = cluster[..1];
        surface.WriteCluster(ax, ay, cluster, Math.Min(width, 2), style);
    }

    /// <summary>Writes a string (grapheme- and width-aware). Returns the number of columns advanced.</summary>
    public int PutString(int x, int y, string? text, CellStyle style)
    {
        int col = x;
        foreach (var (cluster, w) in TextMetrics.Clusters(text))
        {
            if (w == 0) continue;
            int width = Math.Min(w, 2);
            PutCluster(col, y, cluster, width, style);
            col += width;
        }

        return col - x;
    }

    /// <summary>Changes the style of a cell, keeping its glyph.</summary>
    public void PutAttr(int x, int y, CellStyle style)
    {
        if (!IsVisible(x, y)) return;
        RestyleAbsolute(x + OffsetX, y + OffsetY, _ => style);
    }

    /// <summary>Horizontal line of <paramref name="n"/> copies of <paramref name="ch"/>.</summary>
    public void HLine(int x, int y, int n, Rune ch, CellStyle style)
    {
        for (int i = 0; i < n; i++) PutChar(x + i, y, ch, style);
    }

    /// <summary>Horizontal line.</summary>
    public void HLine(int x, int y, int n, char ch, CellStyle style) => HLine(x, y, n, new Rune(ch), style);

    /// <summary>Vertical line of <paramref name="n"/> copies of <paramref name="ch"/>.</summary>
    public void VLine(int x, int y, int n, Rune ch, CellStyle style)
    {
        for (int i = 0; i < n; i++) PutChar(x, y + i, ch, style);
    }

    /// <summary>Vertical line.</summary>
    public void VLine(int x, int y, int n, char ch, CellStyle style) => VLine(x, y, n, new Rune(ch), style);

    /// <summary>Fills a local rectangle.</summary>
    public void Fill(int x, int y, int width, int height, Rune ch, CellStyle style)
    {
        for (int row = 0; row < height; row++) HLine(x, y + row, width, ch, style);
    }

    /// <summary>Fills the whole widget area (Jexer putAll).</summary>
    public void PutAll(Rune ch, CellStyle style) => Fill(0, 0, Width, Height, ch, style);

    /// <summary>Fills the whole widget area with spaces.</summary>
    public void Clear(CellStyle style) => PutAll(new Rune(' '), style);

    /// <summary>
    /// Draws a box; <paramref name="right"/>/<paramref name="bottom"/> are exclusive (Jexer drawBox).
    /// </summary>
    public void DrawBox(int left, int top, int right, int bottom, CellStyle border, CellStyle background,
        BorderStyle borderStyle, bool shadow)
    {
        ArgumentNullException.ThrowIfNull(borderStyle);
        int w = right - left, h = bottom - top;
        PutChar(left, top, borderStyle.TopLeft, border);
        PutChar(left + w - 1, top, borderStyle.TopRight, border);
        PutChar(left, top + h - 1, borderStyle.BottomLeft, border);
        PutChar(left + w - 1, top + h - 1, borderStyle.BottomRight, border);
        HLine(left + 1, top, w - 2, borderStyle.Horizontal, border);
        VLine(left, top + 1, h - 2, borderStyle.Vertical, border);
        HLine(left + 1, top + h - 1, w - 2, borderStyle.Horizontal, border);
        VLine(left + w - 1, top + 1, h - 2, borderStyle.Vertical, border);
        for (int i = 1; i < h - 1; i++) HLine(left + 1, top + i, w - 2, ' ', background);
        if (shadow) DrawBoxShadow(left, top, right, bottom);
    }

    /// <summary>The Jexer shadow style: bold black on black, keeping the underlying glyphs.</summary>
    public static CellStyle ShadowStyle { get; } =
        new(TerminalColor.Indexed(0), TerminalColor.Indexed(0), CellStyleFlags.Bold);

    /// <summary>
    /// Draws the box shadow two columns to the right and one row below (Jexer drawBoxShadow). The shadow is
    /// clipped only to the screen (and the clip's top/left), not to the widget.
    /// </summary>
    public void DrawBoxShadow(int left, int top, int right, int bottom)
    {
        int w = right - left, h = bottom - top;
        var clip = new Rect(Clip.X, Clip.Y, surface.Columns - Clip.X, surface.Rows - Clip.Y);
        for (int i = 0; i < h; i++)
        {
            ShadowCell(clip, OffsetX + left + w, OffsetY + top + 1 + i);
            ShadowCell(clip, OffsetX + left + w + 1, OffsetY + top + 1 + i);
        }

        for (int i = 0; i < w; i++) ShadowCell(clip, OffsetX + left + 2 + i, OffsetY + top + h);
    }

    private void ShadowCell(Rect clip, int ax, int ay)
    {
        if (!clip.Contains(ax, ay)) return;
        var cell = surface.GetRow(ay)[ax];
        if (cell.Role != CellRole.Single)
        {
            surface.Write(ax, ay, new Rune(' '), 1, ShadowStyle);
            return;
        }

        RestyleAbsolute(ax, ay, _ => ShadowStyle);
    }

    /// <summary>Reads the cell at absolute (ax, ay).</summary>
    internal Cell GetAbsoluteCell(int ax, int ay) => surface.GetRow(ay)[ax];

    internal void RestyleAbsolute(int ax, int ay, Func<CellStyle, CellStyle> restyle)
    {
        var cell = surface.GetRow(ay)[ax];
        if (cell.Role == CellRole.WideContinuation)
        {
            if (ax == 0) return;
            ax--;
            cell = surface.GetRow(ay)[ax];
        }

        int width = cell.Role == CellRole.WideLead ? 2 : 1;
        var style = restyle(cell.Style);
        if (cell.HasCluster)
        {
            var runes = TextMetrics.ToRunes(surface.GetText(ax, ay));
            surface.WriteCluster(ax, ay, runes.ToArray(), width, style);
        }
        else
        {
            surface.Write(ax, ay, cell.Glyph, width, style);
        }
    }
}
