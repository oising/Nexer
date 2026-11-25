using System.Text;
using Microsoft.Extensions.Time.Testing;

namespace Nexer.Input.Tests;

public class InputDecoderTests
{
    private readonly FakeTimeProvider time = new();
    private readonly List<InputEvent> events = [];
    private readonly InputDecoder decoder;

    public InputDecoderTests()
    {
        decoder = new InputDecoder(events.Add, timeProvider: time);
    }

    [Theory]
    [InlineData("a", "'a'")]
    [InlineData("A", "'A'")]
    [InlineData("é中😀", "'é' '中' '😀'")]
    [InlineData(" ", "' '")]
    [InlineData("\r", "Enter")]
    [InlineData("\t", "Tab")]
    [InlineData("\x7f", "Backspace")]
    [InlineData("\b", "Backspace+Ctrl")]
    [InlineData("\x01", "'a'+Ctrl")]
    [InlineData("\x11", "'q'+Ctrl")]
    [InlineData("\x18\x1a", "'x'+Ctrl 'z'+Ctrl")]
    [InlineData("\n", "'j'+Ctrl")]
    [InlineData("\0", "' '+Ctrl")]
    [InlineData("\x1c\x1d\x1f", "'\\'+Ctrl ']'+Ctrl '_'+Ctrl")]
    [InlineData("\e[A\e[B\e[C\e[D", "Up Down Right Left")]
    [InlineData("\e[H\e[F", "Home End")]
    [InlineData("\eOA\eOH\eOF", "Up Home End")]
    [InlineData("\eOP\eOQ\eOR\eOS", "F1 F2 F3 F4")]
    [InlineData("\e[1;5A", "Up+Ctrl")]
    [InlineData("\e[1;2D", "Left+Shift")]
    [InlineData("\e[1;3C", "Right+Alt")]
    [InlineData("\e[1;8B", "Down+Shift,Alt,Ctrl")]
    [InlineData("\e[1;10A", "Up+Shift,Alt")]
    [InlineData("\e[1;5P\e[1;2Q\e[1;5R\e[1;3S", "F1+Ctrl F2+Shift F3+Ctrl F4+Alt")]
    [InlineData("\e[2~\e[3~\e[5~\e[6~", "Insert Delete PageUp PageDown")]
    [InlineData("\e[1~\e[4~\e[7~\e[8~", "Home End Home End")]
    [InlineData("\e[3;5~", "Delete+Ctrl")]
    [InlineData("\e[11~\e[15~\e[17~\e[21~\e[23~\e[24~", "F1 F5 F6 F10 F11 F12")]
    [InlineData("\e[24;2~", "F12+Shift")]
    [InlineData("\e[Z", "BackTab")]
    [InlineData("\e[1;2Z", "BackTab")]
    [InlineData("\eOM", "Enter")]
    [InlineData("\eOk\eOp", "'+' '0'")]
    [InlineData("\eO5A", "Up+Ctrl")]
    [InlineData("\e[27;5;13~", "Enter+Ctrl")]
    [InlineData("\e[27;6;65~", "'a'+Shift,Ctrl")]
    [InlineData("\e[97;5u", "'a'+Ctrl")]
    [InlineData("\e[13;2u", "Enter+Shift")]
    [InlineData("\ea", "'a'+Alt")]
    [InlineData("\eA", "'A'+Alt")]
    [InlineData("\e ", "' '+Alt")]
    [InlineData("\e\x7f", "Backspace+Alt")]
    [InlineData("\e\r", "Enter+Alt")]
    [InlineData("\e\x01", "'a'+Alt,Ctrl")]
    [InlineData("\eé", "'é'+Alt")]
    [InlineData("\e\e[A", "Up+Alt")]
    [InlineData("\e\ex", "Escape 'x'+Alt")]
    [InlineData("\e\e\e[B", "Escape Down+Alt")]
    public void DecodesKeys(string input, string expected)
    {
        Feed(input);
        Assert.False(decoder.HasPendingSequence);
        Assert.Equal(expected, Describe());
    }

