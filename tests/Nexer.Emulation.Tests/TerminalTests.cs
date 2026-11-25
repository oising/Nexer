using System.Text;
using Nexer.Core;

namespace Nexer.Emulation.Tests;

public sealed class TerminalTests
{
    [Fact]
    public void ShellTranscriptHasExpectedTextStyleCursorAndDamageAtEverySplit()
    {
        byte[] transcript = Encoding.UTF8.GetBytes("boot\r\n\u001b[32mready\u001b[0m\r\nworking...\r\u001b[2Kdone \u754c e\u0301");
        var expected = new Terminal(12, 4);
        expected.Feed(transcript);
        expected.CompleteInput();

        Assert.Equal("boot        ", Row(expected, 0));
        Assert.Equal("ready       ", Row(expected, 1));
        Assert.Equal("done \u754c e\u0301   ", Row(expected, 2));
        Assert.Equal((9, 2), (expected.CursorColumn, expected.CursorRow));
        Assert.Equal(TerminalColor.Indexed(2), expected.Screen.GetRow(1)[0].Style.Foreground);
        Assert.Equal(TerminalColor.Default, expected.Screen.GetRow(2)[0].Style.Foreground);
        Assert.True(expected.Screen.GetRowVersion(2) > expected.Screen.GetRowVersion(1));
        Assert.Equal(0ul, expected.Screen.GetRowVersion(3));

        for (int split = 0; split <= transcript.Length; split++)
        {
            var actual = new Terminal(12, 4);
            actual.Feed(transcript.AsSpan(0, split));
            actual.Feed(transcript.AsSpan(split));
            actual.CompleteInput();
            Equivalent(expected, actual);
        }

        var singleBytes = new Terminal(12, 4);
        for (int i = 0; i < transcript.Length; i++) singleBytes.Feed(transcript.AsSpan(i, 1));
        singleBytes.CompleteInput();
        Equivalent(expected, singleBytes);
    }

    [Fact]
    public void CursorOnlyCommandsDoNotDirtyCells()
    {
        var terminal = new Terminal(4, 2);
        terminal.Feed("X"u8);
        ulong version = terminal.Screen.Version;

        terminal.Feed("\u001b[2;4H\u001b[?25l"u8);

        Assert.Equal((3, 1), (terminal.CursorColumn, terminal.CursorRow));
        Assert.False(terminal.CursorVisible);
        Assert.Equal(version, terminal.Screen.Version);
    }

    [Fact]
    public void AutowrapWaitsForANewClusterAndSgrDoesNotClearPendingWrap()
    {
        var terminal = new Terminal(4, 2);
        terminal.Feed("ABCD"u8);
        Assert.True(terminal.WrapPending);
        Assert.Equal((3, 0), (terminal.CursorColumn, terminal.CursorRow));

        terminal.Feed("\u001b[31m"u8);
        Assert.True(terminal.WrapPending);
        terminal.Feed("E"u8);

        Assert.Equal("ABCD", Row(terminal, 0));
        Assert.Equal("E   ", Row(terminal, 1));
        Assert.Equal((1, 1), (terminal.CursorColumn, terminal.CursorRow));
        Assert.False(terminal.WrapPending);
    }

    [Fact]
    public void CombiningExtensionAtTheMarginDoesNotWrap()
    {
        var terminal = new Terminal(2, 2);
        terminal.Feed("Ae"u8);
        terminal.Feed("\u0301"u8);

        Assert.Equal("Ae\u0301", Row(terminal, 0));
        Assert.True(terminal.WrapPending);
        Assert.Equal(0, terminal.CursorRow);
        terminal.Feed("X"u8);
        Assert.Equal("X ", Row(terminal, 1));
    }

