using System.Globalization;

namespace Nexer.Ui;

/// <summary>An integer cell rectangle. <see cref="Right"/> and <see cref="Bottom"/> are exclusive.</summary>
/// <remarks>XAML text form: <c>"x,y,width,height"</c> (commas and/or spaces).</remarks>
public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    /// <summary>Exclusive right edge.</summary>
    public int Right => X + Width;

    /// <summary>Exclusive bottom edge.</summary>
    public int Bottom => Y + Height;

    /// <summary>True when the rectangle covers no cells.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>True when (x, y) lies inside the rectangle.</summary>
    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>Returns the intersection (empty rectangles normalise to zero size).</summary>
    public Rect Intersect(Rect other)
    {
        int left = Math.Max(X, other.X);
        int top = Math.Max(Y, other.Y);
        int right = Math.Min(Right, other.Right);
        int bottom = Math.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top ? new Rect(left, top, 0, 0) : new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>True when the two rectangles share at least one cell.</summary>
    public bool IntersectsWith(Rect other) => !Intersect(other).IsEmpty;

    /// <summary>Returns the rectangle moved by (dx, dy).</summary>
    public Rect Offset(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    /// <summary>Parses <c>"x,y,width,height"</c>.</summary>
    public static Rect Parse(string text)
    {
        var parts = ParseHelpers.SplitInts(text, nameof(Rect));
        if (parts.Length != 4) throw new FormatException($"Rect expects 'x,y,width,height', got '{text}'.");
        return new Rect(parts[0], parts[1], parts[2], parts[3]);
    }

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{X},{Y},{Width},{Height}");
}

/// <summary>Thickness of four edges in cells.</summary>
/// <remarks>XAML text form: <c>"uniform"</c>, <c>"horizontal,vertical"</c> or <c>"left,top,right,bottom"</c>.</remarks>
public readonly record struct Thickness(int Left, int Top, int Right, int Bottom)
{
    /// <summary>Creates a uniform thickness.</summary>
    public Thickness(int uniform) : this(uniform, uniform, uniform, uniform) { }

    /// <summary>Parses 1, 2 or 4 integers.</summary>
    public static Thickness Parse(string text)
    {
        var p = ParseHelpers.SplitInts(text, nameof(Thickness));
        return p.Length switch
        {
            1 => new Thickness(p[0]),
            2 => new Thickness(p[0], p[1], p[0], p[1]),
            4 => new Thickness(p[0], p[1], p[2], p[3]),
            _ => throw new FormatException($"Thickness expects 1, 2 or 4 values, got '{text}'."),
        };
    }

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Left},{Top},{Right},{Bottom}");
}

internal static class ParseHelpers
{
    public static int[] SplitInts(string text, string type)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = text.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var result = new int[tokens.Length];
        for (int i = 0; i < tokens.Length; i++)
        {
            if (!int.TryParse(tokens[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out result[i]))
            {
                throw new FormatException($"Invalid {type} component '{tokens[i]}' in '{text}'.");
            }
        }

        return result;
    }
}
