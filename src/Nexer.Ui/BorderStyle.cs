// Ported from Jexer bits/BorderStyle.java (MIT, © Autumn Lamonte).
using System.Text;

namespace Nexer.Ui;

/// <summary>Glyph set for a box border.</summary>
/// <remarks>XAML text form: <c>none</c>, <c>default</c>, <c>single</c>, <c>double</c>, <c>round</c>,
/// <c>singleVdoubleH</c>, <c>singleHdoubleV</c> (case-insensitive).</remarks>
public sealed class BorderStyle
{
    private BorderStyle(string name, char horizontal, char vertical, char topLeft, char topRight, char bottomLeft, char bottomRight)
    {
        Name = name;
        Horizontal = new Rune(horizontal);
        Vertical = new Rune(vertical);
        TopLeft = new Rune(topLeft);
        TopRight = new Rune(topRight);
        BottomLeft = new Rune(bottomLeft);
        BottomRight = new Rune(bottomRight);
    }

    /// <summary>No border (spaces).</summary>
    public static BorderStyle None { get; } = new("none", ' ', ' ', ' ', ' ', ' ', ' ');

    /// <summary>Single lines.</summary>
    public static BorderStyle Single { get; } = new("single", GraphicsChars.SingleBar, GraphicsChars.WindowSide,
        GraphicsChars.ULCorner, GraphicsChars.URCorner, GraphicsChars.LLCorner, GraphicsChars.LRCorner);

    /// <summary>Double lines.</summary>
    public static BorderStyle Double { get; } = new("double", GraphicsChars.DoubleBar, GraphicsChars.WindowSideDouble,
        GraphicsChars.WindowLeftTopDouble, GraphicsChars.WindowRightTopDouble,
        GraphicsChars.WindowLeftBottomDouble, GraphicsChars.WindowRightBottomDouble);

    /// <summary>Single vertical, double horizontal lines.</summary>
    public static BorderStyle SingleVDoubleH { get; } = new("singleVdoubleH", GraphicsChars.WindowTop, GraphicsChars.WindowSide,
        GraphicsChars.WindowLeftTop, GraphicsChars.WindowRightTop, GraphicsChars.WindowLeftBottom, GraphicsChars.WindowRightBottom);

    /// <summary>Single horizontal, double vertical lines.</summary>
    public static BorderStyle SingleHDoubleV { get; } = new("singleHdoubleV", GraphicsChars.SingleBar, GraphicsChars.WindowSideDouble,
        '\u2553', '\u2556', '\u2559', '\u255C');

    /// <summary>Single lines with rounded corners.</summary>
    public static BorderStyle Round { get; } = new("round", GraphicsChars.SingleBar, GraphicsChars.WindowSide,
        '\u256D', '\u256E', '\u2570', '\u256F');

    /// <summary>Style name.</summary>
    public string Name { get; }

#pragma warning disable CS1591
    public Rune Horizontal { get; }
    public Rune Vertical { get; }
    public Rune TopLeft { get; }
    public Rune TopRight { get; }
    public Rune BottomLeft { get; }
    public Rune BottomRight { get; }
#pragma warning restore CS1591

    /// <summary>Parses a style name; unknown names map to <see cref="Single"/> as in Jexer.</summary>
    public static BorderStyle Parse(string text) => (text ?? "").Trim().ToLowerInvariant() switch
    {
        "none" => None,
        "double" => Double,
        "round" => Round,
        "singlevdoubleh" => SingleVDoubleH,
        "singlehdoublev" => SingleHDoubleV,
        _ => Single,
    };

    /// <inheritdoc/>
    public override string ToString() => Name;
}
