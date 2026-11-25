// Ported from Jexer bits/ColorTheme.java (MIT, © Autumn Lamonte).
using System.Diagnostics.CodeAnalysis;
using Nexer.Core;

namespace Nexer.Ui;

/// <summary>
/// Named colours keyed by Jexer's theme keys (e.g. <c>"twindow.border"</c>). A new theme holds Jexer's defaults.
/// </summary>
public partial class ColorTheme
{
    private readonly Dictionary<string, CellStyle> colors = new(StringComparer.Ordinal);

    /// <summary>Creates a theme populated with Jexer's default colours.</summary>
    public ColorTheme()
    {
        SetDefaultTheme();
    }

    /// <summary>Shared default theme instance (do not mutate; clone it instead).</summary>
    public static ColorTheme Default => DefaultHolder.Instance;

    private static class DefaultHolder
    {
        public static readonly ColorTheme Instance = new();
    }

    /// <summary>All keys, sorted.</summary>
    public IReadOnlyList<string> Keys => [.. colors.Keys.Order(StringComparer.Ordinal)];

    /// <summary>Gets or sets a colour by key. Unknown keys return Jexer's default attributes (white on black).</summary>
    public CellStyle this[string key]
    {
        get => GetColor(key);
        set => SetColor(key, value);
    }

    /// <summary>Returns the colour for <paramref name="key"/>; unknown keys yield white on black.</summary>
    public CellStyle GetColor(string key) =>
        colors.TryGetValue(key, out var style) ? style : new CellStyle(TerminalColor.Indexed(7), TerminalColor.Indexed(0));

    /// <summary>Looks up a colour.</summary>
    public bool TryGetColor(string key, [MaybeNullWhen(false)] out CellStyle style) => colors.TryGetValue(key, out style);

    /// <summary>Sets a colour.</summary>
    public void SetColor(string key, CellStyle style)
    {
        ArgumentNullException.ThrowIfNull(key);
        colors[key] = style;
    }

    /// <summary>Sets a colour from <see cref="StyleSyntax"/> text such as <c>"bold white on blue"</c>.</summary>
    public void SetColor(string key, string spec) => SetColor(key, StyleSyntax.Parse(spec));

    /// <summary>Resets to Jexer's default theme.</summary>
    public void SetDefaultTheme()
    {
        colors.Clear();
        foreach (var (key, spec) in DefaultColorSpecs)
        {
            colors[key] = StyleSyntax.Parse(spec);
        }
    }

    /// <summary>Loads <c>key = spec</c> lines (Jexer's theme file format). Invalid lines are ignored as in Jexer.</summary>
    public void Load(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        for (var line = reader.ReadLine(); line != null; line = reader.ReadLine())
        {
            int eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0) continue;
            if (StyleSyntax.TryParse(line[(eq + 1)..], out var style)) colors[line[..eq].Trim()] = style;
        }
    }

    /// <summary>Returns a copy of this theme.</summary>
    public ColorTheme Clone()
    {
        var copy = new ColorTheme();
        copy.colors.Clear();
        foreach (var (k, v) in colors) copy.colors[k] = v;
        return copy;
    }
}
