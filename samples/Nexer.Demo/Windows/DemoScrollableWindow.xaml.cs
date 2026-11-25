// Ported from Jexer TScrollableWindow.java usage in the Jexer demo (MIT, © Autumn Lamonte).
using System.Globalization;
using Nexer.Input;
using Nexer.Ui;
using Nexer.Ui.Controls;

namespace Nexer.Demo;

/// <summary>
/// A <see cref="ScrollableWindow"/> over a virtual canvas larger than the window; the border scroll bars, arrow keys,
/// PgUp/PgDn and Home/End move the view.
/// </summary>
public partial class DemoScrollableWindow : ScrollableWindow
{
    /// <summary>Width of the virtual canvas.</summary>
    public const int CanvasWidth = 120;

    /// <summary>Height of the virtual canvas.</summary>
    public const int CanvasHeight = 60;

    /// <summary>Creates the window.</summary>
    public DemoScrollableWindow()
    {
        InitializeComponent();
        ReflowData();
    }

    /// <inheritdoc/>
    public override void ReflowData()
    {
        if (VerticalScroller is { } v)
        {
            v.Maximum = Math.Max(0, CanvasHeight - (Height - 2));
            v.BigChange = Math.Max(1, Height - 2);
        }

        if (HorizontalScroller is { } h)
        {
            h.Maximum = Math.Max(0, CanvasWidth - (Width - 2));
            h.BigChange = Math.Max(1, Width - 2);
        }
    }

    /// <summary>The text of canvas row <paramref name="row"/>.</summary>
    public static string CanvasLine(int row)
    {
        var line = new System.Text.StringBuilder(CanvasWidth);
        for (int col = 0; line.Length < CanvasWidth; col += 10)
        {
            line.Append(string.Create(CultureInfo.InvariantCulture, $"[{row,2},{col,3}] "));
        }

        return line.ToString(0, CanvasWidth);
    }

    /// <inheritdoc/>
    protected override void Draw(DrawContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Draw(context);
        var style = BackgroundColor;
        int rows = Height - 2, cols = Width - 2;
        for (int y = 0; y < rows; y++)
        {
            int row = y + VerticalValue;
            if (row >= CanvasHeight) break;
            var line = CanvasLine(row);
            int start = Math.Min(HorizontalValue, line.Length);
            var visible = line.Substring(start, Math.Min(cols, line.Length - start));
            context.PutString(1, 1 + y, visible, style);
        }
    }

    /// <inheritdoc/>
    protected override void OnKeypress(KeyEvent key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Modifiers == KeyModifiers.None)
        {
            switch (key.Key)
            {
                case Key.Up: VerticalScroller?.Decrement(); return;
                case Key.Down: VerticalScroller?.Increment(); return;
                case Key.Left: HorizontalScroller?.Decrement(); return;
                case Key.Right: HorizontalScroller?.Increment(); return;
                case Key.PageUp: VerticalScroller?.BigDecrement(); return;
                case Key.PageDown: VerticalScroller?.BigIncrement(); return;
                case Key.Home: ToHome(); return;
                case Key.End: ToEnd(); return;
            }
        }

        base.OnKeypress(key);
    }
}
