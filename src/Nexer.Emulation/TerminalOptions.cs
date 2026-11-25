namespace Nexer.Emulation;

public sealed record TerminalOptions
{
    public bool AmbiguousIsWide { get; init; }
    public int MaxCells { get; init; } = 1_000_000;
    public int MaxClusterBytes { get; init; } = 1_048_576;
    public int MaxControlStringBytes { get; init; } = 4096;
}