    [Fact]
    public void VariationSelectorGrowthAtRightMarginRepositionsTheCluster()
    {
        var terminal = new Terminal(4, 2);
        terminal.Feed("ABC\u2764"u8);
        terminal.Feed("\ufe0f"u8);

        Assert.Equal("ABC ", Row(terminal, 0));
        Assert.Equal("\u2764\ufe0f  ", Row(terminal, 1));
        Assert.Equal(CellRole.WideLead, terminal.Screen.GetRow(1)[0].Role);
        Assert.Equal(CellRole.WideContinuation, terminal.Screen.GetRow(1)[1].Role);
        Assert.Equal((2, 1), (terminal.CursorColumn, terminal.CursorRow));
    }

    [Fact]
    public void WidthGrowthAtBottomMarginScrollsExactlyOnce()
    {
        var terminal = new Terminal(4, 2);
        terminal.Feed("old\u001b[2;1Hxyz\u2764"u8);
        terminal.Feed("\ufe0f"u8);

        Assert.Equal("xyz ", Row(terminal, 0));
        Assert.Equal("\u2764\ufe0f  ", Row(terminal, 1));
        Assert.Equal((2, 1), (terminal.CursorColumn, terminal.CursorRow));
    }

    [Fact]
    public void EmojiTextRevisionShrinksTheCellFootprint()
    {
        var terminal = new Terminal(4, 1);
        terminal.Feed("\U0001f1e6"u8);
        Assert.Equal(2, terminal.CursorColumn);
        terminal.Feed("\ufe0e"u8);

        Assert.Equal("\U0001f1e6\ufe0e", terminal.Screen.GetText(0, 0));
        Assert.Equal(CellRole.Single, terminal.Screen.GetRow(0)[0].Role);
        Assert.Equal(Cell.Blank, terminal.Screen.GetRow(0)[1]);
        Assert.Equal(1, terminal.CursorColumn);
    }

    [Theory]
    [InlineData("\U0001f469\u200d\U0001f4bb")]
    [InlineData("\U0001f1fa\U0001f1f8")]
    [InlineData("#\ufe0f\u20e3")]
    [InlineData("\u1100\u1161\u11a8")]
    public void ComplexClustersRemainOneWideGlyphAcrossEveryByte(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        var terminal = new Terminal(8, 2);
        for (int i = 0; i < bytes.Length; i++) terminal.Feed(bytes.AsSpan(i, 1));

        Assert.Equal(text, terminal.Screen.GetText(0, 0));
        Assert.Equal(CellRole.WideLead, terminal.Screen.GetRow(0)[0].Role);
        Assert.Equal(CellRole.WideContinuation, terminal.Screen.GetRow(0)[1].Role);
        Assert.Equal(2, terminal.CursorColumn);
    }

    [Fact]
    public void WideTextWrapsBeforeWritingAndDisabledAutowrapClips()
    {
        var terminal = new Terminal(4, 2);
        terminal.Feed("ABC\u754c"u8);
        Assert.Equal("ABC ", Row(terminal, 0));
        Assert.Equal("\u754c  ", Row(terminal, 1));

        terminal.Feed("\u001b[?7l\u001b[1;4H\u754cX"u8);
        Assert.Equal("ABCX", Row(terminal, 0));
        Assert.False(terminal.WrapPending);
        Assert.Equal((3, 0), (terminal.CursorColumn, terminal.CursorRow));
    }

    [Fact]
    public void OneColumnWideTextDoesNotLoopOrPreScroll()
    {
        var terminal = new Terminal(1, 2);
        terminal.Feed("\u754c"u8);
        Assert.Equal(" ", Row(terminal, 0));
        Assert.Equal(0, terminal.CursorRow);
        Assert.True(terminal.WrapPending);
        terminal.Feed("X"u8);
        Assert.Equal("X", Row(terminal, 1));
    }

