using Nexer.Input;
using Nexer.Rendering;

namespace Nexer.Hosting;

/// <summary>Configuration for <see cref="ConsoleTerminalHost"/>.</summary>
public sealed record ConsoleTerminalHostOptions
{
    /// <summary>Clock for timestamps, ESC disambiguation and frame pacing. Default <see cref="TimeProvider.System"/>.</summary>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>Use the alternate screen (DEC 1049). Default true.</summary>
    public bool AlternateScreen { get; init; } = true;

    /// <summary>Report mouse buttons, drags and wheel (DEC 1000/1002 with SGR 1006 encoding). Default true.</summary>
    public bool Mouse { get; init; } = true;

    /// <summary>Also report motion with no button held (DEC 1003); requires <see cref="Mouse"/>. Default false.</summary>
    public bool MouseMotion { get; init; }

    /// <summary>Report focus changes (DEC 1004). Default true.</summary>
    public bool FocusEvents { get; init; } = true;

    /// <summary>Bracketed paste (DEC 2004). Default true.</summary>
    public bool BracketedPaste { get; init; } = true;

    /// <summary>Wrap frames in synchronized output (DEC 2026). Terminals without support ignore it. Default true.</summary>
    public bool SynchronizedOutput { get; init; } = true;

    /// <summary>Color depth sent to the terminal. Default <see cref="ColorMode.TrueColor"/>.</summary>
    public ColorMode Colors { get; init; } = ColorMode.TrueColor;

    /// <summary>Minimum time between two frames; presents in between are coalesced. Default 16 ms.</summary>
    public TimeSpan MinFrameInterval { get; init; } = TimeSpan.FromMilliseconds(16);

    /// <summary>Capacity of the <see cref="ITerminalHost.Input"/> channel. The reader blocks (never drops) when full. Default 1024.</summary>
    public int InputCapacity { get; init; } = 1024;

    /// <summary>Input decoder tuning (ESC timeout, paste chunking).</summary>
    public InputDecoderOptions? Input { get; init; }
}
