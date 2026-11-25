using System.Text;

namespace Nexer.Core.Tests;

public sealed class CellSurfaceTests
{
    [Fact]
    public void SingleCellChangesOnlyItsRowAndIdenticalWritesAreClean()
    {
        var surface = new CellSurface(4, 2);
        surface.Write(1, 1, new Rune('X'));
        ulong written = surface.Version;

        surface.Write(1, 1, new Rune('X'));

        Assert.Equal(0ul, surface.GetRowVersion(0));
        Assert.Equal(written, surface.GetRowVersion(1));
        Assert.Equal(written, surface.Version);
        Assert.Equal(new Rune('X'), surface.GetRow(1)[1].Glyph);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void OverwritingEitherWideHalfRepairsItsPartner(int column)
    {
        var surface = new CellSurface(4, 1);
        surface.Write(0, 0, new Rune(0x754c), width: 2);

        surface.Write(column, 0, new Rune('X'));

        Assert.Equal(Cell.Create(new Rune('X')), surface.GetRow(0)[column]);
        Assert.Equal(Cell.Blank, surface.GetRow(0)[1 - column]);
    }

    [Fact]
    public void WideWriteRepairsBothIntersectedGlyphs()
    {
        var surface = new CellSurface(4, 1);
        surface.Write(0, 0, new Rune(0x754c), width: 2);
        surface.Write(2, 0, new Rune(0x754c), width: 2);

        surface.Write(1, 0, new Rune(0x8a9e), width: 2);
        ulong version = surface.Version;
        surface.Write(1, 0, new Rune(0x8a9e), width: 2);

        Assert.Equal(Cell.Blank, surface.GetRow(0)[0]);
        Assert.Equal(CellRole.WideLead, surface.GetRow(0)[1].Role);
        Assert.Equal(CellRole.WideContinuation, surface.GetRow(0)[2].Role);
        Assert.Equal(Cell.Blank, surface.GetRow(0)[3]);
        Assert.Equal(version, surface.Version);
    }

    [Fact]
    public void ClippedWideWriteBlanksTheEdgeAndRepairsTheOldLead()
    {
        var surface = new CellSurface(2, 1);
        surface.Write(0, 0, new Rune(0x754c), width: 2);
        var style = new CellStyle(Background: TerminalColor.Indexed(4));

        surface.Write(1, 0, new Rune(0x754c), width: 2, style);

        Assert.Equal(Cell.Blank, surface.GetRow(0)[0]);
        Assert.Equal(Cell.Create(new Rune(' '), style), surface.GetRow(0)[1]);
    }

    [Fact]
    public void ClearInvalidatesOnlyChangedRowsAndCanBeRepeated()
    {
        var surface = new CellSurface(2, 2);
        surface.Write(0, 1, new Rune('X'));

        surface.Clear();
        ulong cleared = surface.Version;
        surface.Clear();

        Assert.Equal(0ul, surface.GetRowVersion(0));
        Assert.Equal(cleared, surface.GetRowVersion(1));
        Assert.Equal(cleared, surface.Version);
        Assert.Equal(Cell.Blank, surface.GetRow(1)[0]);
    }

    [Fact]
    public void InvalidWritesDoNotMutateTheSurface()
    {
        var surface = new CellSurface(2, 1);
        surface.Write(0, 0, new Rune('X'));
        ulong version = surface.Version;

        Assert.Throws<ArgumentException>(() => surface.Write(0, 0, new Rune('\n')));
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.Write(0, 0, new Rune('X'), width: 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.Write(2, 0, new Rune('X')));
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.Write(0, -1, new Rune('X')));
        Assert.Equal(version, surface.Version);
        Assert.Equal(new Rune('X'), surface.GetRow(0)[0].Glyph);
    }

