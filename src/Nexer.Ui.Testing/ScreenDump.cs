using System.Text;
using Nexer.Core;

namespace Nexer.Ui.Testing;

/// <summary>An immutable copy of a presented frame.</summary>
public sealed class ScreenSnapshot
{
    private readonly string[] text;
    private readonly CellStyle[] styles;
    private readonly CellRole[] roles;

    /// <summary>Copies <paramref name="surface"/>.</summary>
    public ScreenSnapshot(IReadOnlyCellSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        Columns = surface.Columns;
        Rows = surface.Rows;
        text = new string[Columns * Rows];
        styles = new CellStyle[Columns * Rows];
        roles = new CellRole[Columns * Rows];
        for (int y = 0; y < Rows; y++)
        {
            var row = surface.GetRow(y);
            for (int x = 0; x < Columns; x++)
            {
                int i = y * Columns + x;
                roles[i] = row[x].Role;
                styles[i] = row[x].Style;
                text[i] = row[x].Role == CellRole.WideContinuation ? "" : surface.GetText(x, y);
            }
        }
    }

    /// <summary>Width in cells.</summary>
    public int Columns { get; }

    /// <summary>Height in cells.</summary>
    public int Rows { get; }

    /// <summary>The grapheme in the cell ("" for the right half of a wide character).</summary>
    public string GetText(int x, int y) => text[Index(x, y)];

    /// <summary>The style of the cell.</summary>
    public CellStyle GetStyle(int x, int y) => styles[Index(x, y)];

    /// <summary>The role of the cell.</summary>
    public CellRole GetRole(int x, int y) => roles[Index(x, y)];

    /// <summary>The text of row <paramref name="y"/> from column <paramref name="x"/> for <paramref name="width"/> cells.</summary>
    public string GetRowText(int y, int x = 0, int width = -1)
    {
        if (width < 0) width = Columns - x;
        var sb = new StringBuilder();
        for (int i = x; i < x + width && i < Columns; i++) sb.Append(GetText(i, y));
        return sb.ToString();
    }

    private int Index(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Columns);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Rows);
        return y * Columns + x;
    }
}

/// <summary>Text dumps of frames for assertions.</summary>
public static class ScreenDump
{
    /// <summary>
    /// The text of a rectangular region, one line per row joined with <c>\n</c>. Trailing spaces are trimmed from
    /// each line unless <paramref name="trimEnd"/> is false.
    /// </summary>
    public static string Text(ScreenSnapshot screen, int x = 0, int y = 0, int width = -1, int height = -1, bool trimEnd = true)
    {
        ArgumentNullException.ThrowIfNull(screen);
        if (width < 0) width = screen.Columns - x;
        if (height < 0) height = screen.Rows - y;
        var lines = new List<string>();
        for (int row = y; row < y + height && row < screen.Rows; row++)
        {
            string line = screen.GetRowText(row, x, width);
            lines.Add(trimEnd ? line.TrimEnd(' ') : line);
        }

        return string.Join("\n", lines);
    }

    /// <summary>The text of a surface (see <see cref="Text(ScreenSnapshot,int,int,int,int,bool)"/>).</summary>
    public static string Text(IReadOnlyCellSurface surface, int x = 0, int y = 0, int width = -1, int height = -1, bool trimEnd = true) =>
        Text(new ScreenSnapshot(surface), x, y, width, height, trimEnd);

    /// <summary>
    /// Renders a rectangular region as a self-contained SVG image with the frame's colours and attributes, for
    /// documentation screenshots and visual review (for example <c>File.WriteAllText("main.svg", ScreenDump.Svg(driver.Frame))</c>).
    /// Output is deterministic, so regenerated images diff cleanly.
    /// </summary>
    public static string Svg(ScreenSnapshot screen, SvgOptions? options = null, int x = 0, int y = 0, int width = -1, int height = -1)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        return ScreenSvg.Render(screen, options ?? new SvgOptions(), x, y, width, height);
    }

    /// <summary>Renders a surface as SVG (see <see cref="Svg(ScreenSnapshot,SvgOptions?,int,int,int,int)"/>).</summary>
    public static string Svg(IReadOnlyCellSurface surface, SvgOptions? options = null, int x = 0, int y = 0, int width = -1, int height = -1) =>
        Svg(new ScreenSnapshot(surface), options, x, y, width, height);

    /// <summary>
    /// An attribute dump of a region: each distinct style gets a letter (a, b, c... in order of first appearance)
    /// and the legend follows the grid.
    /// </summary>
    public static string Attributes(ScreenSnapshot screen, int x = 0, int y = 0, int width = -1, int height = -1)
    {
        ArgumentNullException.ThrowIfNull(screen);
        if (width < 0) width = screen.Columns - x;
        if (height < 0) height = screen.Rows - y;
        var legend = new List<CellStyle>();
        var sb = new StringBuilder();
        for (int row = y; row < y + height && row < screen.Rows; row++)
        {
            for (int col = x; col < x + width && col < screen.Columns; col++)
            {
                var style = screen.GetStyle(col, row);
                int index = legend.IndexOf(style);
                if (index < 0)
                {
                    index = legend.Count;
                    legend.Add(style);
                }

                sb.Append(index < 26 ? (char)('a' + index) : '?');
            }

            sb.Append('\n');
        }

        for (int i = 0; i < legend.Count; i++) sb.Append(i < 26 ? (char)('a' + i) : '?').Append(": ").Append(legend[i]).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }
}
