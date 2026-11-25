using System.Buffers;
using System.Text;
using Nexer.Core.Protocol;

namespace Nexer.Input;

/// <summary>
/// Decodes the raw byte stream a VT terminal (xterm, Windows Terminal, ...) sends to an application into
/// <see cref="InputEvent"/>s: keys, SGR (1006) and X10 mouse reports, focus (1004) and bracketed paste (2004).
/// Terminal replies (DA, DSR/CPR, DECRPM, OSC/DCS strings, ...) are swallowed and never surface as keys.
/// </summary>
/// <remarks>
/// <para>
/// Single owner: <see cref="Feed"/>, <see cref="ResolveTimeout"/> and <see cref="Complete"/> must not be called
/// concurrently. Events are delivered synchronously to the callback passed to the constructor.
/// </para>
/// <para>
/// A lone ESC is ambiguous (Escape key, Alt prefix, or the start of a sequence). An incomplete sequence is held
/// until its next byte arrives or <see cref="InputDecoderOptions.EscapeTimeout"/> elapses since the last byte.
/// The owner should arm a timer for <see cref="TimeUntilTimeout"/> and then call <see cref="ResolveTimeout"/>;
/// <see cref="Feed"/> also resolves an expired sequence before consuming new bytes.
/// </para>
/// </remarks>
public sealed class InputDecoder
{
    private const int MaxPending = 128;
    private static readonly byte[] PasteEnd = "\e[201~"u8.ToArray();
    private static readonly SearchValues<byte> GroundSpecials = SearchValues.Create([0x1b, 0x7f, 0x18, 0x1a]);

    private enum State : byte
    {
        Ground,
        Escape,
        Utf8Alt,
        Csi,
        X10,
        Ss3,
        String,
        StringEscape,
        DiscardCsi,
        DiscardString,
        DiscardStringEscape,
        Paste,
    }

    private readonly Action<InputEvent> onEvent;
    private readonly InputDecoderOptions options;
    private readonly TimeProvider time;
    private readonly VtScanner scanner;
    private readonly byte[] pending = new byte[MaxPending];
    private readonly ArrayBufferWriter<byte> paste = new();
    private readonly Decoder pasteDecoder = Encoding.UTF8.GetDecoder();
    private State state;
    private int pendingLength;
    private bool altPrefix;
    private bool stringIsOsc;
    private int pasteMatch;
    private long lastByteTimestamp;
    private MouseButton x10Button;

