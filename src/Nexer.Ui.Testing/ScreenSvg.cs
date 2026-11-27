using System.Globalization;
using System.Text;
using Nexer.Core;

namespace Nexer.Ui.Testing;

/// <summary>Options for <see cref="ScreenDump.Svg(ScreenSnapshot, SvgOptions?, int, int, int, int)"/>.</summary>
public sealed record SvgOptions
{
    /// <summary>Jexer's default VGA palette for indexes 0-15 (0xRRGGBB).</summary>
    public static IReadOnlyList<uint> VgaPalette { get; } =
    [
        0x000000, 0xa80000, 0x00a800, 0xa85400, 0x0000a8, 0xa800a8, 0x00a8a8, 0xa8a8a8,
        0x545454, 0xfc5454, 0x54fc54, 0xfcfc54, 0x5454fc, 0xfc54fc, 0x54fcfc, 0xfcfcfc,
    ];

    /// <summary>Colours for indexes 0-15 (0xRRGGBB, 16 entries). Indexes 16-255 use the standard xterm cube and grey ramp.</summary>
    public IReadOnlyList<uint> Palette { get; init; } = VgaPalette;

    /// <summary>The colour of <see cref="TerminalColor.Default"/> foregrounds (0xRRGGBB).</summary>
    public uint DefaultForeground { get; init; } = 0xa8a8a8;

    /// <summary>The colour of <see cref="TerminalColor.Default"/> backgrounds (0xRRGGBB).</summary>
    public uint DefaultBackground { get; init; } = 0x000000;

    /// <summary>When true (the Jexer convention), bold text with a base colour (0-7) uses the bright colour (8-15).</summary>
    public bool BoldIsBright { get; init; } = true;

    /// <summary>CSS font-family list for text.</summary>
    public string FontFamily { get; init; } = "'Cascadia Mono', Consolas, 'DejaVu Sans Mono', Menlo, monospace";

    /// <summary>Font size in pixels.</summary>
    public double FontSize { get; init; } = 15;

    /// <summary>Width of one cell in pixels.</summary>
    public double CellWidth { get; init; } = 9;

    /// <summary>Height of one cell in pixels.</summary>
    public double CellHeight { get; init; } = 18;

    /// <summary>
    /// When true, box-drawing (U+2500-U+257F) and block (U+2580-U+259F) characters are drawn as shapes so that
    /// borders join seamlessly whatever font the viewer has; otherwise they are emitted as text.
    /// </summary>
    public bool DrawBoxCharacters { get; init; } = true;

    /// <summary>Optional accessible title (emitted as the SVG <c>&lt;title&gt;</c>).</summary>
    public string? Title { get; init; }
}

/// <summary>Renders frames to SVG.</summary>
internal static class ScreenSvg
{
    // Box-drawing characters and their arm weights per direction (up, right, down, left): 0 none, 1 light, 2 heavy,
    // 3 double. BoxArmWeights holds one 4-digit group per character of BoxChars, in the same order.
    private const string BoxChars = "─│┌┐└┘├┤┬┴┼━┃┏┓┗┛┣┫┳┻╋═║╒╓╔╕╖╗╘╙╚╛╜╝╞╟╠╡╢╣╤╥╦╧╨╩╪╫╬╴╵╶╷";

    private const string BoxArmWeights =
        "0101 1010 0110 0011 1100 1001 1110 1011 0111 1101 1111 "
        + "0202 2020 0220 0022 2200 2002 2220 2022 0222 2202 2222 "
        + "0303 3030 0310 0130 0330 0013 0031 0033 1300 3100 3300 1003 3001 3003 "
        + "1310 3130 3330 1013 3031 3033 0313 0131 0333 1303 3101 3303 1313 3131 3333 "
        + "0001 1000 0100 0010";

    private static readonly Dictionary<char, string> BoxArms = BoxChars
        .Zip(BoxArmWeights.Split(' '))
        .ToDictionary(p => p.First, p => p.Second);

