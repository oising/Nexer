using System.Text;
using Nexer.Core;
using Nexer.Emulation;
using static Nexer.Rendering.Tests.RenderTestHelpers;

namespace Nexer.Rendering.Tests;

/// <summary>Feeds renderer output into the independent Nexer.Emulation terminal and compares screens.</summary>
public class VtRendererRoundTripTests
{
    private static readonly string[] Glyphs =
    [
        "a", "b", "Z", "0", " ", " ", " ", "~", "é", "e\u0301", "中", "日", "😀", "👍🏽", "Ω", "─", "│", "█",
        "\U0001F1FA", "\U0001F1F8", "\u0915\u094d\u0937",
    ];

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 7, 3)]
    [InlineData(3, 1, 9)]
    [InlineData(4, 30, 8)]
    [InlineData(5, 80, 24)]
    [InlineData(6, 13, 5)]
    public void RandomSuccessiveFramesMatchEmulator(int seed, int columns, int rows)
    {
        var random = new Random(seed);
        var surface = new CellSurface(columns, rows);
        var terminal = new Terminal(columns, rows);
        var renderer = new VtRenderer(columns, rows, new RendererOptions { SynchronizedOutput = seed % 2 == 0 });
        for (int frame = 0; frame < 60; frame++)
        {
            Mutate(random, surface, frame == 0 ? columns * rows : random.Next(0, Math.Max(2, columns * rows / 4)));
            RenderCursor cursor = random.Next(3) == 0 ? RenderCursor.Hidden
                : new RenderCursor(true, random.Next(columns), random.Next(rows));
            if (random.Next(8) == 0)
            {
                // A prepared but never written frame must not affect the baseline.
                using RenderFrame discarded = renderer.Prepare(surface, cursor);
            }

            Render(renderer, surface, cursor, terminal);
            AssertScreenMatches(surface, terminal, cursor, $"seed {seed} frame {frame}");
        }
    }

    [Fact]
    public void PartialWriteThenInvalidateRecovers()
    {
        var random = new Random(42);
        var surface = new CellSurface(20, 6);
        var terminal = new Terminal(20, 6);
        var renderer = new VtRenderer(20, 6);
        Mutate(random, surface, 120);
        Render(renderer, surface, default, terminal);
        for (int i = 0; i < 20; i++)
        {
            Mutate(random, surface, 30);
            using (RenderFrame frame = renderer.Prepare(surface, RenderCursor.Hidden))
            {
                ReadOnlySpan<byte> bytes = frame.Bytes.Span;
                terminal.Feed(bytes[..random.Next(bytes.Length + 1)]);
            }

            renderer.Invalidate();
            Render(renderer, surface, default, terminal);
            AssertScreenMatches(surface, terminal, default, $"iteration {i}");
        }
    }

    [Fact]
    public void ScrolledSurfaceMatchesEmulator()
    {
        var random = new Random(7);
        var surface = new CellSurface(12, 6);
        var terminal = new Terminal(12, 6);
        var renderer = new VtRenderer(12, 6);
        for (int i = 0; i < 30; i++)
        {
            Mutate(random, surface, 10);
            surface.ScrollUp(0, 5, random.Next(1, 3), new CellStyle(default, TerminalColor.Indexed((byte)random.Next(16))));
            Render(renderer, surface, default, terminal);
            AssertScreenMatches(surface, terminal, default, $"iteration {i}");
        }
    }

    [Fact]
    public void ReplacingTheSurfaceInstanceIsDetected()
    {
        var terminal = new Terminal(5, 2);
        var renderer = new VtRenderer(5, 2);
        var first = new CellSurface(5, 2);
        WriteText(first, 0, 0, "hello");
        Render(renderer, first, default, terminal);
        var second = new CellSurface(5, 2);
        WriteText(second, 0, 1, "world");
        Render(renderer, second, default, terminal);
        AssertScreenMatches(second, terminal, default);
    }

    [Theory]
    [InlineData(ColorMode.Indexed256)]
    [InlineData(ColorMode.Indexed16)]
    public void ProjectedColorsReachTheTerminalStably(ColorMode mode)
    {
        var surface = new CellSurface(4, 1);
        surface.Write(0, 0, new Rune('x'), style: new CellStyle(TerminalColor.Rgb(10, 200, 30), TerminalColor.Indexed(200)));
        var renderer = new VtRenderer(4, 1, new RendererOptions { Colors = mode });
        var terminal = new Terminal(4, 1);
        Render(renderer, surface, default, terminal);
        CellStyle style = terminal.Screen.GetRow(0)[0].Style;
        Assert.Equal(TerminalColorKind.Indexed, style.Foreground.Kind);
        Assert.Equal(TerminalColorKind.Indexed, style.Background.Kind);
        if (mode == ColorMode.Indexed16) Assert.True(style.Background.Value < 16);
        Assert.Equal("", Render(renderer, surface));
    }

    private static void Mutate(Random random, CellSurface surface, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int column = random.Next(surface.Columns), row = random.Next(surface.Rows);
            CellStyle style = RandomStyle(random);
            switch (random.Next(10))
            {
                case 0:
                    surface.Erase(column, row, random.Next(surface.Columns - column + 1), style with { Flags = CellStyleFlags.None });
                    break;
                case 1:
                    surface.Erase(column, row, surface.Columns - column, style with { Flags = random.Next(2) == 0 ? CellStyleFlags.None : CellStyleFlags.Inverse });
                    break;
                default:
                    string glyph = Glyphs[random.Next(Glyphs.Length)];
                    Rune[] runes = glyph.EnumerateRunes().ToArray();
                    surface.WriteCluster(column, row, runes, Core.Text.UnicodeText.GetWidth(runes), style);
                    break;
            }
        }
    }

    private static CellStyle RandomStyle(Random random)
    {
        return new CellStyle(RandomColor(random), RandomColor(random), (CellStyleFlags)random.Next(0, 256) & RandomMask(random));

        static CellStyleFlags RandomMask(Random random) => random.Next(3) == 0 ? (CellStyleFlags)0xff : CellStyleFlags.None;

        static TerminalColor RandomColor(Random random) => random.Next(5) switch
        {
            0 => TerminalColor.Indexed((byte)random.Next(16)),
            1 => TerminalColor.Indexed((byte)random.Next(256)),
            2 => TerminalColor.Rgb((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)),
            _ => TerminalColor.Default,
        };
    }
}
