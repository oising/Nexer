using System.Text;
using Nexer.Core;
using Nexer.Core.Text;
using Nexer.Hosting;
using Nexer.Input;

// Backend sample: draws a box and echoes decoded input events. Ctrl+Q quits.
// --smoke renders a single frame and exits (useful to check startup/teardown).
bool smoke = args.Contains("--smoke");

ConsoleTerminalHost host;
try
{
    host = await ConsoleTerminalHost.StartAsync();
}
catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
{
    Console.Error.WriteLine($"Cannot start the terminal host: {ex.Message}");
    return 2;
}

var log = new List<string>();
await using (host)
{
    TerminalSize size = host.Size;
    var surface = new CellSurface(size.Columns, size.Rows);
    var mouse = (X: -1, Y: -1);
    Draw();

    if (smoke)
    {
        await Task.Delay(200);
        return 0;
    }

    await foreach (InputEvent e in host.Input.ReadAllAsync())
    {
        switch (e)
        {
            case KeyEvent { Key: Key.Character, Char.Value: 'q', Modifiers: KeyModifiers.Ctrl }:
                return 0;
            case ResizeEvent r:
                size = new TerminalSize(r.Columns, r.Rows);
                surface = new CellSurface(size.Columns, size.Rows);
                break;
            case MouseEvent m:
                mouse = (m.X, m.Y);
                break;
        }

        Append(Describe(e));
        Draw();
    }

    return 0;

    void Append(string line)
    {
        log.Add(line);
        if (log.Count > 200)
        {
            log.RemoveRange(0, log.Count - 200);
        }
    }

    void Draw()
    {
        surface.Clear();
        int w = surface.Columns, h = surface.Rows;
        var frame = new CellStyle(TerminalColor.Indexed(6));
        var title = new CellStyle(TerminalColor.Rgb(255, 200, 0), Flags: CellStyleFlags.Bold);
        if (w >= 2 && h >= 2)
        {
            for (int x = 1; x < w - 1; x++)
            {
                surface.Write(x, 0, new Rune('─'), 1, frame);
                surface.Write(x, h - 1, new Rune('─'), 1, frame);
            }

            for (int y = 1; y < h - 1; y++)
            {
                surface.Write(0, y, new Rune('│'), 1, frame);
                surface.Write(w - 1, y, new Rune('│'), 1, frame);
            }

            surface.Write(0, 0, new Rune('┌'), 1, frame);
            surface.Write(w - 1, 0, new Rune('┐'), 1, frame);
            surface.Write(0, h - 1, new Rune('└'), 1, frame);
            surface.Write(w - 1, h - 1, new Rune('┘'), 1, frame);
        }

        WriteText(2, 0, $" Nexer backend sample {w}x{h} — Ctrl+Q quits ", title, w - 2);
        int visible = Math.Max(0, h - 2);
        int first = Math.Max(0, log.Count - visible);
        for (int i = first; i < log.Count; i++)
        {
            WriteText(2, 1 + i - first, log[i], default, w - 2);
        }

        if (mouse.X >= 0 && mouse.X < w && mouse.Y >= 0 && mouse.Y < h)
        {
            surface.Write(mouse.X, mouse.Y, new Rune('●'), 1, new CellStyle(TerminalColor.Indexed(9)));
        }

        host.Present(surface, CursorState.Hidden);
    }

    void WriteText(int column, int row, string text, CellStyle style, int limit)
    {
        if (row < 0 || row >= surface.Rows)
        {
            return;
        }

        foreach (Rune rune in text.EnumerateRunes())
        {
            int width = UnicodeText.GetWidth([rune]);
            if (width == 0)
            {
                continue;
            }

            if (column + width > limit)
            {
                break;
            }

            surface.Write(column, row, rune, width, style);
            column += width;
        }
    }
}

static string Describe(InputEvent e) => e switch
{
    KeyEvent k => $"Key {k.Key}{(k.Key == Key.Character ? $" '{Printable(k.Char)}' (U+{k.Char.Value:X4})" : "")} {k.Modifiers}",
    MouseEvent m => $"Mouse {m.Kind} {m.Button} at {m.X},{m.Y} {m.Modifiers}",
    ResizeEvent r => $"Resize {r.Columns}x{r.Rows}",
    FocusEvent f => f.Focused ? "Focus gained" : "Focus lost",
    PasteEvent p => $"Paste {p.Text.Length} chars: \"{Escape(p.Text)}\"",
    _ => e.ToString() ?? string.Empty,
};

static string Printable(Rune r) => Rune.IsControl(r) ? "?" : r.ToString();

static string Escape(string text)
{
    var sb = new StringBuilder();
    foreach (Rune r in text.EnumerateRunes())
    {
        if (sb.Length > 60)
        {
            sb.Append('…');
            break;
        }

        sb.Append(r.Value switch
        {
            '\r' => "\\r",
            '\n' => "\\n",
            '\t' => "\\t",
            _ when Rune.IsControl(r) => $"\\x{r.Value:X2}",
            _ => r.ToString(),
        });
    }

    return sb.ToString();
}