    [Theory]
    [InlineData(0, 1, 10)]
    [InlineData(1, -1, 10)]
    [InlineData(4, 3, 10)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue)]
    public void GeometryIsValidatedBeforeAllocation(int columns, int rows, int limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CellSurface(columns, rows, limit));
    }

    [Fact]
    public void DefaultColorsAreNotPaletteColorsAndDefaultCellsAreCanonicalBlanks()
    {
        Assert.NotEqual(TerminalColor.Default, TerminalColor.Indexed(0));
        Assert.NotEqual(TerminalColor.Indexed(0), TerminalColor.Rgb(0, 0, 0));
        Assert.Equal(Cell.Blank, Cell.Create(new Rune(' ')));
    }

    [Fact]
    public void ClustersAreDeduplicatedAndReleasedWhenNoCellsReferenceThem()
    {
        var surface = new CellSurface(4, 1, maxClusterBytes: 4);
        Rune[] text = [new Rune('e'), new Rune(0x301)];
        surface.WriteCluster(0, 0, text, 1);
        surface.WriteCluster(1, 0, text, 1);
        Assert.Equal(4, surface.RetainedClusterBytes);
        Assert.Equal("e\u0301", surface.GetText(0, 0));
        Assert.Equal(surface.GetRow(0)[0], surface.GetRow(0)[1]);
        surface.Erase(0, 0, 1);
        Assert.Equal(4, surface.RetainedClusterBytes);
        surface.Erase(1, 0, 1);
        Assert.Equal(0, surface.RetainedClusterBytes);

        surface.WriteCluster(0, 0, [new Rune('a'), new Rune(0x302)], 1);
        Assert.Equal("a\u0302", surface.GetText(0, 0));
        surface.Clear();
        Assert.Equal(0, surface.RetainedClusterBytes);
    }

    [Fact]
    public void ClusterBudgetSubstitutesWithoutGrowingStorage()
    {
        var surface = new CellSurface(2, 1, maxClusterBytes: 0);
        surface.WriteCluster(0, 0, [new Rune('e'), new Rune(0x301)], 1);
        Assert.Equal("\ufffd", surface.GetText(0, 0));
        Assert.Equal(0, surface.RetainedClusterBytes);
    }

    [Fact]
    public void ErasingAHalfRepairsTheWideGlyphAndUsesRequestedEraseStyle()
    {
        var surface = new CellSurface(3, 1);
        surface.Write(0, 0, new Rune(0x754c), 2);
        var style = new CellStyle(Background: TerminalColor.Rgb(10, 20, 30));
        surface.Erase(1, 0, 1, style);
        Assert.Equal(Cell.Blank, surface.GetRow(0)[0]);
        Assert.Equal(Cell.Create(new Rune(' '), style), surface.GetRow(0)[1]);
    }

    [Fact]
    public void RegionScrollingRotatesRowsAndReleasesEvictedClusters()
    {
        var surface = new CellSurface(2, 4);
        surface.Write(0, 0, new Rune('A'));
        surface.WriteCluster(0, 1, [new Rune('e'), new Rune(0x301)], 1);
        surface.Write(0, 2, new Rune('C'));
        surface.Write(0, 3, new Rune('D'));
        ulong untouched = surface.GetRowVersion(0);

        surface.ScrollUp(1, 2);

        Assert.Equal("A", surface.GetText(0, 0));
        Assert.Equal("C", surface.GetText(0, 1));
        Assert.Equal(" ", surface.GetText(0, 2));
        Assert.Equal("D", surface.GetText(0, 3));
        Assert.Equal(untouched, surface.GetRowVersion(0));
        Assert.Equal(0, surface.RetainedClusterBytes);

        surface.ScrollDown(1, 3, count: 2);
        Assert.Equal(" ", surface.GetText(0, 1));
        Assert.Equal(" ", surface.GetText(0, 2));
        Assert.Equal("C", surface.GetText(0, 3));
    }

    [Fact]
    public void ScrollingMoreThanTheRegionHeightClearsOnlyTheRegion()
    {
        var surface = new CellSurface(1, 3);
        for (int row = 0; row < 3; row++) surface.Write(0, row, new Rune('X'));
        surface.ScrollUp(1, 2, int.MaxValue);
        Assert.Equal("X", surface.GetText(0, 0));
        Assert.Equal(" ", surface.GetText(0, 1));
        Assert.Equal(" ", surface.GetText(0, 2));
    }
}
