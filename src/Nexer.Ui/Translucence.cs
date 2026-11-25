// Ported from Jexer TApplication.java (drawTranslucentWindow) and LogicalScreen.java (blendScreen,
// blendRectangle) (MIT, © Autumn Lamonte).
using System.Text;
using Nexer.Core;

namespace Nexer.Ui;

/// <summary>
/// Jexer's window translucence: a window is drawn opaquely, then blended over what was underneath at
/// <see cref="Window.Alpha"/>, followed by a blended black drop shadow. Blended colours are emitted as RGB.
/// </summary>
internal static class Translucence
{
    private static readonly (byte R, byte G, byte B)[] Vga =
    [
        (0x00, 0x00, 0x00), (0xa8, 0x00, 0x00), (0x00, 0xa8, 0x00), (0xa8, 0x54, 0x00),
        (0x00, 0x00, 0xa8), (0xa8, 0x00, 0xa8), (0x00, 0xa8, 0xa8), (0xa8, 0xa8, 0xa8),
        (0x54, 0x54, 0x54), (0xfc, 0x54, 0x54), (0x54, 0xfc, 0x54), (0xfc, 0xfc, 0x54),
        (0x54, 0x54, 0xfc), (0xfc, 0x54, 0xfc), (0x54, 0xfc, 0xfc), (0xfc, 0xfc, 0xfc),
    ];

    private static readonly (byte R, byte G, byte B) DefaultForeground = (0xa8, 0xa8, 0xa8);
    private static readonly (byte R, byte G, byte B) DefaultBackground = (0x00, 0x00, 0x00);

    private readonly record struct Saved(Cell Cell, string Text);

    /// <summary>Draws <paramref name="window"/> blended over the current screen content (Jexer <c>drawTranslucentWindow</c>).</summary>
    public static void DrawWindow(CellSurface surface, Window window, Rect screen)
    {
        int alpha = Math.Clamp(window.Alpha, 0, 255);
        var rect = window.Bounds.Intersect(screen);
        var before = Snapshot(surface, rect);
        window.DrawTree(surface, screen);
        if (alpha < 255 && !rect.IsEmpty)
        {
            var after = Snapshot(surface, rect);
            Blend(surface, rect, before, after, alpha);
        }

        int shadowAlpha = 30 * alpha / 100;
        BlendBlack(surface, new Rect(window.X + window.Width, window.Y + 1, 2, window.Height - 1).Intersect(screen), shadowAlpha);
        BlendBlack(surface, new Rect(window.X + 2, window.Y + window.Height, window.Width, 1).Intersect(screen), shadowAlpha);
    }

    /// <summary>Resolves a colour to RGB using Jexer's default VGA palette (bold brightens the 8 base colours).</summary>
    public static (byte R, byte G, byte B) ToRgb(TerminalColor color, bool foreground, bool bold)
    {
        switch (color.Kind)
        {
            case TerminalColorKind.Rgb:
                return ((byte)(color.Value >> 16), (byte)(color.Value >> 8), (byte)color.Value);
            case TerminalColorKind.Indexed:
                int index = (int)color.Value;
                if (index < 8) return Vga[foreground && bold ? index + 8 : index];
                if (index < 16) return Vga[index];
                if (index < 232)
                {
                    index -= 16;
                    return (Cube(index / 36), Cube(index / 6 % 6), Cube(index % 6));
                }

                byte grey = (byte)(8 + (index - 232) * 10);
                return (grey, grey, grey);
            default:
                return foreground ? DefaultForeground : DefaultBackground;
        }
    }

    private static byte Cube(int level) => (byte)(level == 0 ? 0 : 55 + level * 40);

    private static (byte R, byte G, byte B) Lerp((byte R, byte G, byte B) under, (byte R, byte G, byte B) over, int alpha) =>
        ((byte)((over.R * alpha + under.R * (255 - alpha)) / 255),
         (byte)((over.G * alpha + under.G * (255 - alpha)) / 255),
         (byte)((over.B * alpha + under.B * (255 - alpha)) / 255));

    private static TerminalColor ToColor((byte R, byte G, byte B) c) => TerminalColor.Rgb(c.R, c.G, c.B);

