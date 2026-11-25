using System.Text;
using Nexer.Core;
using static Nexer.Rendering.Tests.RenderTestHelpers;

namespace Nexer.Rendering.Tests;

public class VtRendererBytesTests
{
    private static readonly CellStyle BoldRed = new(TerminalColor.Indexed(1), default, CellStyleFlags.Bold);

    [Fact]
    public void FirstFrameRepaintsWithExplicitPositionAndRendition()
    {
        var surface = new CellSurface(4, 1);
        WriteText(surface, 0, 0, "ab");
        var renderer = new VtRenderer(4, 1);
        Assert.Equal("\e[?25l\e[H\e[mab  ", Render(renderer, surface));
    }

    [Fact]
    public void UnchangedSurfaceProducesEmptyFrame()
    {
        var surface = new CellSurface(4, 2);
        var renderer = new VtRenderer(4, 2);
        Render(renderer, surface);
        using RenderFrame frame = renderer.Prepare(surface, RenderCursor.Hidden);
        Assert.True(frame.IsEmpty);
    }

    [Fact]
    public void SingleCellChangeUsesShortMove()
    {
        var surface = new CellSurface(4, 1);
        var renderer = new VtRenderer(4, 1);
        Render(renderer, surface);
        surface.Write(3, 0, new Rune('x'));
        Assert.Equal("\e[4Gx", Render(renderer, surface));
    }

    [Fact]
    public void PendingWrapForbidsRelativeMoves()
    {
        var surface = new CellSurface(3, 1);
        WriteText(surface, 0, 0, "abc");
        var renderer = new VtRenderer(3, 1);
        Render(renderer, surface);
        surface.Write(2, 0, new Rune('x'));
        Assert.Equal("\e[3Gx", Render(renderer, surface));
    }

    [Fact]
    public void NearbyChangeUsesRelativeMoveOrGapRewrite()
    {
        var surface = new CellSurface(20, 1);
        var renderer = new VtRenderer(20, 1);
        Render(renderer, surface);
        surface.Write(2, 0, new Rune('a'));
        surface.Write(4, 0, new Rune('b'));
        surface.Write(15, 0, new Rune('c'));
        // Absolute column, rewrite of the one-cell gap instead of a move, then another column jump.
        Assert.Equal("\e[3Ga b\e[16Gc", Render(renderer, surface));
    }

    [Fact]
    public void ChangesOnDifferentRowsUseShortestMove()
    {
        var surface = new CellSurface(10, 5);
        var renderer = new VtRenderer(10, 5);
        Render(renderer, surface);
        surface.Write(0, 1, new Rune('a'));
        surface.Write(0, 2, new Rune('b'));
        surface.Write(7, 4, new Rune('c'));
        Assert.Equal("\e[2Ha\e[3Hb\e[5;8Hc", Render(renderer, surface));
    }

    [Fact]
    public void SgrIsMinimalAndCarriedAcrossRows()
    {
        var surface = new CellSurface(2, 2);
        var renderer = new VtRenderer(2, 2);
        Render(renderer, surface);
        surface.Write(0, 0, new Rune('a'), style: BoldRed);
        surface.Write(0, 1, new Rune('b'), style: BoldRed);
        surface.Write(1, 1, new Rune('c'), style: BoldRed with { Flags = CellStyleFlags.Faint });
        Assert.Equal("\e[H\e[1;31ma\e[2Hb\e[22;2mc", Render(renderer, surface));
    }

    [Fact]
    public void TrueColorIsEncodedDirectlyAndDefaultUsesReset()
    {
        var surface = new CellSurface(1, 1);
        var renderer = new VtRenderer(1, 1);
        surface.Write(0, 0, new Rune('x'), style: new CellStyle(TerminalColor.Indexed(1)));
        Render(renderer, surface);
        surface.Write(0, 0, new Rune('y'), style: new CellStyle(TerminalColor.Rgb(1, 2, 3)));
        Assert.Equal("\r\e[38;2;1;2;3my", Render(renderer, surface));
        surface.Write(0, 0, new Rune('z'));
        Assert.Equal("\r\e[mz", Render(renderer, surface));
    }

    [Theory]
    [InlineData(ColorMode.TrueColor, "\e[48;2;255;0;0m")]
    [InlineData(ColorMode.Indexed256, "\e[48;5;196m")]
    [InlineData(ColorMode.Indexed16, "\e[101m")]
    public void ColorsAreProjectedToTheColorMode(ColorMode mode, string sgr)
    {
        var surface = new CellSurface(1, 1);
        var renderer = new VtRenderer(1, 1, new RendererOptions { Colors = mode });
        Render(renderer, surface);
        surface.Write(0, 0, new Rune('x'), style: new CellStyle(default, TerminalColor.Rgb(255, 0, 0)));
        Assert.Equal($"\r{sgr}x", Render(renderer, surface));
    }

    [Fact]
    public void ErasesTailWithElOnlyWhenEquivalent()
    {
        var blue = new CellStyle(default, TerminalColor.Indexed(4));
        var surface = new CellSurface(10, 2);
        surface.Erase(0, 0, 10, blue);
        surface.Write(0, 0, new Rune('a'));
        surface.Erase(0, 1, 10, blue with { Flags = CellStyleFlags.Underline });
        var renderer = new VtRenderer(10, 2);
        string bytes = Render(renderer, surface);
        Assert.Equal("\e[?25l\e[H\e[ma\e[44m\e[K\e[2H\e[4m          ", bytes);
    }

    [Fact]
    public void WideGlyphAdvancesTwoColumns()
    {
        var surface = new CellSurface(3, 1);
        WriteText(surface, 0, 0, "中x");
        var renderer = new VtRenderer(3, 1);
        Assert.Equal("\e[?25l\e[H\e[m中x", Render(renderer, surface));
    }

