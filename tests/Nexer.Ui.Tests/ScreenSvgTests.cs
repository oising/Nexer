using System.Text;
using System.Xml.Linq;
using Nexer.Core;
using Nexer.Ui.Testing;

namespace Nexer.Ui.Tests;

public class ScreenSvgTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    private static XDocument Render(CellSurface surface, SvgOptions? options = null) =>
        XDocument.Parse(ScreenDump.Svg(surface, options));

    private static void Put(CellSurface surface, int x, int y, string text, CellStyle style = default)
    {
        foreach (var rune in text.EnumerateRunes()) surface.Write(x++, y, rune, 1, style);
    }

    [Fact]
    public void TextIsEscapedAndSizedToTheGrid()
    {
        var surface = new CellSurface(10, 2);
        Put(surface, 1, 0, "a<&>\"b");
        var doc = Render(surface, new SvgOptions { CellWidth = 10, CellHeight = 20, Title = "T & <x>" });

        var root = doc.Root!;
        Assert.Equal("100", (string?)root.Attribute("width"));
        Assert.Equal("40", (string?)root.Attribute("height"));
        Assert.Equal("T & <x>", root.Element(Svg + "title")!.Value);
        var text = Assert.Single(root.Elements(Svg + "text"));
        Assert.Equal("a<&>\"b", text.Value);
        Assert.Equal("10", (string?)text.Attribute("x"));
        Assert.Equal("60", (string?)text.Attribute("textLength"));
    }

    [Fact]
    public void ColoursFollowThePaletteBoldBrightAndInverse()
    {
        var surface = new CellSurface(4, 1);
        Put(surface, 0, 0, "a", new CellStyle(TerminalColor.Indexed(1), TerminalColor.Indexed(4)));
        Put(surface, 1, 0, "b", new CellStyle(TerminalColor.Indexed(1), default, CellStyleFlags.Bold));
        Put(surface, 2, 0, "c", new CellStyle(TerminalColor.Rgb(0x12, 0x34, 0x56), TerminalColor.Indexed(196), CellStyleFlags.Inverse));
        Put(surface, 3, 0, "d", new CellStyle(TerminalColor.Indexed(244)));
        var root = Render(surface).Root!;

        var fills = root.Descendants(Svg + "text").ToDictionary(t => t.Value, t => (string?)t.Attribute("fill"));
        Assert.Equal("#a80000", fills["a"]);
        Assert.Equal("#fc5454", fills["b"]);
        Assert.Equal("#ff0000", fills["c"]);
        Assert.Equal("#808080", fills["d"]);
        var backgrounds = root.Descendants(Svg + "rect").Select(r => (string?)r.Attribute("fill")).ToList();
        Assert.Contains("#0000a8", backgrounds);
        Assert.Contains("#123456", backgrounds);
    }

    [Fact]
    public void WideCharactersSpanTwoCells()
    {
        var surface = new CellSurface(6, 1);
        surface.Write(0, 0, new Rune('界'), 2);
        Put(surface, 2, 0, "x");
        var text = Assert.Single(Render(surface, new SvgOptions { CellWidth = 10 }).Root!.Elements(Svg + "text"));
        Assert.Equal("界x", text.Value);
        Assert.Equal("30", (string?)text.Attribute("textLength"));
    }

    [Fact]
    public void BoxCharactersBecomeShapesUnlessDisabled()
    {
        var surface = new CellSurface(4, 3);
        Put(surface, 0, 0, "╔═╗");
        Put(surface, 0, 1, "║░║");
        Put(surface, 0, 2, "╚═╝");

        var shapes = Render(surface).Root!;
        Assert.Empty(shapes.Elements(Svg + "text"));
        Assert.NotEmpty(shapes.Elements(Svg + "path"));

        var text = Render(surface, new SvgOptions { DrawBoxCharacters = false }).Root!;
        Assert.Equal(["╔═╗", "║░║", "╚═╝"], text.Elements(Svg + "text").Select(t => t.Value));
    }
}