    [Theory]
    [InlineData("\e[<0;1;1M", "Down Left 0,0")]
    [InlineData("\e[<0;10;5m", "Up Left 9,4")]
    [InlineData("\e[<1;3;4M\e[<2;3;4M", "Down Middle 2,3 | Down Right 2,3")]
    [InlineData("\e[<32;7;8M", "Drag Left 6,7")]
    [InlineData("\e[<34;7;8M", "Drag Right 6,7")]
    [InlineData("\e[<35;7;8M", "Move None 6,7")]
    [InlineData("\e[<64;2;2M\e[<65;2;2M", "WheelUp None 1,1 | WheelDown None 1,1")]
    [InlineData("\e[<4;1;1M", "Down Left 0,0 Shift")]
    [InlineData("\e[<8;1;1M", "Down Left 0,0 Alt")]
    [InlineData("\e[<16;1;1M", "Down Left 0,0 Ctrl")]
    [InlineData("\e[<80;1;1M", "WheelUp None 0,0 Ctrl")]
    [InlineData("\e[<66;1;1M\e[<128;1;1M", "")]
    [InlineData("\e[<0;300;200M", "Down Left 299,199")]
    [InlineData("\e[M !!\e[M#!!", "Down Left 0,0 | Up Left 0,0")]
    [InlineData("\e[M\"*+\e[MB*+\e[MC*+", "Down Right 9,10 | Drag Right 9,10 | Move None 9,10")]
    [InlineData("\e[M`!!\e[Ma!!", "WheelUp None 0,0 | WheelDown None 0,0")]
    [InlineData("\e[32;5;6M\e[35;5;6M", "Down Left 4,5 | Up Left 4,5")]
    public void DecodesMouse(string input, string expected)
    {
        Feed(input);
        Assert.False(decoder.HasPendingSequence);
        Assert.Equal(expected, string.Join(" | ", events.Cast<MouseEvent>().Select(m =>
            $"{m.Kind} {m.Button} {m.X},{m.Y}" + (m.Modifiers == KeyModifiers.None ? "" : " " + m.Modifiers.ToString().Replace(" ", "")))));
    }

    [Fact]
    public void X10MouseBytesAboveAsciiAreRaw()
    {
        // Coordinates 200,150 encode as bytes 0xe9/0xb7 which are not valid UTF-8 on their own.
        decoder.Feed([0x1b, (byte)'[', (byte)'M', 32, 233, 183, (byte)'x']);
        var mouse = Assert.IsType<MouseEvent>(events[0]);
        Assert.Equal((200, 150), (mouse.X, mouse.Y));
        Assert.Equal(new KeyEvent(Key.Character, new Rune('x')) { Timestamp = events[1].Timestamp }, events[1]);
    }

    [Fact]
    public void DecodesFocus()
    {
        Feed("\e[I\e[O");
        Assert.Equal(new InputEvent[] { new FocusEvent(true), new FocusEvent(false) }, events.Select(e => e with { Timestamp = 0 }));
    }

    [Theory]
    [InlineData("\e[c")]
    [InlineData("\e[?1;2c")]
    [InlineData("\e[>0;276;0c")]
    [InlineData("\e[0n")]
    [InlineData("\e[12;40R")]
    [InlineData("\e[1;1R")]
    [InlineData("\e[?2026;2$y")]
    [InlineData("\e[8;24;80t")]
    [InlineData("\e]11;rgb:0000/0000/0000\a")]
    [InlineData("\e]10;rgb:ffff/ffff/ffff\e\\")]
    [InlineData("\eP>|xterm(390)\e\\")]
    [InlineData("\eP1$r0m\e\\")]
    [InlineData("\e_Gi=1;OK\e\\")]
    [InlineData("\e[201~")]
    [InlineData("\e[?u")]
    [InlineData("\e[1;2;3;4;5;6;7;8;9;10A")]
    public void SwallowsReplies(string reply)
    {
        Feed("a" + reply + "b");
        Assert.Equal("'a' 'b'", Describe());
    }

    [Fact]
    public void SwallowsOverlongSequencesAndStrings()
    {
        Feed("a\e[" + new string('1', 500) + "mb\e]52;c;" + new string('Q', 5000) + "\aZ");
        Assert.Equal("'a' 'b' 'Z'", Describe());
    }

