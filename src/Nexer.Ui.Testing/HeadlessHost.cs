using System.Text;
using System.Threading.Channels;
using Nexer.Core;
using Nexer.Hosting;
using Nexer.Input;

namespace Nexer.Ui.Testing;

/// <summary>
/// An in-memory <see cref="ITerminalHost"/> for tests: input is scripted with the <c>Enqueue*</c> helpers and the
/// last presented frame is captured in <see cref="Frame"/>.
/// </summary>
public sealed class HeadlessHost : ITerminalHost
{
    private readonly Channel<InputEvent> input = Channel.CreateUnbounded<InputEvent>();

    /// <summary>Creates a host of the given size. Uses a <see cref="ManualTimeProvider"/> unless <paramref name="time"/> is given.</summary>
    public HeadlessHost(int columns = 80, int rows = 25, TimeProvider? time = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        Size = new TerminalSize(columns, rows);
        Time = time ?? new ManualTimeProvider();
    }

    /// <inheritdoc/>
    public TerminalSize Size { get; private set; }

    /// <inheritdoc/>
    public ChannelReader<InputEvent> Input => input.Reader;

    /// <inheritdoc/>
    public TimeProvider Time { get; }

    /// <summary>The clock when <see cref="Time"/> is a <see cref="ManualTimeProvider"/>.</summary>
    public ManualTimeProvider? Clock => Time as ManualTimeProvider;

    /// <summary>The last presented frame (a copy), or null before the first frame.</summary>
    public ScreenSnapshot? Frame { get; private set; }

    /// <summary>The cursor of the last presented frame.</summary>
    public CursorState Cursor { get; private set; }

    /// <summary>The number of frames presented.</summary>
    public int PresentCount { get; private set; }

    /// <summary>Raised after each frame is captured.</summary>
    public event EventHandler? Presented;

    /// <inheritdoc/>
    public void Present(IReadOnlyCellSurface surface, CursorState cursor)
    {
        Frame = new ScreenSnapshot(surface);
        Cursor = cursor;
        PresentCount++;
        Presented?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Queues an input event, stamping it with the current time if it has no timestamp.</summary>
    public void Enqueue(InputEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Timestamp == 0) e = e with { Timestamp = Time.GetTimestamp() };
        input.Writer.TryWrite(e);
    }

    /// <summary>Queues a key press.</summary>
    public void EnqueueKey(Key key, KeyModifiers modifiers = KeyModifiers.None) => Enqueue(new KeyEvent(key, default, modifiers));

    /// <summary>Queues a key gesture such as <c>"Ctrl+Q"</c>, <c>"Alt+F"</c> or <c>"F10"</c>.</summary>
    public void EnqueueGesture(string gesture)
    {
        var g = KeyGesture.Parse(gesture);
        Enqueue(g.Key == Key.Character ? new KeyEvent(Key.Character, g.Char, g.Modifiers) : new KeyEvent(g.Key, default, g.Modifiers));
    }

    /// <summary>Queues one character key event per scalar of <paramref name="text"/>.</summary>
    public void EnqueueText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var rune in text.EnumerateRunes()) Enqueue(new KeyEvent(Key.Character, rune));
    }

    /// <summary>Queues a mouse event.</summary>
    public void EnqueueMouse(MouseEventKind kind, int x, int y, MouseButton button = MouseButton.Left, KeyModifiers modifiers = KeyModifiers.None) =>
        Enqueue(new MouseEvent(kind, kind is MouseEventKind.Move or MouseEventKind.WheelUp or MouseEventKind.WheelDown ? MouseButton.None : button, x, y, modifiers));

    /// <summary>Queues a left click (down + up) at a screen cell.</summary>
    public void EnqueueClick(int x, int y, MouseButton button = MouseButton.Left)
    {
        EnqueueMouse(MouseEventKind.Down, x, y, button);
        EnqueueMouse(MouseEventKind.Up, x, y, button);
    }

    /// <summary>Queues a left-button drag from one cell to another (down, drag, up).</summary>
    public void EnqueueDrag(int fromX, int fromY, int toX, int toY)
    {
        EnqueueMouse(MouseEventKind.Down, fromX, fromY);
        EnqueueMouse(MouseEventKind.Drag, toX, toY);
        EnqueueMouse(MouseEventKind.Up, toX, toY);
    }

    /// <summary>Queues a paste.</summary>
    public void EnqueuePaste(string text) => Enqueue(new PasteEvent(text));

    /// <summary>Changes <see cref="Size"/> and queues the matching <see cref="ResizeEvent"/>.</summary>
    public void Resize(int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        Size = new TerminalSize(columns, rows);
        Enqueue(new ResizeEvent(columns, rows));
    }

    /// <summary>Ends the input stream (the application loop exits once it drains).</summary>
    public void CompleteInput() => input.Writer.TryComplete();

    /// <summary>The text of the last frame (see <see cref="ScreenDump.Text(ScreenSnapshot,int,int,int,int,bool)"/>).</summary>
    public string ScreenText(int x = 0, int y = 0, int width = -1, int height = -1) =>
        ScreenDump.Text(Frame ?? throw new InvalidOperationException("No frame has been presented."), x, y, width, height);

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        CompleteInput();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Drives an <see cref="Application"/> deterministically on the test thread: attaches it to a
/// <see cref="HeadlessHost"/> and pumps input, posted work and timers until idle after each scripted step.
/// </summary>
public sealed class UiDriver
{
    /// <summary>Attaches <paramref name="application"/> to a new headless host and pumps once.</summary>
    public UiDriver(Application application, int columns = 80, int rows = 25)
        : this(application, new HeadlessHost(columns, rows))
    {
    }

