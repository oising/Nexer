namespace Nexer.Rendering;

/// <summary>Colour depth the host terminal can display. Cells are projected to it before diffing.</summary>
public enum ColorMode : byte
{
    /// <summary>24-bit RGB (<c>38;2;r;g;b</c>) plus 256/16 indexed colours.</summary>
    TrueColor,

    /// <summary>xterm 256-colour palette; RGB is quantized to the nearest palette entry.</summary>
    Indexed256,

    /// <summary>The 16 ANSI colours only.</summary>
    Indexed16,
}

/// <summary>Immutable renderer configuration.</summary>
public sealed record RendererOptions
{
    /// <summary>Wraps frames that change content in DEC mode 2026 (synchronized output).</summary>
    public bool SynchronizedOutput { get; init; }

    /// <summary>Colour depth of the host terminal.</summary>
    public ColorMode Colors { get; init; } = ColorMode.TrueColor;

    /// <summary>Width policy for East Asian ambiguous characters; must match the drawing code.</summary>
    public bool AmbiguousIsWide { get; init; }
}

/// <summary>Requested hardware cursor in zero-based cell coordinates.</summary>
public readonly record struct RenderCursor(bool Visible, int Column, int Row)
{
    /// <summary>A hidden cursor.</summary>
    public static RenderCursor Hidden => default;
}