    public static string Render(ScreenSnapshot screen, SvgOptions options, int x, int y, int width, int height)
    {
        if (options.Palette.Count < 16) throw new ArgumentException("The palette needs 16 entries.", nameof(options));
        if (width < 0) width = screen.Columns - x;
        if (height < 0) height = screen.Rows - y;
        width = Math.Clamp(width, 0, screen.Columns - x);
        height = Math.Clamp(height, 0, screen.Rows - y);
        double cw = options.CellWidth, ch = options.CellHeight;

        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(N(width * cw)).Append("\" height=\"")
          .Append(N(height * ch)).Append("\" viewBox=\"0 0 ").Append(N(width * cw)).Append(' ').Append(N(height * ch))
          .Append("\" font-family=\"").Append(Escape(options.FontFamily)).Append("\" font-size=\"").Append(N(options.FontSize))
          .Append("\">\n");
        if (options.Title is { } title) sb.Append("<title>").Append(Escape(title)).Append("</title>\n");
        sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"").Append(Hex(options.DefaultBackground)).Append("\"/>\n");

        // Backgrounds: runs of equal colour per row.
        sb.Append("<g shape-rendering=\"crispEdges\">\n");
        for (int row = 0; row < height; row++)
        {
            int col = 0;
            while (col < width)
            {
                uint bg = Colors(screen.GetStyle(x + col, y + row), options).Bg;
                int start = col;
                while (col < width && Colors(screen.GetStyle(x + col, y + row), options).Bg == bg) col++;
                if (bg == options.DefaultBackground) continue;
                Rect(sb, start * cw, row * ch, (col - start) * cw, ch, bg);
            }
        }

        sb.Append("</g>\n");

        var shapes = new SortedDictionary<uint, StringBuilder>();
        var shades = new StringBuilder();
        var text = new StringBuilder();
        for (int row = 0; row < height; row++)
        {
            int col = 0;
            while (col < width)
            {
                var style = screen.GetStyle(x + col, y + row);
                string glyph = screen.GetText(x + col, y + row);
                var (fg, _) = Colors(style, options);
                if ((style.Flags & CellStyleFlags.Conceal) != 0 || glyph.Length == 0 || glyph == " ")
                {
                    col++;
                    continue;
                }

                if (options.DrawBoxCharacters && glyph is "░" or "▒" or "▓")
                {
                    // Merge runs of the same shade into one rectangle (the desktop is mostly shade characters).
                    int shadeStart = col;
                    while (col < width && screen.GetText(x + col, y + row) == glyph
                           && Colors(screen.GetStyle(x + col, y + row), options).Fg == fg
                           && (screen.GetStyle(x + col, y + row).Flags & CellStyleFlags.Conceal) == 0) col++;
                    double opacity = glyph == "░" ? 0.25 : glyph == "▒" ? 0.5 : 0.75;
                    shades.Append("<rect x=\"").Append(N(shadeStart * cw)).Append("\" y=\"").Append(N(row * ch)).Append("\" width=\"")
                          .Append(N((col - shadeStart) * cw)).Append("\" height=\"").Append(N(ch)).Append("\" fill=\"").Append(Hex(fg))
                          .Append("\" fill-opacity=\"").Append(N(opacity)).Append("\"/>\n");
                    continue;
                }

                if (options.DrawBoxCharacters && glyph.Length == 1 && TryShape(glyph[0], col * cw, row * ch, cw, ch,
                        Path(shapes, fg)))
                {
                    col++;
                    continue;
                }

                // A run of text in one style, stopping at shapes and at style changes.
                int start = col;
                var run = new StringBuilder();
                while (col < width)
                {
                    string g = screen.GetText(x + col, y + row);
                    if (screen.GetStyle(x + col, y + row) != style) break;
                    if (options.DrawBoxCharacters && g.Length == 1 && IsShape(g[0])) break;
                    if ((style.Flags & CellStyleFlags.Conceal) != 0) break;
                    run.Append(g);
                    col++;
                }

                string runText = run.ToString().TrimEnd(' ');
                if (runText.Length == 0) continue;
                int cells = CellCount(screen, x + start, y + row, runText);
                text.Append("<text x=\"").Append(N(start * cw)).Append("\" y=\"").Append(N(row * ch + ch * 0.78))
                    .Append("\" textLength=\"").Append(N(cells * cw)).Append("\" lengthAdjust=\"spacingAndGlyphs\" fill=\"")
                    .Append(Hex(fg)).Append('"');
                if ((style.Flags & CellStyleFlags.Bold) != 0) text.Append(" font-weight=\"bold\"");
                if ((style.Flags & CellStyleFlags.Italic) != 0) text.Append(" font-style=\"italic\"");
                if ((style.Flags & CellStyleFlags.Faint) != 0) text.Append(" opacity=\"0.6\"");
                string decoration = ((style.Flags & CellStyleFlags.Underline) != 0 ? "underline " : "")
                    + ((style.Flags & CellStyleFlags.Strike) != 0 ? "line-through" : "");
                if (decoration.Length > 0) text.Append(" text-decoration=\"").Append(decoration.Trim()).Append('"');
                text.Append(" xml:space=\"preserve\">").Append(Escape(runText)).Append("</text>\n");
            }
        }

