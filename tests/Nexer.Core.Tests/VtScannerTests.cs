using System.Text;
using Nexer.Core.Protocol;

namespace Nexer.Core.Tests;

public sealed class VtScannerTests
{
    [Fact]
    public void MixedStreamHasTheSameMeaningAtEverySplitAndByteByByte()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("A\u754c\U0001f600\u0301\r\n\u001b[?25l\u001b[38:2::1:2:3m\u001b(0"
            + "\u001b]2;title\u0007\u001bP1;2qdata\u001b\\Z");
        string[] expected =
        [
            "P:41", "P:754C", "P:1F600", "P:301", "C:0D", "C:0A",
            "Q:Csi:?25::l", "Q:Csi:38:2::1:2:3::m", "Q:Escape::(:0",
            "S:Osc:2;title", "S:Dcs:1;2qdata", "P:5A"
        ];

        for (int split = 0; split <= bytes.Length; split++)
        {
            var sink = new RecordingSink();
            var scanner = new VtScanner(sink);
            scanner.Feed(bytes.AsSpan(0, split));
            scanner.Feed(bytes.AsSpan(split));
            scanner.Complete();
            Assert.Equal(expected, sink.Events);
        }

        var byteSink = new RecordingSink();
        var byteScanner = new VtScanner(byteSink);
        for (int i = 0; i < bytes.Length; i++)
        {
            byteScanner.Feed(bytes.AsSpan(i, 1));
        }

