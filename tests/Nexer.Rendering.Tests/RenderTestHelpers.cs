using System.Text;
using Nexer.Core;
using Nexer.Core.Text;
using Nexer.Emulation;

namespace Nexer.Rendering.Tests;

internal static class RenderTestHelpers
{
    public static string Render(VtRenderer renderer, IReadOnlyCellSurface surface, RenderCursor cursor = default, Terminal? terminal = null)
    {
        using RenderFrame frame = renderer.Prepare(surface, cursor);
        byte[] bytes = frame.Bytes.ToArray();
        terminal?.Feed(bytes);
        frame.Commit();
        return Encoding.UTF8.GetString(bytes);
    }

    public static void WriteText(CellSurface surface, int column, int row, string text, CellStyle style = default)
    {
        foreach (string cluster in Clusters(text))
        {
            Rune[] runes = cluster.EnumerateRunes().ToArray();
            int width = UnicodeText.GetWidth(runes);
            surface.WriteCluster(column, row, runes, width, style);
            column += width;
        }
    }

    public static IEnumerable<string> Clusters(string text)
    {
        var segmenter = new GraphemeSegmenter();
        var current = new StringBuilder();
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (segmenter.Push(rune) && current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
            }

            current.Append(rune.ToString());
        }

        if (current.Length > 0) yield return current.ToString();
    }

    public static void AssertScreenMatches(IReadOnlyCellSurface expected, Terminal terminal, RenderCursor cursor, string context = "")
    {
        IReadOnlyCellSurface actual = terminal.Screen;
        for (int row = 0; row < expected.Rows; row++)
        {
            ReadOnlySpan<Cell> e = expected.GetRow(row);
            ReadOnlySpan<Cell> a = actual.GetRow(row);
            for (int column = 0; column < expected.Columns; column++)
            {
                string expectedText = expected.GetText(column, row);
                string actualText = actual.GetText(column, row);
                if (e[column].Role != a[column].Role || e[column].Style != a[column].Style || expectedText != actualText)
                {
                    Assert.Fail($"{context} cell ({column},{row}): expected '{expectedText}' {e[column].Role} {e[column].Style}, " +
                        $"actual '{actualText}' {a[column].Role} {a[column].Style}");
                }
            }
        }

        Assert.Equal(cursor.Visible, terminal.CursorVisible);
        if (cursor.Visible)
        {
            Assert.Equal((cursor.Column, cursor.Row), (terminal.CursorColumn, terminal.CursorRow));
        }
    }
}