    [Fact]
    public void BracketedPasteIsNeverKeys()
    {
        Feed("x\e[200~hello\e[A\r\nwörld\x03\e[201~y");
        Assert.Equal("'x' Paste(hello\e[A\r\nwörld\x03) 'y'", Describe());
    }

    [Fact]
    public void BracketedPasteSurvivesArbitrarySplitsAndPartialTerminators()
    {
        string input = "\e[200~ab\e[20x\e[201中\e\e[201~z";
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        for (int split = 0; split <= bytes.Length; split++)
        {
            events.Clear();
            decoder.Feed(bytes.AsSpan(0, split));
            decoder.Feed(bytes.AsSpan(split));
            Assert.Equal("Paste(ab\e[20x\e[201中\e) 'z'", Describe());
        }
    }

    [Fact]
    public void LargePasteIsChunked()
    {
        var small = new InputDecoder(events.Add, new InputDecoderOptions { MaxPasteChunkBytes = 16 }, time);
        string text = string.Concat(Enumerable.Repeat("ab€", 20));
        small.Feed(Encoding.UTF8.GetBytes("\e[200~" + text + "\e[201~"));
        Assert.True(events.Count > 2);
        Assert.Equal(text, string.Concat(events.Cast<PasteEvent>().Select(p => p.Text)));
    }

    [Fact]
    public void EmptyPasteIsReported()
    {
        Feed("\e[200~\e[201~");
        Assert.Equal("Paste()", Describe());
    }

    [Fact]
    public void EveryTwoWaySplitDecodesIdentically()
    {
        const string input = "a\e[1;5A\e[<0;3;4M\eOP\e[3~é中\e[I\e]11;x\a\eb\e[200~p\e[201~\e[M !!\r";
        Feed(input);
        string expected = Describe();
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        for (int split = 1; split < bytes.Length; split++)
        {
            events.Clear();
            decoder.Feed(bytes.AsSpan(0, split));
            decoder.Feed(bytes.AsSpan(split));
            Assert.False(decoder.HasPendingSequence);
            Assert.Equal(expected, Describe());
        }

        events.Clear();
        foreach (byte b in bytes) decoder.Feed([b]);
        Assert.Equal(expected, Describe());
    }

    [Fact]
    public void LoneEscapeWaitsForTimeout()
    {
        Feed("\e");
        Assert.Empty(events);
        Assert.True(decoder.HasPendingSequence);
        Assert.Equal(TimeSpan.FromMilliseconds(50), decoder.TimeUntilTimeout);
        time.Advance(TimeSpan.FromMilliseconds(49));
        Assert.False(decoder.ResolveTimeout());
        Assert.Empty(events);
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(TimeSpan.Zero, decoder.TimeUntilTimeout);
        Assert.True(decoder.ResolveTimeout());
        Assert.Equal("Escape", Describe());
        Assert.Null(decoder.TimeUntilTimeout);
    }

    [Fact]
    public void SequenceSplitWithinTimeoutStillDecodes()
    {
        Feed("\e");
        time.Advance(TimeSpan.FromMilliseconds(30));
        Feed("[");
        time.Advance(TimeSpan.FromMilliseconds(30));
        Feed("A");
        Assert.Equal("Up", Describe());
    }

    [Fact]
    public void ExpiredEscapeIsResolvedBeforeNewBytes()
    {
        Feed("\e");
        time.Advance(TimeSpan.FromMilliseconds(80));
        Feed("[A");
        Assert.Equal("Escape '[' 'A'", Describe());
    }

