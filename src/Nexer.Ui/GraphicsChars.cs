// Ported from Jexer bits/GraphicsChars.java (MIT, © Autumn Lamonte).
namespace Nexer.Ui;

/// <summary>Box-drawing and CP437 glyphs used by the widgets (Jexer's <c>GraphicsChars</c>).</summary>
public static class GraphicsChars
{
    /// <summary>CP437 mapped to Unicode (index 0 is U+2007 figure space, as Jexer).</summary>
    public static IReadOnlyList<char> CP437 { get; } =
    [
        '\u2007', '\u263A', '\u263B', '\u2665', '\u2666', '\u2663', '\u2660', '\u2022',
        '\u25D8', '\u25CB', '\u25D9', '\u2642', '\u2640', '\u266A', '\u266B', '\u263C',
        '\u25B6', '\u25C0', '\u2195', '\u203C', '\u00B6', '\u00A7', '\u25AC', '\u21A8',
        '\u2191', '\u2193', '\u2192', '\u2190', '\u221F', '\u2194', '\u25B2', '\u25BC',
        ' ', '!', '"', '#', '$', '%', '&', '\'', '(', ')', '*', '+', ',', '-', '.', '/',
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', ':', ';', '<', '=', '>', '?',
        '@', 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O',
        'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', '[', '\\', ']', '^', '_',
        '`', 'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o',
        'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z', '{', '|', '}', '~', '\u2302',
        '\u00c7', '\u00fc', '\u00e9', '\u00e2', '\u00e4', '\u00e0', '\u00e5', '\u00e7',
        '\u00ea', '\u00eb', '\u00e8', '\u00ef', '\u00ee', '\u00ec', '\u00c4', '\u00c5',
        '\u00c9', '\u00e6', '\u00c6', '\u00f4', '\u00f6', '\u00f2', '\u00fb', '\u00f9',
        '\u00ff', '\u00d6', '\u00dc', '\u00a2', '\u00a3', '\u00a5', '\u20a7', '\u0192',
        '\u00e1', '\u00ed', '\u00f3', '\u00fa', '\u00f1', '\u00d1', '\u00aa', '\u00ba',
        '\u00bf', '\u2310', '\u00ac', '\u00bd', '\u00bc', '\u00a1', '\u00ab', '\u00bb',
        '\u2591', '\u2592', '\u2593', '\u2502', '\u2524', '\u2561', '\u2562', '\u2556',
        '\u2555', '\u2563', '\u2551', '\u2557', '\u255d', '\u255c', '\u255b', '\u2510',
        '\u2514', '\u2534', '\u252c', '\u251c', '\u2500', '\u253c', '\u255e', '\u255f',
        '\u255a', '\u2554', '\u2569', '\u2566', '\u2560', '\u2550', '\u256c', '\u2567',
        '\u2568', '\u2564', '\u2565', '\u2559', '\u2558', '\u2552', '\u2553', '\u256b',
        '\u256a', '\u2518', '\u250c', '\u2588', '\u2584', '\u258c', '\u2590', '\u2580',
        '\u03b1', '\u00df', '\u0393', '\u03c0', '\u03a3', '\u03c3', '\u00b5', '\u03c4',
        '\u03a6', '\u0398', '\u03a9', '\u03b4', '\u221e', '\u03c6', '\u03b5', '\u2229',
        '\u2261', '\u00b1', '\u2265', '\u2264', '\u2320', '\u2321', '\u00f7', '\u2248',
        '\u00b0', '\u2219', '\u00b7', '\u221a', '\u207f', '\u00b2', '\u25a0', '\u00a0',
    ];

#pragma warning disable CS1591 // Glyph names are self-describing (see Jexer GraphicsChars).
    public const char Hatch = '\u2591';
    public const char DoubleBar = '\u2550';
    public const char Box = '\u25a0';
    public const char Check = '\u221a';
    public const char Triplet = '\u2261';
    public const char Omega = '\u03a9';
    public const char Pi = '\u03c0';
    public const char UpArrow = '\u2191';
    public const char DownArrow = '\u2193';
    public const char RightArrow = '\u2192';
    public const char LeftArrow = '\u2190';
    public const char SingleBar = '\u2500';
    public const char BackArrowhead = '\u25C0';
    public const char LRCorner = '\u2518';
    public const char URCorner = '\u2510';
    public const char LLCorner = '\u2514';
    public const char ULCorner = '\u250c';
    public const char Degree = '\u00b0';
    public const char PlusMinus = '\u00b1';
    public const char WindowTop = '\u2550';
    public const char WindowLeftTop = '\u2552';
    public const char WindowRightTop = '\u2555';
    public const char WindowSide = '\u2502';
    public const char WindowLeftBottom = '\u2558';
    public const char WindowRightBottom = '\u255b';
    public const char WindowLeftTee = '\u255e';
    public const char WindowRightTee = '\u2561';
    public const char WindowSideDouble = '\u2551';
    public const char WindowLeftTopDouble = '\u2554';
    public const char WindowRightTopDouble = '\u2557';
    public const char WindowLeftBottomDouble = '\u255a';
    public const char WindowRightBottomDouble = '\u255d';
    public const char VerticalBar = '\u2502';
    public const char Octostar = '\u263C';
    public const char DownArrowLeft = '\u258c';
    public const char DownArrowRight = '\u2590';
    public const char UpDownArrow = '\u2195';
    public const char RightTriangle = '\u25B6';
    public const char LeftTriangle = '\u25C0';
    public const char UpTriangle = '\u25B2';
    public const char DownTriangle = '\u25BC';
    public const char Bullet = '\u2022';
    public const char HalfTone = '\u2592';
    public const char FullBlock = '\u2588';
#pragma warning restore CS1591
}