    [Fact]
    public void OversizedClusterReplacesAndDiscardsExtensionsWithoutLosingTheNextBase()
    {
        var terminal = new Terminal(4, 2);
        terminal.Feed(Encoding.UTF8.GetBytes("e" + new string('\u0301', 1000) + "A"));

        Assert.Equal("\ufffdA  ", Row(terminal, 0));
        Assert.Equal(0, terminal.Screen.RetainedClusterBytes);
        Assert.Equal(2, terminal.CursorColumn);
    }

    [Theory]
    [InlineData("\u0301\u0302")]
    [InlineData("\u093e")]
    public void LeadingCombiningMarksUseADottedCircle(string marks)
    {
        var terminal = new Terminal(4, 1);
        terminal.Feed(Encoding.UTF8.GetBytes(marks + "X"));

        Assert.Equal("\u25cc" + marks, terminal.Screen.GetText(0, 0));
        Assert.Equal("X", terminal.Screen.GetText(1, 0));
    }

    [Theory]
    [InlineData("e\u001b]oversized string\u0007\u0301")]
    [InlineData("e\u001b[12\u0018\u0301")]
    [InlineData("e\u007f\u0301")]
    [InlineData("e\u0000\u0301")]
    public void IgnoredSequencesAndControlsStillBreakTextContinuity(string text)
    {
        var terminal = new Terminal(4, 1, new TerminalOptions { MaxControlStringBytes = 2 });
        terminal.Feed(Encoding.UTF8.GetBytes(text));

        Assert.Equal("e", terminal.Screen.GetText(0, 0));
        Assert.Equal("\u25cc\u0301", terminal.Screen.GetText(1, 0));
    }

    [Fact]
    public void LfDoesNotImplyCrAndBottomLfScrolls()
    {
        var terminal = new Terminal(4, 2);
        terminal.Feed("A\nB"u8);
        Assert.Equal("A   ", Row(terminal, 0));
        Assert.Equal(" B  ", Row(terminal, 1));
        terminal.Feed("\r\nC"u8);
        Assert.Equal(" B  ", Row(terminal, 0));
        Assert.Equal("C   ", Row(terminal, 1));
    }

    [Fact]
    public void RegionScrollingLeavesOutsideRowsUntouched()
    {
        var terminal = new Terminal(4, 4);
        terminal.Feed("A\u001b[2;1HB\u001b[3;1HC\u001b[4;1HD"u8);
        ulong first = terminal.Screen.GetRowVersion(0), last = terminal.Screen.GetRowVersion(3);

        terminal.Feed("\u001b[2;3r\u001b[3;1H\n"u8);
        Assert.Equal("A   ", Row(terminal, 0));
        Assert.Equal("C   ", Row(terminal, 1));
        Assert.Equal("    ", Row(terminal, 2));
        Assert.Equal("D   ", Row(terminal, 3));
        Assert.Equal(first, terminal.Screen.GetRowVersion(0));
        Assert.Equal(last, terminal.Screen.GetRowVersion(3));

        terminal.Feed("\u001b[2;1H\u001bM"u8);
        Assert.Equal("    ", Row(terminal, 1));
        Assert.Equal("C   ", Row(terminal, 2));
    }

    [Fact]
    public void OriginModeAndRelativeMotionRespectMargins()
    {
        var terminal = new Terminal(8, 5);
        terminal.Feed("\u001b[2;4r\u001b[?6h"u8);
        Assert.Equal((0, 1), (terminal.CursorColumn, terminal.CursorRow));
        terminal.Feed("\u001b[99;99H"u8);
        Assert.Equal((7, 3), (terminal.CursorColumn, terminal.CursorRow));
        terminal.Feed("\u001b[99A"u8);
        Assert.Equal(1, terminal.CursorRow);
        terminal.Feed("\u001b[?6l"u8);
        Assert.Equal((0, 0), (terminal.CursorColumn, terminal.CursorRow));
        terminal.Feed("\u001b[99B"u8);
        Assert.Equal(3, terminal.CursorRow);
    }