    private static int Distance((byte R, byte G, byte B) a, (byte R, byte G, byte B) b)
    {
        int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
        return (int)Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    private static Saved[] Snapshot(CellSurface surface, Rect rect)
    {
        var saved = new Saved[Math.Max(0, rect.Width * rect.Height)];
        for (int y = 0; y < rect.Height; y++)
        {
            var row = surface.GetRow(rect.Y + y);
            for (int x = 0; x < rect.Width; x++)
            {
                var cell = row[rect.X + x];
                saved[y * rect.Width + x] = new Saved(cell, cell.HasCluster ? surface.GetText(rect.X + x, rect.Y + y) : "");
            }
        }

        return saved;
    }

    private static (TerminalColor Fg, TerminalColor Bg) Colors(CellStyle style) => (style.Foreground, style.Background);

    private static bool IsPlainSpace(Cell cell) =>
        cell.Role == CellRole.Single && !cell.HasCluster && cell.Glyph.Value == ' ' &&
        (cell.Style.Flags & CellStyleFlags.Underline) == 0;

    private static bool IsHatch(Rune glyph) => glyph.Value is >= 0x2591 and <= 0x2593;

    // Jexer blendScreen: background = over.bg blended onto under.bg; a blank overlay lets the underlying glyph show
    // through (unless it would be invisible, or is a hatch character); otherwise the overlay glyph is drawn.
    private static void Blend(CellSurface surface, Rect rect, Saved[] under, Saved[] over, int alpha)
    {
        for (int y = 0; y < rect.Height; y++)
        {
            for (int x = 0; x < rect.Width; x++)
            {
                int ax = rect.X + x, ay = rect.Y + y;
                var u = under[y * rect.Width + x];
                var o = over[y * rect.Width + x];
                if (o.Cell.Role == CellRole.WideContinuation) continue;
                bool uBold = (u.Cell.Style.Flags & CellStyleFlags.Bold) != 0;
                bool oBold = (o.Cell.Style.Flags & CellStyleFlags.Bold) != 0;
                var (uFg, uBg) = Colors(u.Cell.Style);
                var (oFg, oBg) = Colors(o.Cell.Style);
                var underBg = ToRgb(uBg, false, uBold);
                var overBg = ToRgb(oBg, false, oBold);
                var newBg = Lerp(underBg, overBg, alpha);
                if (IsPlainSpace(o.Cell))
                {
                    var newFg = Lerp(ToRgb(uFg, true, uBold), overBg, alpha);
                    bool showThrough = u.Cell.Role == CellRole.Single && !IsPlainSpace(u.Cell) &&
                        !IsHatch(u.Cell.Glyph) && Distance(newFg, newBg) >= 5;
                    var style = new CellStyle(ToColor(newFg), ToColor(newBg), showThrough ? u.Cell.Style.Flags : o.Cell.Style.Flags);
                    if (showThrough) Write(surface, ax, ay, u, 1, style);
                    else surface.Write(ax, ay, new Rune(' '), 1, style);
                }
                else
                {
                    var fg = Lerp(newBg, ToRgb(oFg, true, oBold), alpha);
                    int width = o.Cell.Role == CellRole.WideLead && x + 1 < rect.Width ? 2 : 1;
                    var style = new CellStyle(ToColor(fg), ToColor(newBg), o.Cell.Style.Flags);
                    if (o.Cell.Role == CellRole.WideLead && width == 1) surface.Write(ax, ay, new Rune(' '), 1, style);
                    else Write(surface, ax, ay, o, width, style);
                }
            }
        }
    }

    private static void Write(CellSurface surface, int x, int y, Saved saved, int width, CellStyle style)
    {
        if (saved.Cell.HasCluster)
        {
            var runes = TextMetrics.ToRunes(saved.Text).ToArray();
            surface.WriteCluster(x, y, runes, width, style);
        }
        else
        {
            surface.Write(x, y, saved.Cell.Glyph, width, style);
        }
    }

    // Jexer blendRectangle with black: darkens both colours, keeping the glyphs.
    private static void BlendBlack(CellSurface surface, Rect rect, int alpha)
    {
        if (rect.IsEmpty || alpha <= 0) return;
        var black = (R: (byte)0, G: (byte)0, B: (byte)0);
        var cells = Snapshot(surface, rect);
        for (int y = 0; y < rect.Height; y++)
        {
            for (int x = 0; x < rect.Width; x++)
            {
                var saved = cells[y * rect.Width + x];
                var cell = saved.Cell;
                if (cell.Role == CellRole.WideContinuation) continue;
                bool bold = (cell.Style.Flags & CellStyleFlags.Bold) != 0;
                var fg = Lerp(ToRgb(cell.Style.Foreground, true, bold), black, alpha);
                var bg = Lerp(ToRgb(cell.Style.Background, false, bold), black, alpha);
                var style = new CellStyle(ToColor(fg), ToColor(bg), cell.Style.Flags);
                int width = cell.Role == CellRole.WideLead && x + 1 < rect.Width ? 2 : 1;
                if (cell.Role == CellRole.WideLead && width == 1) surface.Write(rect.X + x, rect.Y + y, new Rune(' '), 1, style);
                else Write(surface, rect.X + x, rect.Y + y, saved, width, style);
            }
        }
    }
}