    [Theory]
    [InlineData("\e[", "'['+Alt")]
    [InlineData("\e[1;", "'['+Alt '1' ';'")]
    [InlineData("\eO", "'O'+Alt")]
    [InlineData("\e]", "']'+Alt")]
    [InlineData("\ePabc", "'P'+Alt 'a' 'b' 'c'")]
    [InlineData("\e[M!", "'['+Alt 'M' '!'")]
    [InlineData("\e\e", "Escape Escape")]
    [InlineData("\e\xc3", "")]
    public void IncompleteSequencesTimeOutAsTypedKeys(string input, string expected)
    {
        if (input == "\e\xc3")
        {
            decoder.Feed([0x1b, 0xc3]);
            expected = "Escape '\uFFFD'";
        }
        else
        {
            Feed(input);
        }

        Assert.Empty(events);
        time.Advance(TimeSpan.FromMilliseconds(50));
        Assert.True(decoder.ResolveTimeout());
        decoder.Complete();
        Assert.Equal(expected, Describe());
    }

    [Fact]
    public void ControlInsideSequenceAbandonsIt()
    {
        Feed("\e[1\r");
        Assert.Equal("'['+Alt '1' Enter", Describe());
    }

    [Fact]
    public void Ss3FollowedByNonFinalIsAltO()
    {
        Feed("\eO\x7f");
        Assert.Equal("'O'+Alt Backspace", Describe());
    }

    [Fact]
    public void CompleteResolvesPendingAndPartialUtf8()
    {
        decoder.Feed([(byte)'a', 0xe4, 0xb8]);
        Assert.Equal("'a'", Describe());
        decoder.Complete();
        Assert.Equal("'a' '\uFFFD'", Describe());
        events.Clear();
        Feed("\e");
        decoder.Complete();
        Assert.Equal("Escape", Describe());
    }

    [Fact]
    public void InvalidUtf8BecomesReplacementCharacter()
    {
        decoder.Feed([0xff, (byte)'a', 0xc3, 0x28]);
        Assert.Equal("'\uFFFD' 'a' '\uFFFD' '('", Describe());
    }

    [Fact]
    public void PartialUtf8IsFlushedBeforeControls()
    {
        decoder.Feed([0xe4, 0x7f, 0xe4, 0x1b, (byte)'[', (byte)'A']);
        Assert.Equal("'\uFFFD' Backspace '\uFFFD' Up", Describe());
    }

    [Fact]
    public void BackspaceOptionTreats08AsPlainBackspace()
    {
        var plain = new InputDecoder(events.Add, new InputDecoderOptions { ControlHIsControlBackspace = false }, time);
        plain.Feed("\b\x7f"u8);
        Assert.Equal("Backspace Backspace", Describe());
    }

    [Fact]
    public void TimestampsComeFromTimeProvider()
    {
        time.Advance(TimeSpan.FromSeconds(3));
        long expected = time.GetTimestamp();
        Feed("a");
        Assert.Equal(expected, events[0].Timestamp);
    }

    [Fact]
    public void RandomBytesNeverThrowOrStayPendingAfterComplete()
    {
        var random = new Random(1);
        byte[] alphabet = "\e[]O<;0123456789MmAP~u\\\a\x7f\r abc"u8.ToArray();
        for (int round = 0; round < 500; round++)
        {
            byte[] data = new byte[random.Next(1, 40)];
            for (int i = 0; i < data.Length; i++)
                data[i] = random.Next(4) == 0 ? (byte)random.Next(256) : alphabet[random.Next(alphabet.Length)];
            int split = random.Next(data.Length + 1);
            decoder.Feed(data.AsSpan(0, split));
            if (random.Next(2) == 0) time.Advance(TimeSpan.FromMilliseconds(random.Next(100)));
            decoder.ResolveTimeout();
            decoder.Feed(data.AsSpan(split));
            decoder.Complete();
            Assert.False(decoder.HasPendingSequence);
        }
    }

    private void Feed(string text) => decoder.Feed(Encoding.UTF8.GetBytes(text));

    private string Describe() => string.Join(" ", events.Select(Describe));

    private static string Describe(InputEvent e) => e switch
    {
        KeyEvent { Key: Key.Character } k => $"'{k.Char}'" + Mods(k.Modifiers),
        KeyEvent k => k.Key + Mods(k.Modifiers),
        PasteEvent p => $"Paste({p.Text})",
        _ => e.ToString(),
    };

    private static string Mods(KeyModifiers modifiers) =>
        modifiers == KeyModifiers.None ? "" : "+" + modifiers.ToString().Replace(" ", "");
}
