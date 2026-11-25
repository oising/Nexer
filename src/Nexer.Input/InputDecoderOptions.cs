namespace Nexer.Input;

/// <summary>Tuning for <see cref="InputDecoder"/>.</summary>
public sealed record InputDecoderOptions
{
    /// <summary>
    /// How long an incomplete sequence (a lone ESC, <c>ESC [</c>, <c>ESC O</c>, ...) may wait for its next byte
    /// before it is resolved as typed keys (Escape, or Alt+character). Default 50 ms.
    /// </summary>
    public TimeSpan EscapeTimeout { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Bracketed paste is delivered in <see cref="PasteEvent"/> chunks of at most about this many bytes so a huge
    /// paste is never buffered whole. Default 256 KiB.
    /// </summary>
    public int MaxPasteChunkBytes { get; init; } = 256 * 1024;

    /// <summary>
    /// When true (default; xterm, Windows Terminal), 0x08 is Ctrl+Backspace and 0x7f is Backspace.
    /// When false, both are plain Backspace.
    /// </summary>
    public bool ControlHIsControlBackspace { get; init; } = true;
}