    [Fact]
    public void InvalidMarginsAreIgnoredAndValidMarginsHome()
    {
        var terminal = new Terminal(8, 4);
        terminal.Feed("\u001b[3;4H\u001b[3;2r"u8);
        Assert.Equal((3, 2), (terminal.CursorColumn, terminal.CursorRow));
        Assert.Equal((0, 3), (terminal.ScrollTop, terminal.ScrollBottom));
        terminal.Feed("\u001b[2;3r"u8);
        Assert.Equal((0, 0), (terminal.CursorColumn, terminal.CursorRow));
        Assert.Equal((1, 2), (terminal.ScrollTop, terminal.ScrollBottom));
    }

    [Fact]
    public void ErasingUsesColorsWithoutStyleFlagsAndRepairsWideHalves()
    {
        var terminal = new Terminal(6, 2);
        terminal.Feed("A\u754cBC\u001b[1;3H\u001b[1;31;44m\u001b[X"u8);

        Assert.Equal("A  BC ", Row(terminal, 0));
        Assert.Equal(Cell.Blank, terminal.Screen.GetRow(0)[1]);
        Assert.Equal(new CellStyle(TerminalColor.Indexed(1), TerminalColor.Indexed(4)), terminal.Screen.GetRow(0)[2].Style);
        Assert.Equal(CellStyleFlags.Bold, terminal.CurrentStyle.Flags);
        Assert.Equal((2, 0), (terminal.CursorColumn, terminal.CursorRow));
    }

    [Theory]
    [InlineData(0, "ABCDEF", "gh    ")]
    [InlineData(1, "      ", "   jkl")]
    [InlineData(2, "      ", "      ")]
    public void EdModesIncludeTheCursorAndDoNotMoveIt(int mode, string first, string second)
    {
        var terminal = new Terminal(6, 2);
        terminal.Feed(Encoding.UTF8.GetBytes($"ABCDEF\r\nghijkl\u001b[2;3H\u001b[{mode}J"));

        Assert.Equal(first, Row(terminal, 0));
        Assert.Equal(second, Row(terminal, 1));
        Assert.Equal((2, 1), (terminal.CursorColumn, terminal.CursorRow));
    }

    [Theory]
    [InlineData(0, "AB    ")]
    [InlineData(1, "   DEF")]
    [InlineData(2, "      ")]
    public void ElModesIncludeTheCursor(int mode, string expected)
    {
        var terminal = new Terminal(6, 1);
        terminal.Feed(Encoding.UTF8.GetBytes($"ABCDEF\u001b[3G\u001b[{mode}K"));
        Assert.Equal(expected, Row(terminal, 0));
        Assert.False(terminal.WrapPending);
    }

    [Fact]
    public void TabsCanBeSetAndCleared()
    {
        var terminal = new Terminal(20, 2);
        terminal.Feed("\t"u8);
        Assert.Equal(8, terminal.CursorColumn);
        terminal.Feed("\u001b[3g\r\t"u8);
        Assert.Equal(19, terminal.CursorColumn);
        terminal.Feed("\u001b[5G\u001bH\r\t"u8);
        Assert.Equal(4, terminal.CursorColumn);
        terminal.Feed("\u001b[g\r\t"u8);
        Assert.Equal(19, terminal.CursorColumn);
    }

