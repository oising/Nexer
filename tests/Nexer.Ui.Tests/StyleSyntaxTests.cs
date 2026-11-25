using Nexer.Core;

namespace Nexer.Ui.Tests;

public class StyleSyntaxTests
{
    [Theory]
    [InlineData("yellow on blue", 3, 4, CellStyleFlags.None)]
    [InlineData("Bold White on Brown", 7, 3, CellStyleFlags.Bold)]
    [InlineData("bright blink  cyan on black", 6, 0, CellStyleFlags.Bold | CellStyleFlags.Blink)]
    [InlineData("reverse underline red on green", 1, 2, CellStyleFlags.Inverse | CellStyleFlags.Underline)]
    public void ParsesPaletteStyles(string text, byte fg, byte bg, CellStyleFlags flags) =>
        Assert.Equal(new CellStyle(TerminalColor.Indexed(fg), TerminalColor.Indexed(bg), flags), StyleSyntax.Parse(text));

    [Fact]
    public void ParsesRgbAndDefault()
    {
        Assert.Equal(new CellStyle(TerminalColor.Rgb(0x33, 0x66, 0xFF), default), StyleSyntax.Parse("#3366ff on default"));
        Assert.Equal(new CellStyle(TerminalColor.Rgb(1, 2, 3), TerminalColor.Rgb(0, 0, 0)), StyleSyntax.Parse("rgb: 010203 on #000000"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("yellow")]
    [InlineData("yellow blue")]
    [InlineData("purple on blue")]
    [InlineData("bold yellow on blue extra")]
    [InlineData("#12345 on blue")]
    [InlineData("rgb: red on blue")]
    public void RejectsInvalid(string text)
    {
        Assert.False(StyleSyntax.TryParse(text, out _));
        Assert.Throws<FormatException>(() => StyleSyntax.Parse(text));
    }
}
