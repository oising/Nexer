namespace Nexer.Rendering;

/// <summary>
/// An encoded frame prepared by <see cref="VtRenderer.Prepare"/>. Write <see cref="Bytes"/> to the terminal,
/// then call <see cref="Commit"/> after the complete write succeeded. Disposing without committing discards the
/// frame and leaves the renderer baseline unchanged.
/// </summary>
/// <remarks>Only one frame per renderer may be in flight. <see cref="Bytes"/> is valid until commit or disposal.</remarks>
public readonly struct RenderFrame : IDisposable
{
    private readonly VtRenderer? renderer;
    private readonly long token;

    internal RenderFrame(VtRenderer renderer, long token)
    {
        this.renderer = renderer;
        this.token = token;
    }

    /// <summary>The VT bytes to write. Empty when nothing visible changed.</summary>
    public ReadOnlyMemory<byte> Bytes => renderer is null ? default : renderer.GetFrameBytes(token);

    /// <summary>True when there is nothing to write (the frame may still be committed to acknowledge damage).</summary>
    public bool IsEmpty => Bytes.IsEmpty;

    /// <summary>Installs the frame's state as the renderer baseline. Call only after a complete, successful write.</summary>
    public void Commit()
    {
        if (renderer is null) throw new InvalidOperationException("Default frame cannot be committed.");
        renderer.Commit(token);
    }

    /// <summary>Discards the frame if it has not been committed.</summary>
    public void Dispose() => renderer?.Discard(token);
}
