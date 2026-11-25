using System.Collections.Concurrent;
using System.Text;
using Nexer.Emulation;
using Nexer.Hosting.Platform;

namespace Nexer.Hosting.Tests;

/// <summary>A scripted platform whose output is interpreted by the Nexer.Emulation terminal.</summary>
internal sealed class FakePlatform : ITerminalPlatform
{
    private readonly BlockingCollection<byte[]> input = [];
    private readonly Lock gate = new();
    private readonly List<string> log = [];
    private Terminal terminal;

    public FakePlatform(int columns = 20, int rows = 5)
    {
        Size = new TerminalSize(columns, rows);
        terminal = new Terminal(columns, rows);
    }

    public volatile bool FailWrites;

    public TerminalSize Size { get; set; }

    public int FrameWrites { get; private set; }

    public IReadOnlyList<string> Log
    {
        get
        {
            lock (gate) return [.. log];
        }
    }

    public void Enter() => Add("enter");

    public void Restore() => Add("restore");

    public TerminalSize GetSize() => Size;

    public void Write(ReadOnlySpan<byte> bytes)
    {
        if (FailWrites) throw new IOException("broken pipe");
        lock (gate)
        {
            string text = Encoding.UTF8.GetString(bytes);
            log.Add("write:" + text);
            if (!text.Contains("\e[?1049", StringComparison.Ordinal)) FrameWrites++;
            terminal.Feed(bytes);
        }
    }

    public int Read(Span<byte> buffer, TimeSpan timeout)
    {
        if (!input.TryTake(out byte[]? bytes, timeout)) return 0;
        if (bytes.Length == 0) return -1;
        bytes.CopyTo(buffer);
        return bytes.Length;
    }

    public void Type(string text) => input.Add(Encoding.UTF8.GetBytes(text));

    public void EndInput() => input.Add([]);

    public void ResizeTerminal(int columns, int rows)
    {
        lock (gate) terminal = new Terminal(columns, rows);
        Size = new TerminalSize(columns, rows);
    }

    public string ScreenRow(int row)
    {
        lock (gate)
        {
            var text = new StringBuilder();
            for (int column = 0; column < terminal.Screen.Columns; column++) text.Append(terminal.Screen.GetText(column, row));
            return text.ToString().TrimEnd();
        }
    }

    public (bool Visible, int Column, int Row) Cursor
    {
        get
        {
            lock (gate) return (terminal.CursorVisible, terminal.CursorColumn, terminal.CursorRow);
        }
    }

    private void Add(string entry)
    {
        lock (gate) log.Add(entry);
    }
}
