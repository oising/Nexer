using Nexer.Core;

namespace Nexer.Emulation;

public sealed partial class Terminal
{
    private void ApplySgr(ReadOnlySpan<byte> bytes)
    {
        Span<int> values = stackalloc int[32];
        Span<byte> separators = stackalloc byte[32];
        values.Fill(-1);
        int count = 1;
        separators[0] = 0;
        foreach (byte value in bytes)
        {
            if (value is (byte)';' or (byte)':')
            {
                if (count == values.Length) return;
                separators[count++] = value;
            }
            else if (value is >= (byte)'0' and <= (byte)'9')
            {
                int next = Math.Max(0, values[count - 1]) * 10 + value - '0';
                if (next > 1_000_000) return;
                values[count - 1] = next;
            }
            else return;
        }

        for (int i = 0; i < count; i++)
        {
            int code = Math.Max(0, values[i]);
            int end = i + 1;
            while (end < count && separators[end] == (byte)':') end++;
            if (end > i + 1)
            {
                ApplyColonSgr(code, values[(i + 1)..end]);
                i = end - 1;
                continue;
            }

            if (code is 38 or 48)
            {
                int needed = i + 1 < count ? values[i + 1] switch { 5 => 2, 2 => 4, _ => 0 } : 0;
                if (needed == 0 || count - i - 1 < needed) return;
                for (int part = 1; part <= needed; part++)
                {
                    if (separators[i + part] != (byte)';') return;
                }

                if (i + needed + 1 < count && separators[i + needed + 1] == (byte)':') return;
                if (!TryColor(values.Slice(i + 1, needed), out TerminalColor color)) return;
                SetColor(code, color);
                i += needed;
                continue;
            }

            CellStyleFlags flags = CurrentStyle.Flags;
            switch (code)
            {
                case 0: CurrentStyle = default; break;
                case 1: CurrentStyle = CurrentStyle with { Flags = flags | CellStyleFlags.Bold }; break;
                case 2: CurrentStyle = CurrentStyle with { Flags = flags | CellStyleFlags.Faint }; break;
                case 3: CurrentStyle = CurrentStyle with { Flags = flags | CellStyleFlags.Italic }; break;
                case 4: CurrentStyle = CurrentStyle with { Flags = flags | CellStyleFlags.Underline }; break;
                case 5: CurrentStyle = CurrentStyle with { Flags = flags | CellStyleFlags.Blink }; break;
                case 7: CurrentStyle = CurrentStyle with { Flags = flags | CellStyleFlags.Inverse }; break;
                case 8: CurrentStyle = CurrentStyle with { Flags = flags | CellStyleFlags.Conceal }; break;
                case 9: CurrentStyle = CurrentStyle with { Flags = flags | CellStyleFlags.Strike }; break;
                case 22: CurrentStyle = CurrentStyle with { Flags = flags & ~(CellStyleFlags.Bold | CellStyleFlags.Faint) }; break;
                case 23: CurrentStyle = CurrentStyle with { Flags = flags & ~CellStyleFlags.Italic }; break;
                case 24: CurrentStyle = CurrentStyle with { Flags = flags & ~CellStyleFlags.Underline }; break;
                case 25: CurrentStyle = CurrentStyle with { Flags = flags & ~CellStyleFlags.Blink }; break;
                case 27: CurrentStyle = CurrentStyle with { Flags = flags & ~CellStyleFlags.Inverse }; break;
                case 28: CurrentStyle = CurrentStyle with { Flags = flags & ~CellStyleFlags.Conceal }; break;
                case 29: CurrentStyle = CurrentStyle with { Flags = flags & ~CellStyleFlags.Strike }; break;
                case 39: CurrentStyle = CurrentStyle with { Foreground = default }; break;
                case 49: CurrentStyle = CurrentStyle with { Background = default }; break;
                case >= 30 and <= 37: CurrentStyle = CurrentStyle with { Foreground = TerminalColor.Indexed((byte)(code - 30)) }; break;
                case >= 40 and <= 47: CurrentStyle = CurrentStyle with { Background = TerminalColor.Indexed((byte)(code - 40)) }; break;
                case >= 90 and <= 97: CurrentStyle = CurrentStyle with { Foreground = TerminalColor.Indexed((byte)(code - 90 + 8)) }; break;
                case >= 100 and <= 107: CurrentStyle = CurrentStyle with { Background = TerminalColor.Indexed((byte)(code - 100 + 8)) }; break;
            }
        }
    }

    private void ApplyColonSgr(int code, scoped ReadOnlySpan<int> parts)
    {
        if (code == 4 && parts.Length == 1 && parts[0] is 0 or 1)
        {
            CurrentStyle = CurrentStyle with
            {
                Flags = parts[0] == 0 ? CurrentStyle.Flags & ~CellStyleFlags.Underline
                    : CurrentStyle.Flags | CellStyleFlags.Underline
            };
        }
        else if (code is 38 or 48)
        {
            Span<int> normalized = stackalloc int[4];
            if (parts.Length == 5 && parts[0] == 2 && parts[1] is -1 or 0)
            {
                normalized[0] = 2;
                parts[2..].CopyTo(normalized[1..]);
                parts = normalized;
            }

            if (TryColor(parts, out TerminalColor color)) SetColor(code, color);
        }
    }

    private static bool TryColor(ReadOnlySpan<int> parts, out TerminalColor color)
    {
        color = default;
        if (parts.Length == 2 && parts[0] == 5 && parts[1] is >= 0 and <= 255)
        {
            color = TerminalColor.Indexed((byte)parts[1]);
            return true;
        }

        if (parts.Length == 4 && parts[0] == 2 &&
            parts[1] is >= 0 and <= 255 && parts[2] is >= 0 and <= 255 && parts[3] is >= 0 and <= 255)
        {
            color = TerminalColor.Rgb((byte)parts[1], (byte)parts[2], (byte)parts[3]);
            return true;
        }

        return false;
    }

    private void SetColor(int code, TerminalColor color)
    {
        CurrentStyle = code == 38 ? CurrentStyle with { Foreground = color }
            : CurrentStyle with { Background = color };
    }
}
