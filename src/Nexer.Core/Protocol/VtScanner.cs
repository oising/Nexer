using System.Buffers;
using System.Text;

namespace Nexer.Core.Protocol;

/// <summary>A single-owner UTF-8/seven-bit VT scanner, independent of input/output sequence semantics.</summary>
public sealed class VtScanner
{
    private enum State : byte
    {
        Ground,
        Escape,
        EscapeIntermediate,
        Csi,
        CsiIntermediate,
        IgnoreSequence,
        String
    }

    private readonly IVtSink sink;
    private readonly byte[] header;
    private readonly byte[] payload;
    private readonly byte[] utf8 = new byte[4];
    private State state;
    private VtSequenceKind sequenceKind;
    private VtStringKind stringKind;
    private int headerLength;
    private int parameterLength;
    private int payloadLength;
    private int utf8Length;
    private bool stringEscape;
    private bool stringOverflow;
    private bool active;

    public VtScanner(IVtSink sink, int maxSequenceBytes = 128, int maxStringBytes = 4096)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSequenceBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxSequenceBytes, 1_048_576);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStringBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxStringBytes, 1_048_576);
        this.sink = sink;
        header = new byte[maxSequenceBytes];
        payload = new byte[maxStringBytes];
    }

    public void Feed(ReadOnlySpan<byte> bytes)
    {
        Enter();
        try
        {
            foreach (byte value in bytes)
            {
                Consume(value);
            }
        }
        finally
        {
            active = false;
        }
    }

    /// <summary>Ends the current stream: replaces incomplete UTF-8 and discards incomplete VT sequences.</summary>
    public void Complete()
    {
        Enter();
        try
        {
            FlushUtf8();
            Reset();
        }
        finally
        {
            active = false;
        }
    }

    private void Enter()
    {
        if (active)
        {
            throw new InvalidOperationException("Scanner callbacks must not reenter the scanner.");
        }

        active = true;
    }

    private void Consume(byte value)
    {
        if (value is 0x18 or 0x1a)
        {
            FlushUtf8();
            sink.Boundary();
            Reset();
            return;
        }

        if (state == State.String)
        {
            ConsumeString(value);
            return;
        }

        if (value == 0x1b)
        {
            FlushUtf8();
            sink.Boundary();
            Reset();
            state = State.Escape;
            sequenceKind = VtSequenceKind.Escape;
            return;
        }

        if (value < 0x20)
        {
            FlushUtf8();
            sink.Execute(value);
            return;
        }

        if (value == 0x7f)
        {
            FlushUtf8();
            sink.Boundary();
            return;
        }

        switch (state)
        {
            case State.Ground:
                PrintUtf8(value);
                break;
            case State.Escape:
                switch (value)
                {
                    case (byte)'[':
                        sequenceKind = VtSequenceKind.Csi;
                        state = State.Csi;
                        return;
                    case (byte)']': BeginString(VtStringKind.Osc); return;
                    case (byte)'P': BeginString(VtStringKind.Dcs); return;
                    case (byte)'X': BeginString(VtStringKind.Sos); return;
                    case (byte)'^': BeginString(VtStringKind.Pm); return;
                    case (byte)'_': BeginString(VtStringKind.Apc); return;
                }

                ConsumeHeader(value, escape: true);
                break;
            case State.EscapeIntermediate:
                ConsumeHeader(value, escape: true);
                break;
            case State.Csi:
            case State.CsiIntermediate:
                ConsumeHeader(value, escape: false);
                break;
            case State.IgnoreSequence:
                if (IsFinal(value))
                {
                    Reset();
                }
                break;
        }
    }

    private void ConsumeHeader(byte value, bool escape)
    {
        if (value is >= 0x20 and <= 0x2f)
        {
            if (AppendHeader(value))
            {
                state = escape ? State.EscapeIntermediate : State.CsiIntermediate;
            }
        }
        else if (!escape && value is >= 0x30 and <= 0x3f && state == State.Csi)
        {
            if (AppendHeader(value))
            {
                parameterLength++;
            }
        }
        else if (IsFinal(value))
        {
            sink.Dispatch(sequenceKind, header.AsSpan(0, parameterLength),
                header.AsSpan(parameterLength, headerLength - parameterLength), value);
            Reset();
        }
        else
        {
            state = State.IgnoreSequence;
        }
    }

    private bool IsFinal(byte value) =>
        value >= (sequenceKind == VtSequenceKind.Escape ? 0x30 : 0x40) && value <= 0x7e;

    private bool AppendHeader(byte value)
    {
        if (headerLength == header.Length)
        {
            state = State.IgnoreSequence;
            return false;
        }

        header[headerLength++] = value;
        return true;
    }

    private void BeginString(VtStringKind kind)
    {
        stringKind = kind;
        state = State.String;
    }

    private void ConsumeString(byte value)
    {
        if (stringEscape)
        {
            stringEscape = false;
            if (value == (byte)'\\')
            {
                EndString();
                return;
            }

            AppendPayload(0x1b);
        }

        if (value == 0x1b)
        {
            stringEscape = true;
        }
        else if (value == 0x07 && stringKind == VtStringKind.Osc)
        {
            EndString();
        }
        else
        {
            AppendPayload(value);
        }
    }

    private void AppendPayload(byte value)
    {
        if (payloadLength == payload.Length)
        {
            stringOverflow = true;
            return;
        }

        payload[payloadLength++] = value;
    }

    private void EndString()
    {
        if (!stringOverflow)
        {
            sink.String(stringKind, payload.AsSpan(0, payloadLength));
        }

        Reset();
    }

    private void PrintUtf8(byte value)
    {
        if (utf8Length == 0 && value < 0x80)
        {
            sink.Print(new Rune(value));
            return;
        }

        utf8[utf8Length++] = value;
        while (utf8Length > 0)
        {
            OperationStatus status = Rune.DecodeFromUtf8(utf8.AsSpan(0, utf8Length), out Rune rune, out int consumed);
            if (status == OperationStatus.NeedMoreData)
            {
                return;
            }

            sink.Print(status == OperationStatus.Done ? rune : Rune.ReplacementChar);
            utf8.AsSpan(consumed, utf8Length - consumed).CopyTo(utf8);
            utf8Length -= consumed;
        }
    }

    private void FlushUtf8()
    {
        if (utf8Length > 0)
        {
            utf8Length = 0;
            sink.Print(Rune.ReplacementChar);
        }
    }

    private void Reset()
    {
        state = State.Ground;
        headerLength = 0;
        parameterLength = 0;
        payloadLength = 0;
        stringEscape = false;
        stringOverflow = false;
    }
}