    /// <summary>Creates a decoder that reports events to <paramref name="onEvent"/>.</summary>
    /// <param name="onEvent">Receives every decoded event synchronously.</param>
    /// <param name="options">Tuning; defaults when null.</param>
    /// <param name="timeProvider">Clock for timestamps and ESC timeouts; <see cref="TimeProvider.System"/> when null.</param>
    public InputDecoder(Action<InputEvent> onEvent, InputDecoderOptions? options = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(onEvent);
        this.onEvent = onEvent;
        this.options = options ?? new InputDecoderOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(this.options.EscapeTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(this.options.MaxPasteChunkBytes, 16);
        time = timeProvider ?? TimeProvider.System;
        scanner = new VtScanner(new Sink(this), maxSequenceBytes: MaxPending);
    }

    /// <summary>True while an incomplete, possibly ambiguous sequence is held awaiting more bytes or the timeout.</summary>
    public bool HasPendingSequence => IsTimed(state);

    /// <summary>
    /// Time left before the pending sequence is resolved as typed keys, <see cref="TimeSpan.Zero"/> when already
    /// due, or null when nothing is pending.
    /// </summary>
    public TimeSpan? TimeUntilTimeout
    {
        get
        {
            if (!HasPendingSequence) return null;
            TimeSpan left = options.EscapeTimeout - time.GetElapsedTime(lastByteTimestamp);
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    /// <summary>Consumes <paramref name="bytes"/>, emitting every event that is complete.</summary>
    public void Feed(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return;
        ResolveTimeout();
        lastByteTimestamp = time.GetTimestamp();
        Process(bytes);
    }

    /// <summary>Resolves the pending sequence as typed keys if the ESC timeout has elapsed.</summary>
    /// <returns>True when a pending sequence was resolved.</returns>
    public bool ResolveTimeout()
    {
        if (!HasPendingSequence || time.GetElapsedTime(lastByteTimestamp) < options.EscapeTimeout) return false;
        Abandon();
        return true;
    }

    /// <summary>
    /// Signals end of input: resolves any pending sequence immediately, delivers a partial paste, and replaces
    /// incomplete UTF-8 with U+FFFD.
    /// </summary>
    public void Complete()
    {
        if (HasPendingSequence) Abandon();
        if (state == State.Paste)
        {
            EmitPaste(flush: true);
        }

        state = State.Ground;
        scanner.Complete();
    }

    private static bool IsTimed(State state) => state is State.Escape or State.Utf8Alt or State.Csi or State.X10
        or State.Ss3 or State.String or State.StringEscape;

    private void Process(ReadOnlySpan<byte> bytes)
    {
        int i = 0;
        while (i < bytes.Length)
        {
            if (state == State.Ground)
            {
                int run = bytes[i..].IndexOfAny(GroundSpecials);
                if (run < 0) run = bytes.Length - i;
                if (run > 0)
                {
                    scanner.Feed(bytes.Slice(i, run));
                    i += run;
                    continue;
                }

                GroundSpecial(bytes[i++]);
            }
            else if (state == State.Paste)
            {
                i += ConsumePaste(bytes[i..]);
            }
            else
            {
                ProcessPending(bytes[i++]);
            }
        }
    }

    private void GroundSpecial(byte value)
    {
        // CAN flushes any partial UTF-8 in the scanner (as U+FFFD) and resets it without callbacks of its own.
        Flush();
        switch (value)
        {
            case 0x1b:
                Begin(State.Escape, value);
                break;
            case 0x7f:
                EmitKey(Key.Backspace, default, KeyModifiers.None);
                break;
            default:
                EmitControl(value, KeyModifiers.None);
                break;
        }
    }

    private void Flush() => scanner.Feed([0x18]);

    private void Begin(State next, byte value)
    {
        state = next;
        pending[0] = value;
        pendingLength = 1;
    }

    private void Append(byte value) => pending[pendingLength++] = value;

    private void Reset()
    {
        state = State.Ground;
        pendingLength = 0;
        altPrefix = false;
    }

    private void ProcessPending(byte value)
    {
        switch (state)
        {
            case State.Escape:
                EscapeByte(value);
                break;
            case State.Utf8Alt:
                Utf8AltByte(value);
                break;
            case State.Csi:
                CsiByte(value);
                break;
            case State.X10:
                Append(value);
                if (pendingLength == 6)
                {
                    DecodeX10();
                }

                break;
            case State.Ss3:
                Ss3Byte(value);
                break;
            case State.String:
            case State.StringEscape:
                StringByte(value);
                break;
            case State.DiscardCsi:
                if (value is >= 0x40 and <= 0x7e) Reset();
                else if (value is < 0x20 or > 0x7e)
                {
                    Reset();
                    Process([value]);
                }

                break;
            case State.DiscardString:
                if (value == 0x1b) state = State.DiscardStringEscape;
                else if ((value == 0x07 && stringIsOsc) || value is 0x18 or 0x1a) Reset();
                break;
            case State.DiscardStringEscape:
                if (value == (byte)'\\') Reset();
                else if (value != 0x1b) state = State.DiscardString;
                break;
        }
    }

    private void EscapeByte(byte value)
    {
        switch (value)
        {
            case (byte)'[':
                Append(value);
                state = State.Csi;
                return;
            case (byte)'O':
                Append(value);
                state = State.Ss3;
                return;
            case (byte)']' or (byte)'P' or (byte)'X' or (byte)'^' or (byte)'_':
                Append(value);
                stringIsOsc = value == (byte)']';
                state = State.String;
                return;
            case 0x1b:
                // ESC ESC: the first ESC may be an Alt prefix for a sequence (rxvt Alt+arrow) or a separate Escape.
                if (altPrefix) EmitKey(Key.Escape, default, KeyModifiers.None);
                altPrefix = true;
                return;
        }

        if (value < 0x80)
        {
            // ESC ESC x: the first ESC was a separate Escape key.
            FlushAltAsEscape();
            Reset();
            if (value == 0x7f) EmitKey(Key.Backspace, default, KeyModifiers.Alt);
            else if (value < 0x20) EmitControl(value, KeyModifiers.Alt);
            else EmitKey(Key.Character, new Rune(value), KeyModifiers.Alt);
            return;
        }

        Append(value);
        state = State.Utf8Alt;
        Utf8AltCheck();
    }

    private void Utf8AltByte(byte value)
    {
        if ((value & 0xc0) != 0x80)
        {
            Abandon();
            Process([value]);
            return;
        }

        Append(value);
        Utf8AltCheck();
    }

    private void Utf8AltCheck()
    {
        OperationStatus status = Rune.DecodeFromUtf8(pending.AsSpan(1, pendingLength - 1), out Rune rune, out _);
        if (status == OperationStatus.NeedMoreData) return;
        if (status != OperationStatus.Done)
        {
            Abandon();
            return;
        }

        FlushAltAsEscape();
        Reset();
        EmitKey(Key.Character, rune, KeyModifiers.Alt);
    }

    private void CsiByte(byte value)
    {
        if (value == (byte)'M' && pendingLength == 2)
        {
            Append(value);
            state = State.X10;
        }
        else if (value is >= 0x20 and <= 0x3f)
        {
            if (pendingLength == MaxPending - 1)
            {
                // Overlong: no key is this long, so it is a (broken) reply; drop it through its final byte.
                FlushAltAsEscape();
                Reset();
                state = State.DiscardCsi;
                return;
            }

            Append(value);
        }
        else if (value is >= 0x40 and <= 0x7e)
        {
            Append(value);
            CompleteCsi();
        }
        else
        {
            Abandon();
            Process([value]);
        }
    }

    private void CompleteCsi()
    {
        // The scanner parses the complete sequence and dispatches it to Sink.Dispatch → InterpretCsi.
        int length = pendingLength;
        pendingLength = 0;
        state = State.Ground;
        scanner.Feed(pending.AsSpan(0, length));
        FlushAltAsEscape();
        altPrefix = false;
    }

    private void Ss3Byte(byte value)
    {
        if (value is >= (byte)'0' and <= (byte)'9' && pendingLength == 2)
        {
            Append(value);
            return;
        }

        if (value is < 0x40 or > 0x7e)
        {
            Abandon();
            Process([value]);
            return;
        }

        KeyModifiers modifiers = pendingLength == 3 ? DecodeModifiers(pending[2] - '0') : KeyModifiers.None;
        Key key = value switch
        {
            (byte)'A' => Key.Up,
            (byte)'B' => Key.Down,
            (byte)'C' => Key.Right,
            (byte)'D' => Key.Left,
            (byte)'H' => Key.Home,
            (byte)'F' => Key.End,
            (byte)'P' => Key.F1,
            (byte)'Q' => Key.F2,
            (byte)'R' => Key.F3,
            (byte)'S' => Key.F4,
            (byte)'M' => Key.Enter,
            _ => Key.None,
        };
        Rune keypad = default;
        if (key == Key.None && value is >= (byte)'j' and <= (byte)'y')
        {
            // Application keypad: ESC O j..y = * + , - . / 0..9
            key = Key.Character;
            keypad = new Rune(value - 0x40);
        }
        else if (key == Key.None && value == (byte)'X')
        {
            key = Key.Character;
            keypad = new Rune('=');
        }

        modifiers |= TakeAlt();
        Reset();
        if (key != Key.None)
        {
            EmitKey(key, keypad, modifiers);
        }
    }

    private void StringByte(byte value)
    {
        if (pendingLength == MaxPending)
        {
            // Too long to be typed text: commit to swallowing it as a terminal reply.
            FlushAltAsEscape();
            State next = state == State.StringEscape ? State.DiscardStringEscape : State.DiscardString;
            Reset();
            state = next;
            ProcessPending(value);
            return;
        }

        if (state == State.StringEscape)
        {
            if (value == (byte)'\\')
            {
                SwallowString();
                return;
            }

            Append(value);
            state = value == 0x1b ? State.StringEscape : State.String;
            return;
        }

        if ((value == 0x07 && stringIsOsc) || value is 0x18 or 0x1a)
        {
            SwallowString();
            return;
        }

        Append(value);
        if (value == 0x1b) state = State.StringEscape;
    }

    private void SwallowString()
    {
        FlushAltAsEscape();
        Reset();
    }

    /// <summary>Resolves the pending bytes as typed keys and replays whatever followed the introducer.</summary>
    private void Abandon()
    {
        if (!IsTimed(state)) return;
        Span<byte> replay = stackalloc byte[MaxPending];
        int replayLength = 0;
        if (altPrefix) EmitKey(Key.Escape, default, KeyModifiers.None);
        if (pendingLength == 1)
        {
            EmitKey(Key.Escape, default, KeyModifiers.None);
        }
        else if (pending[1] < 0x80)
        {
            EmitKey(Key.Character, new Rune(pending[1]), KeyModifiers.Alt);
            replayLength = pendingLength - 2;
            pending.AsSpan(2, replayLength).CopyTo(replay);
        }
        else
        {
            EmitKey(Key.Escape, default, KeyModifiers.None);
            replayLength = pendingLength - 1;
            pending.AsSpan(1, replayLength).CopyTo(replay);
        }

        Reset();
        if (replayLength > 0) Process(replay[..replayLength]);
    }

    private int ConsumePaste(ReadOnlySpan<byte> bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            byte value = bytes[i];
            if (value == PasteEnd[pasteMatch])
            {
                if (++pasteMatch == PasteEnd.Length)
                {
                    pasteMatch = 0;
                    EmitPaste(flush: true);
                    state = State.Ground;
                    return i + 1;
                }

                continue;
            }

            if (pasteMatch > 0)
            {
                // A partial terminator was ordinary paste content after all.
                paste.Write(PasteEnd.AsSpan(0, pasteMatch));
                pasteMatch = 0;
                if (value == 0x1b)
                {
                    pasteMatch = 1;
                    continue;
                }
            }

            paste.GetSpan(1)[0] = value;
            paste.Advance(1);
            if (paste.WrittenCount >= options.MaxPasteChunkBytes) EmitPaste(flush: false);
        }

        return bytes.Length;
    }

    private void EmitPaste(bool flush)
    {
        ReadOnlySpan<byte> bytes = paste.WrittenSpan;
        if (flush && pasteMatch > 0)
        {
            // EOF in the middle of a terminator: the partial terminator is content.
            paste.Write(PasteEnd.AsSpan(0, pasteMatch));
            pasteMatch = 0;
            bytes = paste.WrittenSpan;
        }

        char[] chars = ArrayPool<char>.Shared.Rent(Encoding.UTF8.GetMaxCharCount(bytes.Length + 4));
        int count = pasteDecoder.GetChars(bytes, chars, flush);
        string text = new(chars, 0, count);
        ArrayPool<char>.Shared.Return(chars);
        paste.ResetWrittenCount();
        if (text.Length > 0 || flush) Emit(new PasteEvent(text) { Timestamp = time.GetTimestamp() });
    }

    private void DecodeX10()
    {
        int code = pending[3] - 32, x = pending[4] - 33, y = pending[5] - 33;
        FlushAltAsEscape();
        Reset();
        if (code < 0) return;
        EmitMouse(code, Math.Max(0, x), Math.Max(0, y), release: (code & 0x43) == 3, x10: true, KeyModifiers.None);
    }

    private void InterpretCsi(ReadOnlySpan<byte> parameters, ReadOnlySpan<byte> intermediates, byte final)
    {
        byte prefix = 0;
        if (!parameters.IsEmpty && parameters[0] is >= 0x3c and <= 0x3f)
        {
            prefix = parameters[0];
            parameters = parameters[1..];
        }

        Span<int> values = stackalloc int[8];
        int count = ParseParameters(parameters, values);
        if (count < 0 || !intermediates.IsEmpty) return;
        if (prefix == (byte)'<')
        {
            if (final is (byte)'M' or (byte)'m' && count == 3)
            {
                FlushAltAsEscape();
                EmitMouse(values[0], Math.Max(0, values[1] - 1), Math.Max(0, values[2] - 1), final == 'm', x10: false, KeyModifiers.None);
            }

            return;
        }

        if (prefix != 0) return;
        int p0 = count > 0 ? values[0] : 0;
        KeyModifiers modifiers = count > 1 ? DecodeModifiers(values[1]) : KeyModifiers.None;
        switch (final)
        {
            case (byte)'A': CursorKey(Key.Up, count, p0, modifiers); break;
            case (byte)'B': CursorKey(Key.Down, count, p0, modifiers); break;
            case (byte)'C': CursorKey(Key.Right, count, p0, modifiers); break;
            case (byte)'D': CursorKey(Key.Left, count, p0, modifiers); break;
            case (byte)'H': CursorKey(Key.Home, count, p0, modifiers); break;
            case (byte)'F': CursorKey(Key.End, count, p0, modifiers); break;
            case (byte)'P': CursorKey(Key.F1, count, p0, modifiers); break;
            case (byte)'Q': CursorKey(Key.F2, count, p0, modifiers); break;
            case (byte)'S': CursorKey(Key.F4, count, p0, modifiers); break;
            case (byte)'R':
                // CSI r;c R is a cursor position report; only CSI 1;m R (m > 1) is a modified F3.
                if (count <= 1 || (count == 2 && p0 == 1 && values[1] > 1)) CursorKey(Key.F3, count, p0, modifiers);
                break;
            case (byte)'Z':
                if (count <= 1) EmitKey(Key.BackTab, default, TakeAlt());
                else EmitKey(Key.BackTab, default, (modifiers & ~KeyModifiers.Shift) | TakeAlt());
                break;
            case (byte)'~':
                if (p0 == 200 && count == 1)
                {
                    FlushAltAsEscape();
                    state = State.Paste;
                }
                else if (p0 == 27 && count == 3)
                {
                    CodepointKey(values[2], DecodeModifiers(values[1]));
                }
                else
                {
                    TildeKey(p0, count <= 2, modifiers);
                }

                break;
            case (byte)'u':
                if (count is 1 or 2) CodepointKey(p0, modifiers);
                break;
            case (byte)'I' or (byte)'O':
                if (count == 0)
                {
                    FlushAltAsEscape();
                    Emit(new FocusEvent(final == 'I') { Timestamp = time.GetTimestamp() });
                }

                break;
            case (byte)'M':
                if (count == 3)
                {
                    // urxvt 1015: CSI b;x;y M with the X10 button encoding.
                    FlushAltAsEscape();
                    int code = values[0] - 32;
                    if (code >= 0) EmitMouse(code, Math.Max(0, values[1] - 1), Math.Max(0, values[2] - 1), (code & 0x43) == 3, x10: true, KeyModifiers.None);
                }

                break;
        }
    }

    private void CursorKey(Key key, int count, int p0, KeyModifiers modifiers)
    {
        if (count > 2 || (count >= 1 && p0 > 1)) return;
        EmitKey(key, default, modifiers | TakeAlt());
    }

    private void TildeKey(int code, bool valid, KeyModifiers modifiers)
    {
        if (!valid) return;
        Key key = code switch
        {
            1 or 7 => Key.Home,
            2 => Key.Insert,
            3 => Key.Delete,
            4 or 8 => Key.End,
            5 => Key.PageUp,
            6 => Key.PageDown,
            11 => Key.F1,
            12 => Key.F2,
            13 => Key.F3,
            14 => Key.F4,
            15 => Key.F5,
            17 => Key.F6,
            18 => Key.F7,
            19 => Key.F8,
            20 => Key.F9,
            21 => Key.F10,
            23 => Key.F11,
            24 => Key.F12,
            _ => Key.None,
        };
        if (key != Key.None) EmitKey(key, default, modifiers | TakeAlt());
    }

    private void CodepointKey(int code, KeyModifiers modifiers)
    {
        modifiers |= TakeAlt();
        switch (code)
        {
            case 13: EmitKey(Key.Enter, default, modifiers); return;
            case 9: EmitKey(Key.Tab, default, modifiers); return;
            case 27: EmitKey(Key.Escape, default, modifiers); return;
            case 8 or 127: EmitKey(Key.Backspace, default, modifiers); return;
        }

        if (code < 0x20 || !Rune.IsValid(code)) return;
        var rune = new Rune(code);
        if ((modifiers & KeyModifiers.Ctrl) != 0 && code is >= 'A' and <= 'Z') rune = new Rune(code + 0x20);
        EmitKey(Key.Character, rune, modifiers);
    }

    private void EmitMouse(int code, int x, int y, bool release, bool x10, KeyModifiers extra)
    {
        if (code >= 128) return;
        KeyModifiers modifiers = extra;
        if ((code & 4) != 0) modifiers |= KeyModifiers.Shift;
        if ((code & 8) != 0) modifiers |= KeyModifiers.Alt;
        if ((code & 16) != 0) modifiers |= KeyModifiers.Ctrl;
        int low = code & 3;
        MouseButton button = low switch
        {
            0 => MouseButton.Left,
            1 => MouseButton.Middle,
            2 => MouseButton.Right,
            _ => MouseButton.None,
        };
        MouseEventKind kind;
        if ((code & 64) != 0)
        {
            kind = low switch
            {
                0 => MouseEventKind.WheelUp,
                1 => MouseEventKind.WheelDown,
                2 => MouseEventKind.WheelLeft,
                _ => MouseEventKind.WheelRight,
            };
            button = MouseButton.None;
        }
        else if ((code & 32) != 0)
        {
            kind = button == MouseButton.None ? MouseEventKind.Move : MouseEventKind.Drag;
        }
        else if (release)
        {
            kind = MouseEventKind.Up;
            if (x10)
            {
                // X10 releases do not say which button; report the one that went down.
                button = x10Button;
                x10Button = MouseButton.None;
            }
        }
        else
        {
            if (button == MouseButton.None) return;
            kind = MouseEventKind.Down;
            if (x10) x10Button = button;
        }

        Emit(new MouseEvent(kind, button, x, y, modifiers) { Timestamp = time.GetTimestamp() });
    }

    private static int ParseParameters(ReadOnlySpan<byte> parameters, Span<int> values)
    {
        if (parameters.IsEmpty) return 0;
        int count = 0, value = 0;
        bool sub = false;
        foreach (byte b in parameters)
        {
            if (b == (byte)';')
            {
                if (count == values.Length) return -1;
                values[count++] = value;
                value = 0;
                sub = false;
            }
            else if (b == (byte)':')
            {
                sub = true;
            }
            else if (b is >= (byte)'0' and <= (byte)'9')
            {
                if (!sub) value = value >= 100_000_000 ? 1_000_000_000 : value * 10 + (b - '0');
            }
            else
            {
                return -1;
            }
        }

        if (count == values.Length) return -1;
        values[count++] = value;
        return count;
    }

    private static KeyModifiers DecodeModifiers(int parameter)
    {
        if (parameter <= 1) return KeyModifiers.None;
        int bits = parameter - 1;
        KeyModifiers modifiers = KeyModifiers.None;
        if ((bits & 1) != 0) modifiers |= KeyModifiers.Shift;
        if ((bits & 2) != 0) modifiers |= KeyModifiers.Alt;
        if ((bits & 4) != 0) modifiers |= KeyModifiers.Ctrl;
        if (bits < 16)
        {
            // xterm layout: 8 = Meta.
            if ((bits & 8) != 0) modifiers |= KeyModifiers.Meta;
        }
        else
        {
            // Bits above 8 only exist in the kitty layout: 8 = Super, 16 = Hyper (no contract member),
            // 32 = Meta, 64/128 = Caps/Num Lock (ignored).
            if ((bits & 8) != 0) modifiers |= KeyModifiers.Super;
            if ((bits & 32) != 0) modifiers |= KeyModifiers.Meta;
        }

        return modifiers;
    }

    private KeyModifiers TakeAlt()
    {
        if (!altPrefix) return KeyModifiers.None;
        altPrefix = false;
        return KeyModifiers.Alt;
    }

    private void FlushAltAsEscape()
    {
        if (!altPrefix) return;
        altPrefix = false;
        EmitKey(Key.Escape, default, KeyModifiers.None);
    }

    private void EmitControl(byte control, KeyModifiers modifiers)
    {
        switch (control)
        {
            case 0x0d: EmitKey(Key.Enter, default, modifiers); return;
            case 0x09: EmitKey(Key.Tab, default, modifiers); return;
            case 0x08:
                EmitKey(Key.Backspace, default, options.ControlHIsControlBackspace ? modifiers | KeyModifiers.Ctrl : modifiers);
                return;
            case 0x00: EmitKey(Key.Character, new Rune(' '), modifiers | KeyModifiers.Ctrl); return;
            case 0x1b: EmitKey(Key.Escape, default, modifiers); return;
        }

        // 0x01-0x1a → Ctrl+a..z; 0x1c-0x1f → Ctrl+\ ] ^ _
        int scalar = control <= 0x1a ? control + 0x60 : control + 0x40;
        EmitKey(Key.Character, new Rune(scalar), modifiers | KeyModifiers.Ctrl);
    }

    private void EmitKey(Key key, Rune value, KeyModifiers modifiers) =>
        Emit(new KeyEvent(key, value, modifiers) { Timestamp = time.GetTimestamp() });

    private void Emit(InputEvent value) => onEvent(value);

    private sealed class Sink(InputDecoder owner) : IVtSink
    {
        public void Print(Rune value) => owner.EmitKey(Key.Character, value, KeyModifiers.None);

        public void Execute(byte control) => owner.EmitControl(control, KeyModifiers.None);

        public void Dispatch(VtSequenceKind kind, ReadOnlySpan<byte> parameters, ReadOnlySpan<byte> intermediates, byte final)
        {
            if (kind == VtSequenceKind.Csi) owner.InterpretCsi(parameters, intermediates, final);
        }

        public void String(VtStringKind kind, ReadOnlySpan<byte> payload)
        {
        }
    }
}