    [Fact]
    public void SaveRestoreAndRisHaveDefinedState()
    {
        var terminal = new Terminal(8, 4);
        terminal.Feed("\u001b[2;4r\u001b[?6h\u001b[?7l\u001b[31m\u001b[2;3H\u001b7"u8);
        terminal.Feed("\u001b[?6l\u001b[?7h\u001b[0m\u001b8"u8);
        Assert.Equal((2, 2), (terminal.CursorColumn, terminal.CursorRow));
        Assert.True(terminal.OriginMode);
        Assert.False(terminal.AutoWrap);
        Assert.Equal(TerminalColor.Indexed(1), terminal.CurrentStyle.Foreground);
        terminal.Feed("e\u0301\u001b[?25l\u001bc"u8);
        Assert.Equal((0, 0), (terminal.CursorColumn, terminal.CursorRow));
        Assert.Equal((0, 3), (terminal.ScrollTop, terminal.ScrollBottom));
        Assert.True(terminal.AutoWrap);
        Assert.True(terminal.CursorVisible);
        Assert.False(terminal.OriginMode);
        Assert.Equal(default, terminal.CurrentStyle);
        Assert.Equal(0, terminal.Screen.RetainedClusterBytes);
        Assert.Equal("        ", Row(terminal, 2));
    }

    [Fact]
    public void CompletingInputIsNotAResetAndFeedIsNotEof()
    {
        var terminal = new Terminal(6, 1);
        terminal.Feed([0xe2, 0x82]);
        Assert.Equal(0ul, terminal.Screen.Version);
        terminal.CompleteInput();
        terminal.Feed("\u0301"u8);
        Assert.Equal("\ufffd", terminal.Screen.GetText(0, 0));
        Assert.Equal("\u25cc\u0301", terminal.Screen.GetText(1, 0));
    }

    [Fact]
    public void NumericLimitsRejectTheWholeCommandWithoutPartialChanges()
    {
        var terminal = new Terminal(4, 2);
        terminal.Feed("\u001b[31;99999999m\u001b[99999999;2H"u8);
        Assert.Equal(default, terminal.CurrentStyle);
        Assert.Equal((0, 0), (terminal.CursorColumn, terminal.CursorRow));
        terminal.Feed(Encoding.UTF8.GetBytes("\u001b[" + string.Join(';', Enumerable.Repeat("1", 33)) + "m"));
        Assert.Equal(default, terminal.CurrentStyle);
    }

    [Fact]
    public void HostSideEffectSequencesAreNotExecutedOrPrinted()
    {
        var terminal = new Terminal(8, 2);
        terminal.Feed("A\u001b]52;c;secret\u001b\\\u001bPcommands\u001b\\B\u001b[8;100;200t"u8);
        Assert.Equal("AB      ", Row(terminal, 0));
        Assert.Equal((8, 2), (terminal.Screen.Columns, terminal.Screen.Rows));
    }

    [Fact]
    public void BellIsAnEventWithoutCellDamage()
    {
        var terminal = new Terminal(4, 2);
        int count = 0;
        terminal.Bell += (_, _) => count++;
        terminal.Feed("\a"u8);
        Assert.Equal(1, count);
        Assert.Equal(0ul, terminal.Screen.Version);
    }

    [Fact]
    public void AmbiguousWidthAndClusterBudgetAreConfigurable()
    {
        var terminal = new Terminal(8, 1, new TerminalOptions { AmbiguousIsWide = true, MaxClusterBytes = 0 });
        terminal.Feed("\u00a1e\u0301"u8);
        Assert.Equal(CellRole.WideLead, terminal.Screen.GetRow(0)[0].Role);
        Assert.Equal("\ufffd", terminal.Screen.GetText(2, 0));
        Assert.Equal(0, terminal.Screen.RetainedClusterBytes);
    }

