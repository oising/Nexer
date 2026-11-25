namespace Nexer.Hosting.Platform;

/// <summary>
/// The OS-specific part of a console session: terminal modes, raw byte I/O and size. Escape sequences that
/// configure the terminal itself (alternate screen, mouse, ...) are written by <see cref="ConsoleTerminalHost"/>.
/// </summary>
internal interface ITerminalPlatform
{
    /// <summary>Captures the current terminal modes and switches to raw VT input/output.</summary>
    void Enter();

    /// <summary>Restores exactly what <see cref="Enter"/> captured. Idempotent; safe from any thread.</summary>
    void Restore();

    /// <summary>Current window size in cells.</summary>
    TerminalSize GetSize();

    /// <summary>Writes all bytes to the terminal (blocking).</summary>
    void Write(ReadOnlySpan<byte> bytes);

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for input and copies available UTF-8 bytes into
    /// <paramref name="buffer"/> (at least 1024 bytes). Returns the byte count (0 if none arrived in time)
    /// or -1 at end of input.
    /// </summary>
    int Read(Span<byte> buffer, TimeSpan timeout);
}
