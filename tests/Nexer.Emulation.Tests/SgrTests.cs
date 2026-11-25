using System.Text;
using Nexer.Core;

namespace Nexer.Emulation.Tests;

public sealed class SgrTests
{
    [Theory]
    [InlineData("38;2;10;20;30", true)]
    [InlineData("38:2:10:20:30", true)]
    [InlineData("38:2::10:20:30", true)]
    [InlineData("38:2:0:10:20:30", true)]
    [InlineData("48;2;10;20;30", false)]
    [InlineData("48:2::10:20:30", false)]
    public void RgbFormsSetTheRequestedColor(string parameters, bool foreground)
    {
        var terminal = new Terminal(4, 1);
        terminal.Feed(Encoding.UTF8.GetBytes($"\u001b[{parameters}mX"));
        CellStyle style = terminal.Screen.GetRow(0)[0].Style;
        Assert.Equal(TerminalColor.Rgb(10, 20, 30), foreground ? style.Foreground : style.Background);
    }

    [Theory]
    [InlineData("38;5;201", true, 201)]
    [InlineData("38:5:201", true, 201)]
    [InlineData("48:5:17", false, 17)]
    [InlineData("91", true, 9)]
    [InlineData("104", false, 12)]
    public void IndexedAndBrightColorsAreDistinctFromDefaults(string parameters, bool foreground, byte index)
    {
        var terminal = new Terminal(4, 1);
        terminal.Feed(Encoding.UTF8.GetBytes($"\u001b[{parameters}mX"));
        CellStyle style = terminal.Screen.GetRow(0)[0].Style;
        Assert.Equal(TerminalColor.Indexed(index), foreground ? style.Foreground : style.Background);
        terminal.Feed("\u001b[39;49mY"u8);
        Assert.Equal(default, terminal.Screen.GetRow(0)[1].Style);
    }

    [Fact]
    public void StyleFlagsCanBeSetAndIndividuallyCleared()
    {
        var terminal = new Terminal(4, 1);
        terminal.Feed("\u001b[1;2;3;4;5;7;8;9mX\u001b[22;23;24;25;27;28;29mY"u8);
        Assert.Equal((CellStyleFlags)255, terminal.Screen.GetRow(0)[0].Style.Flags);
        Assert.Equal(default, terminal.Screen.GetRow(0)[1].Style);
    }

    [Fact]
    public void ColonGroupsDoNotLeakComponentsIntoStyleCodes()
    {
        var terminal = new Terminal(4, 1);
        terminal.Feed("\u001b[38:2:1:2:3;4:1;1mX\u001b[4:0;22mY"u8);
        CellStyle first = terminal.Screen.GetRow(0)[0].Style;
        Assert.Equal(TerminalColor.Rgb(1, 2, 3), first.Foreground);
        Assert.Equal(CellStyleFlags.Bold | CellStyleFlags.Underline, first.Flags);
        Assert.Equal(CellStyleFlags.None, terminal.Screen.GetRow(0)[1].Style.Flags);
    }

    [Theory]
    [InlineData("38:2:1:2;1", true)]
    [InlineData("38:2:7:1:2:3;1", true)]
    [InlineData("38;2;999;2;3;1", false)]
    [InlineData("38;2;1;2;1:3", false)]
    [InlineData("38;5", false)]
    public void MalformedColorsFollowTheDeclaredGroupPolicy(string parameters, bool bold)
    {
        var terminal = new Terminal(4, 1);
        terminal.Feed(Encoding.UTF8.GetBytes($"\u001b[{parameters}mX"));
        CellStyle style = terminal.Screen.GetRow(0)[0].Style;
        Assert.Equal(TerminalColor.Default, style.Foreground);
        Assert.Equal(bold ? CellStyleFlags.Bold : CellStyleFlags.None, style.Flags);
    }

    [Fact]
    public void EmptySgrResetsAndUnknownCodesAreIgnored()
    {
        var terminal = new Terminal(4, 1);
        terminal.Feed("\u001b[31;1mX\u001b[999mY\u001b[mZ"u8);
        Assert.Equal(terminal.Screen.GetRow(0)[0].Style, terminal.Screen.GetRow(0)[1].Style);
        Assert.Equal(default, terminal.Screen.GetRow(0)[2].Style);
    }
}