    [Fact]
    public void OverwritingHalfOfAWideGlyphRewritesTheWholePair()
    {
        var surface = new CellSurface(4, 1);
        WriteText(surface, 0, 0, "中");
        var renderer = new VtRenderer(4, 1);
        Render(renderer, surface);
        surface.Write(1, 0, new Rune('a'));
        Assert.Equal("\r a", Render(renderer, surface));
    }

    [Fact]
    public void CursorIsPositionedAndShownAtFrameEnd()
    {
        var surface = new CellSurface(10, 3);
        var renderer = new VtRenderer(10, 3);
        Render(renderer, surface);
        Assert.Equal("\e[2;6H\e[?25h", Render(renderer, surface, new RenderCursor(true, 5, 1)));
        Assert.Equal("", Render(renderer, surface, new RenderCursor(true, 5, 1)));
        Assert.Equal("\e[C", Render(renderer, surface, new RenderCursor(true, 6, 1)));
        surface.Write(0, 0, new Rune('z'));
        // Content changes hide the cursor while painting, then restore it.
        Assert.Equal("\e[?25l\e[Hz\e[2;7H\e[?25h", Render(renderer, surface, new RenderCursor(true, 6, 1)));
        Assert.Equal("\e[?25l", Render(renderer, surface, RenderCursor.Hidden));
    }

    [Fact]
    public void SynchronizedOutputWrapsContentFrames()
    {
        var surface = new CellSurface(2, 1);
        var renderer = new VtRenderer(2, 1, new RendererOptions { SynchronizedOutput = true });
        Assert.Equal("\e[?2026h\e[H\e[m  \e[?25l\e[?2026l", Render(renderer, surface));
        // Cursor-only frames are not wrapped.
        Assert.Equal("\r\e[?25h", Render(renderer, surface, new RenderCursor(true, 0, 0)));
    }

    [Fact]
    public void DiscardedFrameLeavesBaselineUnchanged()
    {
        var surface = new CellSurface(4, 1);
        var renderer = new VtRenderer(4, 1);
        Render(renderer, surface);
        surface.Write(1, 0, new Rune('q'));
        string first;
        using (RenderFrame frame = renderer.Prepare(surface, RenderCursor.Hidden))
        {
            first = Encoding.UTF8.GetString(frame.Bytes.Span);
        }

        Assert.False(renderer.HasFrameInFlight);
        Assert.Equal(first, Render(renderer, surface));
        Assert.Equal("", Render(renderer, surface));
    }

    [Fact]
    public void OnlyOneFrameMayBeInFlight()
    {
        var surface = new CellSurface(2, 1);
        var renderer = new VtRenderer(2, 1);
        RenderFrame frame = renderer.Prepare(surface, RenderCursor.Hidden);
        Assert.Throws<InvalidOperationException>(() => renderer.Prepare(surface, RenderCursor.Hidden));
        frame.Commit();
        Assert.Throws<InvalidOperationException>(frame.Commit);
        Assert.Throws<ObjectDisposedException>(() => frame.Bytes);
        frame.Dispose();
    }

    [Fact]
    public void InvalidateDuringFlightMakesCommitStale()
    {
        var surface = new CellSurface(2, 1);
        var renderer = new VtRenderer(2, 1);
        RenderFrame frame = renderer.Prepare(surface, RenderCursor.Hidden);
        renderer.Invalidate();
        frame.Commit();
        Assert.Equal("\e[?25l\e[H\e[m  ", Render(renderer, surface));
    }

    [Fact]
    public void InvalidateAndResizeForceFullRepaint()
    {
        var surface = new CellSurface(3, 1);
        WriteText(surface, 0, 0, "abc");
        var renderer = new VtRenderer(3, 1);
        string full = Render(renderer, surface);
        renderer.Invalidate();
        Assert.Equal(full, Render(renderer, surface));
        renderer.Resize(4, 1);
        Assert.Equal("\e[?25l\e[H\e[mabc ", Render(renderer, surface));
    }

    [Fact]
    public void MismatchedSurfaceIsClippedAndPadded()
    {
        var surface = new CellSurface(5, 1);
        WriteText(surface, 0, 0, "abcd中");
        var renderer = new VtRenderer(5, 2);
        Assert.Equal("\e[?25l\e[H\e[mabcd \e[2H\e[K", Render(renderer, surface));
    }

    [Fact]
    public void AdjacentGlyphsThatWouldJoinAreSeparated()
    {
        var surface = new CellSurface(6, 1);
        // Two lone regional indicators would otherwise merge into one flag cluster.
        var indicatorU = new Rune(0x1F1FA);
        var indicatorS = new Rune(0x1F1F8);
        int width = Core.Text.UnicodeText.GetWidth([indicatorU]);
        surface.Write(0, 0, indicatorU, width);
        surface.Write(width, 0, indicatorS, width);
        var renderer = new VtRenderer(6, 1);
        string bytes = Render(renderer, surface);
        Assert.Contains($"{indicatorU}\e[{width + 1}G{indicatorS}", bytes);
    }

    [Fact]
    public void UnchangedFramesDoNotAllocate()
    {
        var surface = new CellSurface(80, 24);
        WriteText(surface, 0, 0, "hello");
        var renderer = new VtRenderer(80, 24);
        Render(renderer, surface);
        for (int i = 0; i < 3; i++) PrepareCommit(renderer, surface, i);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) PrepareCommit(renderer, surface, i);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        static void PrepareCommit(VtRenderer renderer, CellSurface surface, int i)
        {
            surface.Write(i % 80, 1, new Rune('a' + i % 26));
            using RenderFrame frame = renderer.Prepare(surface, new RenderCursor(true, i % 80, 2));
            frame.Commit();
        }
    }
}
