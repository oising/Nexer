using Nexer.Core;
using Nexer.Emulation;
using Nexer.Rendering;
using Nexer.Ui.Testing;

namespace Nexer.Demo.Tests;

/// <summary>
/// Full-stack fidelity: demo frames rendered to VT bytes by <see cref="VtRenderer"/> and replayed through the
/// <see cref="Terminal"/> emulator must reproduce the frames the headless host captured.
/// </summary>
public class RenderFidelityTests
{
    private static void RenderInto(VtRenderer renderer, Terminal terminal, IReadOnlyCellSurface surface)
    {
        using RenderFrame frame = renderer.Prepare(surface, RenderCursor.Hidden);
        terminal.Feed(frame.Bytes.ToArray());
        frame.Commit();
    }

    private static void AssertSameScreen(UiDriver driver, Terminal terminal, string step)
    {
        var expected = driver.Frame;
        Assert.Equal(driver.Text(), ScreenDump.Text(terminal.Screen));
        for (int y = 0; y < expected.Rows; y++)
        {
            var row = terminal.Screen.GetRow(y);
            for (int x = 0; x < expected.Columns; x++)
            {
                if (expected.GetText(x, y) != terminal.Screen.GetText(x, y) || expected.GetStyle(x, y) != row[x].Style)
                {
                    Assert.Fail($"{step}: cell ({x},{y}) expected '{expected.GetText(x, y)}' {expected.GetStyle(x, y)}, " +
                        $"emulated '{terminal.Screen.GetText(x, y)}' {row[x].Style}");
                }
            }
        }
    }

    [Fact]
    public void EmulatedScreenMatchesHeadlessFrames()
    {
        var app = new DemoApplication();
        var driver = new UiDriver(app, 80, 25);
        var renderer = new VtRenderer(80, 25, new RendererOptions { Colors = ColorMode.TrueColor });
        var terminal = new Terminal(80, 25);

        // Full first frame: menu bar, desktop, main window, status bar.
        RenderInto(renderer, terminal, app.Screen!);
        AssertSameScreen(driver, terminal, "main screen");
        Assert.Contains("Demo Window", ScreenDump.Text(terminal.Screen), StringComparison.Ordinal);

        // Incremental frames: an open menu, then a demo window with box-drawing and non-ASCII glyphs.
        driver.Press("Alt+D");
        RenderInto(renderer, terminal, app.Screen!);
        AssertSameScreen(driver, terminal, "demo menu");

        driver.Press("Escape");
        driver.Press("Alt+C");
        RenderInto(renderer, terminal, app.Screen!);
        AssertSameScreen(driver, terminal, "check box window");

        // Translucent windows blend RGB colours; true-colour output must reproduce them exactly.
        app.Translucence = true;
        driver.Pump();
        RenderInto(renderer, terminal, app.Screen!);
        AssertSameScreen(driver, terminal, "translucent");
    }
}
