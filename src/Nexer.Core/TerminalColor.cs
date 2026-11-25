namespace Nexer.Core;

public enum TerminalColorKind : byte
{
    Default,
    Indexed,
    Rgb
}

public readonly record struct TerminalColor
{
    public TerminalColorKind Kind { get; }
    public uint Value { get; }

    private TerminalColor(TerminalColorKind kind, uint value)
    {
        Kind = kind;
        Value = value;
    }

    public static TerminalColor Default => default;

    public static TerminalColor Indexed(byte index) => new(TerminalColorKind.Indexed, index);

    public static TerminalColor Rgb(byte red, byte green, byte blue) =>
        new(TerminalColorKind.Rgb, (uint)(red << 16 | green << 8 | blue));
}
