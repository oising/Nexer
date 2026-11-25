using System.Globalization;
using Nexer.Core;

namespace Nexer.Rendering;

/// <summary>Encodes the shortest SGR sequence that transforms one rendition into another.</summary>
internal static class SgrEncoder
{
    // Worst case: 7 removals, 8 additions and two RGB colours (~75 bytes).
    private const int MaxParameters = 96;

    /// <summary>Appends SGR to reach <paramref name="target"/>. <paramref name="current"/> is null when unknown.</summary>
    public static void Append(OutputBuffer output, CellStyle? current, CellStyle target)
    {
        Span<byte> reset = stackalloc byte[MaxParameters];
        int resetLength = WriteReset(reset, target);
        if (current is { } known)
        {
            Span<byte> delta = stackalloc byte[MaxParameters];
            int deltaLength = WriteDelta(delta, known, target);
            if (deltaLength <= resetLength)
            {
                if (deltaLength == 0) return;
                Emit(output, delta[..deltaLength]);
                return;
            }
        }

        Emit(output, reset[..resetLength]);
    }

    private static void Emit(OutputBuffer output, ReadOnlySpan<byte> parameters)
    {
        output.Append("\e["u8);
        output.Append(parameters);
        output.Append((byte)'m');
    }

    private static int WriteReset(Span<byte> buffer, CellStyle target)
    {
        if (target == default) return 0; // "\e[m"
        int length = 0;
        Number(buffer, ref length, 0);
        AddFlags(buffer, ref length, target.Flags);
        if (target.Foreground.Kind != TerminalColorKind.Default) Color(buffer, ref length, target.Foreground, foreground: true);
        if (target.Background.Kind != TerminalColorKind.Default) Color(buffer, ref length, target.Background, foreground: false);
        return length - 1;
    }

    private static int WriteDelta(Span<byte> buffer, CellStyle current, CellStyle target)
    {
        int length = 0;
        CellStyleFlags removed = current.Flags & ~target.Flags;
        CellStyleFlags added = target.Flags & ~current.Flags;
        if ((removed & (CellStyleFlags.Bold | CellStyleFlags.Faint)) != 0)
        {
            Number(buffer, ref length, 22);
            added |= target.Flags & (CellStyleFlags.Bold | CellStyleFlags.Faint);
        }

        if ((removed & CellStyleFlags.Italic) != 0) Number(buffer, ref length, 23);
        if ((removed & CellStyleFlags.Underline) != 0) Number(buffer, ref length, 24);
        if ((removed & CellStyleFlags.Blink) != 0) Number(buffer, ref length, 25);
        if ((removed & CellStyleFlags.Inverse) != 0) Number(buffer, ref length, 27);
        if ((removed & CellStyleFlags.Conceal) != 0) Number(buffer, ref length, 28);
        if ((removed & CellStyleFlags.Strike) != 0) Number(buffer, ref length, 29);
        AddFlags(buffer, ref length, added);
        if (current.Foreground != target.Foreground) Color(buffer, ref length, target.Foreground, foreground: true);
        if (current.Background != target.Background) Color(buffer, ref length, target.Background, foreground: false);
        return length == 0 ? 0 : length - 1;
    }

    private static void AddFlags(Span<byte> buffer, ref int length, CellStyleFlags flags)
    {
        if ((flags & CellStyleFlags.Bold) != 0) Number(buffer, ref length, 1);
        if ((flags & CellStyleFlags.Faint) != 0) Number(buffer, ref length, 2);
        if ((flags & CellStyleFlags.Italic) != 0) Number(buffer, ref length, 3);
        if ((flags & CellStyleFlags.Underline) != 0) Number(buffer, ref length, 4);
        if ((flags & CellStyleFlags.Blink) != 0) Number(buffer, ref length, 5);
        if ((flags & CellStyleFlags.Inverse) != 0) Number(buffer, ref length, 7);
        if ((flags & CellStyleFlags.Conceal) != 0) Number(buffer, ref length, 8);
        if ((flags & CellStyleFlags.Strike) != 0) Number(buffer, ref length, 9);
    }

    private static void Color(Span<byte> buffer, ref int length, TerminalColor color, bool foreground)
    {
        int baseCode = foreground ? 30 : 40;
        switch (color.Kind)
        {
            case TerminalColorKind.Default:
                Number(buffer, ref length, baseCode + 9);
                break;
            case TerminalColorKind.Indexed when color.Value < 8:
                Number(buffer, ref length, baseCode + (int)color.Value);
                break;
            case TerminalColorKind.Indexed when color.Value < 16:
                Number(buffer, ref length, baseCode + 60 + (int)color.Value - 8);
                break;
            case TerminalColorKind.Indexed:
                Number(buffer, ref length, baseCode + 8);
                Number(buffer, ref length, 5);
                Number(buffer, ref length, (int)color.Value);
                break;
            default:
                Number(buffer, ref length, baseCode + 8);
                Number(buffer, ref length, 2);
                Number(buffer, ref length, (int)(color.Value >> 16 & 0xff));
                Number(buffer, ref length, (int)(color.Value >> 8 & 0xff));
                Number(buffer, ref length, (int)(color.Value & 0xff));
                break;
        }
    }

    private static void Number(Span<byte> buffer, ref int length, int value)
    {
        value.TryFormat(buffer[length..], out int written, provider: CultureInfo.InvariantCulture);
        length += written;
        buffer[length++] = (byte)';';
    }
}