    [Fact]
    public void ScalarEmulationHotPathDoesNotAllocate()
    {
        var terminal = new Terminal(80, 24);
        terminal.Feed("warmup\r\n\u001b[31m"u8);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) terminal.Feed("hello \u754c\r\n\u001b[32m"u8);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Theory]
    [InlineData("ABC\u2764\ufe0fX")]
    [InlineData("old\u001b[2;1Hxyz\u2764\ufe0fX")]
    [InlineData("\U0001f1e6\ufe0eX")]
    [InlineData("ABC\u754c\u0301X")]
    [InlineData("e\u001b]oversized string\u0007\u0301X")]
    public void MarginRevisionsAndBarriersAreInvariantAtEveryByteSplit(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        var options = new TerminalOptions { MaxControlStringBytes = 2 };
        var expected = new Terminal(4, 2, options);
        expected.Feed(bytes);
        expected.CompleteInput();
        for (int split = 0; split <= bytes.Length; split++)
        {
            var actual = new Terminal(4, 2, options);
            actual.Feed(bytes.AsSpan(0, split));
            actual.Feed(bytes.AsSpan(split));
            actual.CompleteInput();
            Equivalent(expected, actual);
        }
    }

    [Fact]
    public void RandomByteInputIsBoundedChunkInvariantAndPreservesWideCellInvariants()
    {
        var random = new Random(7461);
        byte[] bytes = new byte[8192];
        random.NextBytes(bytes);
        byte[] tail = Encoding.UTF8.GetBytes("\u0018\u001b[0m\u754c\U0001f469\u200d\U0001f4bb");
        bytes = [.. bytes, .. tail];
        var options = new TerminalOptions { MaxClusterBytes = 128, MaxControlStringBytes = 32 };
        var expected = new Terminal(13, 5, options);
        expected.Feed(bytes);
        expected.CompleteInput();
        var actual = new Terminal(13, 5, options);
        for (int offset = 0; offset < bytes.Length;)
        {
            int length = Math.Min(bytes.Length - offset, random.Next(1, 24));
            actual.Feed(bytes.AsSpan(offset, length));
            offset += length;
            Assert.InRange(actual.CursorColumn, 0, 12);
            Assert.InRange(actual.CursorRow, 0, 4);
            Assert.InRange(actual.Screen.RetainedClusterBytes, 0, 128);
            for (int row = 0; row < 5; row++)
            {
                ReadOnlySpan<Cell> cells = actual.Screen.GetRow(row);
                for (int column = 0; column < 13; column++)
                {
                    if (cells[column].Role == CellRole.WideLead)
                        Assert.True(column < 12 && cells[column + 1].Role == CellRole.WideContinuation);
                    if (cells[column].Role == CellRole.WideContinuation)
                        Assert.True(column > 0 && cells[column - 1].Role == CellRole.WideLead);
                }
            }
        }

        actual.CompleteInput();
        Equivalent(expected, actual);
    }

    private static string Row(Terminal terminal, int row)
    {
        var text = new StringBuilder();
        for (int column = 0; column < terminal.Screen.Columns; column++) text.Append(terminal.Screen.GetText(column, row));
        return text.ToString();
    }

    private static void Equivalent(Terminal expected, Terminal actual)
    {
        Assert.Equal((expected.CursorColumn, expected.CursorRow, expected.CursorVisible, expected.WrapPending),
            (actual.CursorColumn, actual.CursorRow, actual.CursorVisible, actual.WrapPending));
        Assert.Equal(expected.CurrentStyle, actual.CurrentStyle);
        Assert.Equal(expected.AutoWrap, actual.AutoWrap);
        Assert.Equal(expected.OriginMode, actual.OriginMode);
        Assert.Equal((expected.ScrollTop, expected.ScrollBottom), (actual.ScrollTop, actual.ScrollBottom));
        Assert.Equal(expected.Screen.Version, actual.Screen.Version);
        Assert.Equal(expected.Screen.RetainedClusterBytes, actual.Screen.RetainedClusterBytes);
        for (int row = 0; row < expected.Screen.Rows; row++)
        {
            Assert.Equal(Row(expected, row), Row(actual, row));
            Assert.Equal(expected.Screen.GetRowVersion(row), actual.Screen.GetRowVersion(row));
            for (int column = 0; column < expected.Screen.Columns; column++)
            {
                Assert.Equal(expected.Screen.GetRow(row)[column], actual.Screen.GetRow(row)[column]);
                Assert.Equal(expected.Screen.GetText(column, row), actual.Screen.GetText(column, row));
            }
        }
    }
}
