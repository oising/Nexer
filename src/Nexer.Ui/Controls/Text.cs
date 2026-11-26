// Ported from Jexer TText.java (MIT, © Autumn Lamonte).
using Nexer.Input;

namespace Nexer.Ui.Controls;

/// <summary>Paragraph justification for <see cref="Text"/> (Jexer <c>TText.Justification</c>).</summary>
public enum Justification
{
    /// <summary>No wrapping; lines are split only at newlines.</summary>
    None,

    /// <summary>Word-wrapped, left aligned.</summary>
    Left,

    /// <summary>Word-wrapped, centred.</summary>
    Center,

    /// <summary>Word-wrapped, right aligned.</summary>
    Right,

    /// <summary>Word-wrapped, fully justified.</summary>
    Full,
}

/// <summary>
/// Read-only, word-wrapped, scrollable text (Jexer <c>TText</c>). Paragraphs are separated by blank lines
/// (<c>"\n\n"</c>); <see cref="LineSpacing"/> blank lines follow each paragraph. Scroll with the arrows, PgUp/PgDn,
/// Home/End or the mouse wheel.
/// </summary>
public class Text : ScrollableWidget
{
    private string text = "";
    private Justification justification = Justification.Left;
    private int lineSpacing = 1;
    private readonly List<string> lines = [];

    /// <summary>Creates an empty text widget.</summary>
    public Text()
    {
        VerticalScroller = new VScroller();
        HorizontalScroller = new HScroller();
        Children.Add(VerticalScroller);
        Children.Add(HorizontalScroller);
    }

    /// <summary>The text. Paragraphs are separated by <c>"\n\n"</c>.</summary>
    public string Content
    {
        get => text;
        set
        {
            text = (value ?? "").Replace("\r\n", "\n");
            ReflowData();
            Invalidate();
            OnPropertyChanged();
        }
    }

    /// <summary>Theme key for the text colour (default <c>"ttext"</c>).</summary>
    public string ColorKey
    {
        get => colorKey;
        set => SetProperty(ref colorKey, value);
    }

    private string colorKey = "ttext";

    /// <summary>Paragraph justification (default <see cref="Justification.Left"/>).</summary>
    public Justification Justification
    {
        get => justification;
        set
        {
            justification = value;
            ReflowData();
            OnPropertyChanged();
        }
    }

    /// <summary>Blank lines after each paragraph (default 1).</summary>
    public int LineSpacing
    {
        get => lineSpacing;
        set
        {
            lineSpacing = value;
            ReflowData();
            OnPropertyChanged();
        }
    }

    /// <summary>The wrapped lines.</summary>
    public IReadOnlyList<string> Lines => lines;

    /// <inheritdoc/>
    protected internal override bool IsKeyboardLeaf => true;

    /// <summary>Appends a paragraph.</summary>
    public void AddLine(string line)
    {
        Content = TextMetrics.Width(text) == 0 ? line : text + "\n\n" + line;
    }

    /// <inheritdoc/>
    public override void ReflowData()
    {
        lines.Clear();
        int width = Math.Max(1, Width - 1);
        foreach (var p in text.Split("\n\n"))
        {
            lines.AddRange(justification switch
            {
                Justification.None => p.Split('\n'),
                Justification.Center => TextMetrics.WrapCenter(p, width),
                Justification.Right => TextMetrics.WrapRight(p, width),
                Justification.Full => TextMetrics.WrapFull(p, width),
                _ => TextMetrics.WrapLeft(p, width),
            });
            for (int i = 0; i < lineSpacing; i++) lines.Add("");
        }

        ComputeBounds();
    }

    private void ComputeBounds()
    {
        int maxLineWidth = 0;
        foreach (var line in lines) maxLineWidth = Math.Max(maxLineWidth, TextMetrics.Width(line));
        if (VerticalScroller is { } v)
        {
            v.Minimum = 0;
            v.Maximum = Math.Max(0, lines.Count - Height + 1);
            if (v.Value > v.Maximum) v.Value = v.Maximum;
        }

        if (HorizontalScroller is { } h)
        {
            h.Minimum = 0;
            h.Maximum = Math.Max(0, maxLineWidth - Width + 1);
            if (h.Value > h.Maximum) h.Value = h.Maximum;
        }
    }

    /// <inheritdoc/>
    protected internal override void OnMouseDown(UiMouseEvent mouse)
    {
        if (mouse.IsWheelUp)
        {
            VerticalScroller?.Decrement();
            return;
        }

        if (mouse.IsWheelDown)
        {
            VerticalScroller?.Increment();
            return;
        }

        base.OnMouseDown(mouse);
    }

    /// <inheritdoc/>
    protected internal override void OnKeypress(KeyEvent key)
    {
        if (HandleScrollKey(key)) return;
        base.OnKeypress(key);
    }

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        var color = GetColor(ColorKey);
        int hValue = HorizontalValue;
        int topY = 0;
        for (int i = VerticalValue; i < lines.Count && topY < Height - 1; i++)
        {
            string line = lines[i];
            line = hValue < TextMetrics.Width(line) ? SkipColumns(line, hValue) : "";
            if (Width > 3)
            {
                context.HLine(0, topY, Width - 1, ' ', color);
                context.PutString(0, topY, TextMetrics.Truncate(line, Width - 1), color);
            }

            topY++;
        }

        for (int i = topY; i < Height - 1; i++) context.HLine(0, i, Width - 1, ' ', color);
    }

    private static string SkipColumns(string line, int columns)
    {
        int col = 0, index = 0;
        foreach (var (cluster, w) in TextMetrics.Clusters(line))
        {
            if (col >= columns) break;
            col += w;
            foreach (var r in cluster) index += r.Utf16SequenceLength;
        }

        return line[index..];
    }
}
