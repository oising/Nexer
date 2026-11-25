using System.Text;

namespace Nexer.Core;

[Flags]
public enum CellStyleFlags : ushort
{
    None = 0,
    Bold = 1,
    Faint = 2,
    Italic = 4,
    Underline = 8,
    Blink = 16,
    Inverse = 32,
    Conceal = 64,
    Strike = 128
}

public readonly record struct CellStyle(
    TerminalColor Foreground = default,
    TerminalColor Background = default,
    CellStyleFlags Flags = CellStyleFlags.None);

public enum CellRole : byte
{
    Single,
    WideLead,
    WideContinuation
}

public readonly record struct Cell
{
    private readonly int scalar;

    /// <summary>The first scalar; resolve full cluster text through the owning surface.</summary>
    public Rune Glyph => scalar == 0 ? new Rune(' ') : new Rune(scalar);
    public bool HasCluster => ClusterId != 0;
    internal int ClusterId { get; }
    public CellStyle Style { get; }
    public CellRole Role { get; }
    public static Cell Blank => default;

    private Cell(Rune glyph, CellStyle style, CellRole role, int clusterId = 0)
    {
        if (Rune.IsControl(glyph))
        {
            throw new ArgumentException("Control characters cannot be stored in cells.", nameof(glyph));
        }

        scalar = glyph.Value == ' ' ? 0 : glyph.Value;
        Style = style;
        Role = role;
        ClusterId = clusterId;
    }

    public static Cell Create(Rune glyph, CellStyle style = default) =>
        new(glyph, style, CellRole.Single);

    internal static Cell Lead(Rune glyph, CellStyle style) => new(glyph, style, CellRole.WideLead);
    internal static Cell Continuation(CellStyle style) => new(new Rune(' '), style, CellRole.WideContinuation);
    internal static Cell Cluster(Rune first, int id, CellStyle style, int width) =>
        new(first, style, width == 2 ? CellRole.WideLead : CellRole.Single, id);
}