        byteScanner.Complete();
        Assert.Equal(expected, byteSink.Events);
    }

    [Theory]
    [InlineData("X", VtStringKind.Sos)]
    [InlineData("^", VtStringKind.Pm)]
    [InlineData("_", VtStringKind.Apc)]
    [InlineData("P", VtStringKind.Dcs)]
    [InlineData("]", VtStringKind.Osc)]
    public void StringsRemainOpaqueUntilTheTerminator(string introducer, VtStringKind kind)
    {
        var sink = new RecordingSink();
        var scanner = new VtScanner(sink);

        scanner.Feed(Encoding.UTF8.GetBytes($"\u001b{introducer}hidden\u001b[31m\u001b\\X"));

        Assert.Equal([$"S:{kind}:hidden\u001b[31m", "P:58"], sink.Events);
    }

    [Fact]
    public void OversizedStringsDiscardUntilTerminatedWithoutLeakingOrDispatching()
    {
        var sink = new RecordingSink();
        var scanner = new VtScanner(sink, maxStringBytes: 3);

        scanner.Feed("\u001b]abcde\u001b[31msecret\u001b"u8);
        Assert.Empty(sink.Events);
        scanner.Feed("\\X\u001b]ok\u0007"u8);

        Assert.Equal(["P:58", "S:Osc:ok"], sink.Events);
    }

    [Fact]
    public void ExactlyBoundedPayloadIsAcceptedAndBellDoesNotTerminateDcs()
    {
        var sink = new RecordingSink();
        var scanner = new VtScanner(sink, maxStringBytes: 3);

        scanner.Feed("\u001b]abc\u0007\u001bPa\u0007b\u001b\\"u8);

        Assert.Equal(["S:Osc:abc", "S:Dcs:a\u0007b"], sink.Events);
    }

    [Theory]
    [InlineData("\u001b[12345mX")]
    [InlineData("\u001b[1 2mX")]
    public void OversizedOrMalformedHeadersAreIgnoredUntilFinal(string text)
    {
        var sink = new RecordingSink();
        var scanner = new VtScanner(sink, maxSequenceBytes: 3);

        scanner.Feed(Encoding.UTF8.GetBytes(text));

        Assert.Equal(["P:58"], sink.Events);
    }

    [Fact]
    public void ControlsExecuteInsideCsiAndEscapeRestartsASequence()
    {
        var sink = new RecordingSink();
        var scanner = new VtScanner(sink);

        scanner.Feed("\u001b[1\n;2H\u001b[123\u001b[2J"u8);

        Assert.Equal(["C:0A", "Q:Csi:1;2::H", "Q:Csi:2::J"], sink.Events);
    }

    [Theory]
    [InlineData(0x18)]
    [InlineData(0x1a)]
    public void CancellationEndsBothHeadersAndStrings(byte cancel)
    {
        var sink = new RecordingSink();
        var scanner = new VtScanner(sink);

        scanner.Feed("\u001b[123"u8);
        scanner.Feed([cancel, (byte)'X']);
        scanner.Feed("\u001b]secret"u8);
        scanner.Feed([cancel, (byte)'Y']);

        Assert.Equal(["P:58", "P:59"], sink.Events);
    }

    [Fact]
    public void InvalidUtf8IsReplacedAndControlsAreNotSwallowed()
    {
        var sink = new RecordingSink();
        var scanner = new VtScanner(sink);

        scanner.Feed([0xff, 0xe2, (byte)'X', 0xe2, 0x82, 0x0a, 0xc2, 0x9b]);
        scanner.Complete();

        Assert.Equal(["P:FFFD", "P:FFFD", "P:58", "P:FFFD", "C:0A", "P:9B"], sink.Events);
    }

    [Fact]
    public void CompletionReplacesTruncatedUtf8AndDiscardsIncompleteSequence()
    {
        var sink = new RecordingSink();
        var scanner = new VtScanner(sink);

        scanner.Feed([0xf0, 0x9f]);
        Assert.Empty(sink.Events);
        scanner.Complete();
        scanner.Feed("\u001b]unfinished"u8);
        scanner.Complete();
        scanner.Feed("X"u8);
        scanner.Complete();

        Assert.Equal(["P:FFFD", "P:58"], sink.Events);
    }

    [Fact]
    public void ScannerDoesNotInterpretRawC1AsAControlIntroducer()
    {
        var sink = new RecordingSink();
        var scanner = new VtScanner(sink);

        scanner.Feed([0x9b, (byte)'m', 0x7f]);

        Assert.Equal(["P:FFFD", "P:6D"], sink.Events);
    }

    [Fact]
    public void ReentrantCallbacksAreRejected()
    {
        var sink = new RecordingSink();
        var scanner = new VtScanner(sink);
        sink.OnPrint = () => Assert.Throws<InvalidOperationException>(() => scanner.Feed("Y"u8));

        scanner.Feed("X"u8);

        Assert.Equal(["P:58"], sink.Events);
    }

    [Fact]
    public void ScalarHotPathDoesNotAllocateAfterWarmup()
    {
        var scanner = new VtScanner(new NullSink());
        scanner.Feed("warmup\u001b[31m\u001b]title\u0007"u8);
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 100; i++)
        {
            scanner.Feed("ASCII \u754c \U0001f600\u001b[31m\u001b]title\u0007"u8);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private sealed class RecordingSink : IVtSink
    {
        public List<string> Events { get; } = [];
        public Action? OnPrint { get; set; }

        public void Print(Rune value)
        {
            Events.Add($"P:{value.Value:X}");
            OnPrint?.Invoke();
        }

        public void Execute(byte control) => Events.Add($"C:{control:X2}");

        public void Dispatch(VtSequenceKind kind, ReadOnlySpan<byte> parameters, ReadOnlySpan<byte> intermediates, byte final) =>
            Events.Add($"Q:{kind}:{Encoding.ASCII.GetString(parameters)}:{Encoding.ASCII.GetString(intermediates)}:{(char)final}");

        public void String(VtStringKind kind, ReadOnlySpan<byte> payload) =>
            Events.Add($"S:{kind}:{Encoding.UTF8.GetString(payload)}");
    }

    private sealed class NullSink : IVtSink
    {
        public void Print(Rune value) { }
        public void Execute(byte control) { }
        public void Dispatch(VtSequenceKind kind, ReadOnlySpan<byte> parameters, ReadOnlySpan<byte> intermediates, byte final) { }
        public void String(VtStringKind kind, ReadOnlySpan<byte> payload) { }
    }
}