    /// <summary>Attaches <paramref name="application"/> to <paramref name="host"/> and pumps once.</summary>
    public UiDriver(Application application, HeadlessHost host)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(host);
        Application = application;
        Host = host;
        application.Attach(host);
        Pump();
    }

    /// <summary>The application.</summary>
    public Application Application { get; }

    /// <summary>The host.</summary>
    public HeadlessHost Host { get; }

    /// <summary>The last frame.</summary>
    public ScreenSnapshot Frame => Host.Frame ?? throw new InvalidOperationException("No frame has been presented.");

    /// <summary>Processes everything pending until nothing is left (bounded). Returns the number of items processed.</summary>
    public int Pump()
    {
        int total = 0;
        for (int i = 0; i < 1000; i++)
        {
            int n = Application.ProcessPending();
            total += n;
            if (n == 0 || !Application.IsRunning) break;
        }

        return total;
    }

    /// <summary>Advances the manual clock (firing timers) and pumps.</summary>
    public void Advance(TimeSpan delta)
    {
        (Host.Clock ?? throw new InvalidOperationException("The host does not use a ManualTimeProvider.")).Advance(delta);
        Pump();
    }

    /// <summary>Presses a key and pumps.</summary>
    public void Key(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        Host.EnqueueKey(key, modifiers);
        Pump();
    }

    /// <summary>Presses a gesture (<c>"Alt+F"</c>, <c>"Tab"</c>, <c>"a"</c>) and pumps.</summary>
    public void Press(string gesture)
    {
        Host.EnqueueGesture(gesture);
        Pump();
    }

    /// <summary>Types text and pumps.</summary>
    public void Type(string text)
    {
        Host.EnqueueText(text);
        Pump();
    }

    /// <summary>Clicks at a screen cell and pumps.</summary>
    public void Click(int x, int y, MouseButton button = MouseButton.Left)
    {
        Host.EnqueueClick(x, y, button);
        Pump();
    }

    /// <summary>Double-clicks at a screen cell and pumps.</summary>
    public void DoubleClick(int x, int y)
    {
        Host.EnqueueClick(x, y);
        Host.EnqueueClick(x, y);
        Pump();
    }

    /// <summary>Sends a single mouse event and pumps.</summary>
    public void Mouse(MouseEventKind kind, int x, int y, MouseButton button = MouseButton.Left)
    {
        Host.EnqueueMouse(kind, x, y, button);
        Pump();
    }

    /// <summary>Drags with the left button and pumps.</summary>
    public void Drag(int fromX, int fromY, int toX, int toY)
    {
        Host.EnqueueDrag(fromX, fromY, toX, toY);
        Pump();
    }

    /// <summary>Pastes text and pumps.</summary>
    public void Paste(string text)
    {
        Host.EnqueuePaste(text);
        Pump();
    }

    /// <summary>Resizes the screen and pumps.</summary>
    public void Resize(int columns, int rows)
    {
        Host.Resize(columns, rows);
        Pump();
    }

    /// <summary>The text of the last frame (trailing spaces trimmed per line).</summary>
    public string Text(int x = 0, int y = 0, int width = -1, int height = -1) => Host.ScreenText(x, y, width, height);

    /// <summary>The text of one row of the last frame (untrimmed).</summary>
    public string Row(int y, int x = 0, int width = -1) => Frame.GetRowText(y, x, width);

    /// <summary>The grapheme at a cell of the last frame.</summary>
    public string Cell(int x, int y) => Frame.GetText(x, y);

    /// <summary>The style at a cell of the last frame.</summary>
    public CellStyle Style(int x, int y) => Frame.GetStyle(x, y);

    /// <summary>The style for a theme key in the application's theme.</summary>
    public CellStyle Theme(string key) => Application.Theme.GetColor(key);

    /// <summary>Convenience: the first rune of a cell.</summary>
    public Rune Rune(int x, int y) => System.Text.Rune.GetRuneAt(Cell(x, y) is { Length: > 0 } s ? s : " ", 0);
}
