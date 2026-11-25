using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Nexer.Core;

namespace Nexer.Ui;

// CONTRACT: shared by themes (Nexer.Ui) and the compile-time value converter (Nexer.Xaml.Compiler).
// Changes go through the coordinator; see docs\architecture.md.

/// <summary>
/// Parses Jexer's colour syntax into a <see cref="CellStyle"/>:
/// <c>[bold|bright|blink|underline|reverse|italic ...] &lt;fg&gt; on &lt;bg&gt;</c>, and <c>rgb: #RRGGBB on #RRGGBB</c>.
/// Colours: black, red, green, yellow (alias brown), blue, magenta, cyan, white, default, or #RRGGBB.
/// Case-insensitive. Unlike Jexer, invalid text is an error rather than silently ignored.
/// </summary>
/// <remarks>As in Jexer, "bold"/"bright" set <see cref="CellStyleFlags.Bold"/> and keep the base palette index.</remarks>
public static class StyleSyntax
{
    public static CellStyle Parse(string text) =>
        TryParse(text, out var style, out var error) ? style : throw new FormatException(error);

    public static bool TryParse(string? text, out CellStyle style) => TryParse(text, out style, out _);

    public static bool TryParse(string? text, out CellStyle style, [NotNullWhen(false)] out string? error)
    {
        style = default;
        var tokens = (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var i = 0;
        var flags = CellStyleFlags.None;
        var rgbOnly = false;

        if (tokens.Length > 0 && tokens[0].Equals("rgb:", StringComparison.OrdinalIgnoreCase))
        {
            rgbOnly = true;
            i++;
        }

        for (; !rgbOnly && i < tokens.Length; i++)
        {
            var flag = tokens[i].ToLowerInvariant() switch
            {
                "bold" or "bright" => CellStyleFlags.Bold,
                "blink" => CellStyleFlags.Blink,
                "underline" => CellStyleFlags.Underline,
                "reverse" => CellStyleFlags.Inverse,
                "italic" => CellStyleFlags.Italic,
                _ => CellStyleFlags.None,
            };
            if (flag == CellStyleFlags.None)
            {
                break;
            }

            flags |= flag;
        }

        if (tokens.Length - i != 3 || !tokens[i + 1].Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            error = $"Invalid style '{text}': expected '[modifiers] <foreground> on <background>'.";
            return false;
        }

        if (!TryParseColor(tokens[i], rgbOnly, out var fg) || !TryParseColor(tokens[i + 2], rgbOnly, out var bg))
        {
            error = $"Invalid style '{text}': unknown colour.";
            return false;
        }

        style = new CellStyle(fg, bg, flags);
        error = null;
        return true;
    }

    public static bool TryParseColor(string text, out TerminalColor color) => TryParseColor(text, false, out color);

    private static bool TryParseColor(string text, bool rgbOnly, out TerminalColor color)
    {
        color = default;
        var hex = text.TrimStart('#');
        if (text.StartsWith('#') || rgbOnly)
        {
            if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
            {
                return false;
            }

            color = TerminalColor.Rgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            return true;
        }

        int? index = text.ToLowerInvariant() switch
        {
            "black" => 0,
            "red" => 1,
            "green" => 2,
            "yellow" or "brown" => 3,
            "blue" => 4,
            "magenta" => 5,
            "cyan" => 6,
            "white" => 7,
            "default" => -1,
            _ => null,
        };
        if (index is null)
        {
            return false;
        }

        color = index < 0 ? default : TerminalColor.Indexed((byte)index.Value);
        return true;
    }
}
