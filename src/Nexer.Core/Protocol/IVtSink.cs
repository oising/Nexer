using System.Text;

namespace Nexer.Core.Protocol;

public enum VtSequenceKind : byte
{
    Escape,
    Csi
}

public enum VtStringKind : byte
{
    Osc,
    Dcs,
    Sos,
    Pm,
    Apc
}

/// <summary>Callbacks are synchronous. All spans are borrowed only for the duration of the callback.</summary>
public interface IVtSink
{
    /// <summary>A text barrier, including introductions/cancellation of sequences that may be discarded.</summary>
    void Boundary() { }
    void Print(Rune value);
    void Execute(byte control);
    void Dispatch(VtSequenceKind kind, ReadOnlySpan<byte> parameters, ReadOnlySpan<byte> intermediates, byte final);
    void String(VtStringKind kind, ReadOnlySpan<byte> payload);
}
