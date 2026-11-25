using Nexer.Core;
using Nexer.Ui.Controls;
using Nexer.Ui.Testing;

namespace Nexer.Ui.Tests;

public class TranslucenceTests
{
    private static UiDriver Start(Application app, params Window[] windows)
    {
        foreach (var w in windows) app.Windows.Add(w);
        return new UiDriver(app, 40, 14);
    }

    [Fact]
    public void WindowIsBlendedOverTheDesktopWithAShadow()
    {
        var app = new Application { ConfirmExit = false, Translucence = true };
        var window = new Window { Title = "T", X = 0, Y = 0, Width = 20, Height = 8 };
        var d = Start(app, window);
        Assert.Equal(242, window.Alpha);

        // Interior: blue (window) at 242/255 over white (desktop); the desktop hatch does not show through.
        Assert.Equal(" ", d.Cell(5, 4));
        var interior = d.Style(5, 4);
        Assert.Equal(TerminalColor.Rgb(8, 8, 168), interior.Background);

        // Border glyph: bold white blended onto the new background.
        Assert.Equal("═", d.Cell(5, 8));
        Assert.Equal(TerminalColor.Rgb(239, 239, 247), d.Style(5, 8).Foreground);

        // Shadow: the desktop (blue on white, glyph kept) darkened with black at 30% of the window alpha (72).
        foreach (var (x, y) in new[] { (20, 2), (21, 8), (2, 9), (21, 9) })
        {
            Assert.Equal("░", d.Cell(x, y));
            Assert.Equal(new CellStyle(TerminalColor.Rgb(0, 0, 120), TerminalColor.Rgb(120, 120, 120)), d.Style(x, y));
        }

        Assert.Equal(d.Theme("tdesktop.background"), d.Style(20, 1));
        Assert.Equal(d.Theme("tdesktop.background"), d.Style(1, 9));
    }

    [Fact]
    public void BlankCellsLetUnderlyingTextShowThrough()
    {
        var app = new Application { ConfirmExit = false, Translucence = true };
        var back = new Window { Title = "B", X = 0, Y = 0, Width = 20, Height = 8 };
        back.Children.Add(new Label { Text = "Hello", X = 2, Y = 2 });
        var front = new Window { Title = "F", X = 1, Y = 1, Width = 15, Height = 6, AbsolutePosition = true };
        var d = Start(app, back, front);
        Assert.Same(front, app.ActiveWindow);

        // Label at screen (3,4) is under the front window's blank interior.
        Assert.Equal("Hello", d.Row(4, 3, 5));
        Assert.Equal(TerminalColorKind.Rgb, d.Style(3, 4).Foreground.Kind);

        front.Alpha = 255;
        d.Pump();
        Assert.Equal("     ", d.Row(4, 3, 5));
        Assert.Equal(d.Theme("twindow.background"), d.Style(3, 4));
    }

    [Fact]
    public void OpaqueModeDrawsTheClassicShadow()
    {
        var app = new Application { ConfirmExit = false };
        var window = new Window { Title = "T", X = 0, Y = 0, Width = 20, Height = 8 };
        var d = Start(app, window);
        Assert.Equal(d.Theme("twindow.background"), d.Style(5, 4));
        Assert.Equal(DrawContext.ShadowStyle, d.Style(20, 2));
        Assert.Equal("░", d.Cell(20, 2));
    }
}