        if (shades.Length > 0) sb.Append("<g shape-rendering=\"crispEdges\">\n").Append(shades).Append("</g>\n");
        foreach (var (color, path) in shapes)
        {
            sb.Append("<path fill=\"").Append(Hex(color)).Append("\" shape-rendering=\"crispEdges\" d=\"")
              .Append(path.ToString().TrimEnd()).Append("\"/>\n");
        }

        sb.Append(text);
        sb.Append("</svg>\n");
        return sb.ToString();
    }

    private static int CellCount(ScreenSnapshot screen, int x, int y, string runText)
    {
        // Count cells covered by runText: wide characters occupy a lead cell plus an empty continuation.
        int cells = 0, consumed = 0;
        while (consumed < runText.Length && x + cells < screen.Columns)
        {
            string g = screen.GetText(x + cells, y);
            consumed += g.Length;
            cells++;
            if (x + cells < screen.Columns && screen.GetRole(x + cells, y) == CellRole.WideContinuation) cells++;
        }

        return Math.Max(cells, 1);
    }

    private static bool IsShape(char c) => BoxArms.ContainsKey(c) || c is >= '\u2580' and <= '\u2595';

    private static StringBuilder Path(SortedDictionary<uint, StringBuilder> shapes, uint color)
    {
        if (!shapes.TryGetValue(color, out var path)) shapes[color] = path = new StringBuilder();
        return path;
    }

    private static bool TryShape(char c, double x, double y, double w, double h, StringBuilder path)
    {
        if (BoxArms.TryGetValue(c, out var arms))
        {
            DrawBox(arms, x, y, w, h, path);
            return true;
        }

        switch (c)
        {
            case '█': AddRect(path, x, y, w, h); return true;
            case '▀': AddRect(path, x, y, w, h / 2); return true;
            case '▐': AddRect(path, x + w / 2, y, w / 2, h); return true;
            case '▔': AddRect(path, x, y, w, h / 8); return true;
            case '▕': AddRect(path, x + w * 7 / 8, y, w / 8, h); return true;
            case >= '\u2581' and <= '\u2587':
                double lower = h * (c - '\u2580') / 8;
                AddRect(path, x, y + h - lower, w, lower);
                return true;
            case >= '\u2589' and <= '\u258f':
                AddRect(path, x, y, w * ('\u2590' - c) / 8, h);
                return true;
            default:
                return false;
        }
    }

    private static void DrawBox(string arms, double x, double y, double w, double h, StringBuilder path)
    {
        // Snap the centre and offsets to whole pixels so lines stay crisp.
        double light = Math.Max(1, Math.Round(w / 9)), heavy = light * 2;
        double cx = x + Math.Floor(w / 2) + light % 2 / 2, cy = y + Math.Floor(h / 2) + light % 2 / 2;
        double d = Math.Max(light + 1, Math.Round(w / 4.5));
        int up = arms[0] - '0', right = arms[1] - '0', down = arms[2] - '0', left = arms[3] - '0';

        // Horizontal arms (left, right) are perpendicular to the vertical ones (up, down) and vice versa.
        HorizontalArm(right, +1, up, down, left);
        HorizontalArm(left, -1, up, down, right);
        VerticalArm(down, +1, left, right, up);
        VerticalArm(up, -1, left, right, down);

        void HorizontalArm(int weight, int dir, int before, int after, int opposite)
        {
            if (weight == 0) return;
            double edge = dir > 0 ? x + w : x;
            if (weight == 3)
            {
                foreach (int s in (int[])[-1, +1])
                {
                    int sameSide = s < 0 ? before : after, otherSide = s < 0 ? after : before;
                    double start = Start(sameSide, otherSide, opposite);
                    double ly = cy + s * d;
                    HLine(path, cx + dir * start, edge, ly - light / 2, light);
                }
            }
            else
            {
                double stroke = weight == 2 ? heavy : light;
                double start = SingleStart(before, after, stroke);
                HLine(path, cx + dir * start, edge, cy - stroke / 2, stroke);
            }
        }

        void VerticalArm(int weight, int dir, int before, int after, int opposite)
        {
            if (weight == 0) return;
            double edge = dir > 0 ? y + h : y;
            if (weight == 3)
            {
                foreach (int s in (int[])[-1, +1])
                {
                    int sameSide = s < 0 ? before : after, otherSide = s < 0 ? after : before;
                    double start = Start(sameSide, otherSide, opposite);
                    double lx = cx + s * d;
                    VLine(path, cy + dir * start, edge, lx - light / 2, light);
                }
            }
            else
            {
                double stroke = weight == 2 ? heavy : light;
                double start = SingleStart(before, after, stroke);
                VLine(path, cy + dir * start, edge, cx - stroke / 2, stroke);
            }
        }

        // Where a line of a double arm starts, as an offset from the centre towards the arm.
        double Start(int sameSide, int otherSide, int opposite) =>
            sameSide == 3 ? d - light / 2
            : sameSide != 0 ? -light / 2
            : opposite != 0 ? -light / 2
            : otherSide == 3 ? -d - light / 2
            : -light / 2;

        // Where a single (light or heavy) arm starts.
        double SingleStart(int before, int after, double stroke) =>
            before == 3 && after == 3 ? d - light / 2
            : before == 3 || after == 3 ? -d - light / 2
            : -stroke / 2;
    }

    private static void HLine(StringBuilder path, double x1, double x2, double top, double stroke) =>
        AddRect(path, Math.Min(x1, x2), top, Math.Abs(x2 - x1), stroke);

    private static void VLine(StringBuilder path, double y1, double y2, double left, double stroke) =>
        AddRect(path, left, Math.Min(y1, y2), stroke, Math.Abs(y2 - y1));

    private static void AddRect(StringBuilder path, double x, double y, double w, double h)
    {
        if (w <= 0 || h <= 0) return;
        path.Append('M').Append(N(x)).Append(' ').Append(N(y)).Append('h').Append(N(w)).Append('v').Append(N(h))
            .Append('h').Append(N(-w)).Append("z ");
    }

    private static void Rect(StringBuilder sb, double x, double y, double w, double h, uint color) =>
        sb.Append("<rect x=\"").Append(N(x)).Append("\" y=\"").Append(N(y)).Append("\" width=\"").Append(N(w))
          .Append("\" height=\"").Append(N(h)).Append("\" fill=\"").Append(Hex(color)).Append("\"/>\n");

    internal static (uint Fg, uint Bg) Colors(CellStyle style, SvgOptions options)
    {
        bool bold = (style.Flags & CellStyleFlags.Bold) != 0;
        uint fg = Resolve(style.Foreground, options, options.DefaultForeground, options.BoldIsBright && bold);
        uint bg = Resolve(style.Background, options, options.DefaultBackground, bright: false);
        return (style.Flags & CellStyleFlags.Inverse) != 0 ? (bg, fg) : (fg, bg);
    }

    private static uint Resolve(TerminalColor color, SvgOptions options, uint fallback, bool bright)
    {
        switch (color.Kind)
        {
            case TerminalColorKind.Rgb:
                return color.Value & 0xffffff;
            case TerminalColorKind.Indexed:
                int index = (int)color.Value;
                if (index < 8) return options.Palette[bright ? index + 8 : index];
                if (index < 16) return options.Palette[index];
                if (index >= 232)
                {
                    uint grey = (uint)(8 + (index - 232) * 10);
                    return grey << 16 | grey << 8 | grey;
                }

                index -= 16;
                return Cube(index / 36) << 16 | Cube(index / 6 % 6) << 8 | Cube(index % 6);
            default:
                return fallback;
        }
    }

    private static uint Cube(int level) => level == 0 ? 0u : (uint)(55 + level * 40);

    private static string Hex(uint rgb) => "#" + (rgb & 0xffffff).ToString("x6", CultureInfo.InvariantCulture);

    private static string N(double value) => Math.Round(value, 2).ToString(CultureInfo.InvariantCulture);

    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            sb.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                _ => c.ToString(),
            });
        }

        return sb.ToString();
    }
}
