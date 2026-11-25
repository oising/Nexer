using System.Threading.Channels;
using Nexer.Core;
using Nexer.Input;

namespace Nexer.Hosting;

// CONTRACT: implemented by Nexer.Hosting (console) and Nexer.Ui.Testing (headless);
// consumed by Nexer.Ui. Changes go through the coordinator; see docs\architecture.md.

public readonly record struct TerminalSize(int Columns, int Rows);

/// <summary>Requested hardware cursor. Zero-based cell coordinates.</summary>
public readonly record struct CursorState(bool Visible, int Column, int Row)
{
    public static CursorState Hidden => default;
}

/// <summary>A terminal the UI draws to and receives input from.</summary>
/// <remarks>
/// Threading: <see cref="Present"/> must be called from a single thread (the UI thread).
/// <see cref="Input"/> may be read from any one consumer.
/// </remarks>
public interface ITerminalHost : IAsyncDisposable
{
    /// <summary>Current size. Changes are also delivered as <see cref="ResizeEvent"/> on <see cref="Input"/>.</summary>
    TerminalSize Size { get; }

    /// <summary>Decoded input. Completes when the host input ends (EOF, disposal, or fault).</summary>
    ChannelReader<InputEvent> Input { get; }

    /// <summary>Clock used for event timestamps, ESC disambiguation and frame scheduling.</summary>
    TimeProvider Time { get; }

    /// <summary>
    /// Requests that <paramref name="surface"/> and <paramref name="cursor"/> be displayed.
    /// The host captures everything it needs from <paramref name="surface"/> before returning,
    /// so the caller may mutate it immediately afterwards. Requests may be coalesced; the most
    /// recently presented state is eventually displayed. The surface size should match <see cref="Size"/>;
    /// a mismatched surface is clipped or padded, never an error.
    /// </summary>
    void Present(IReadOnlyCellSurface surface, CursorState cursor);
}
