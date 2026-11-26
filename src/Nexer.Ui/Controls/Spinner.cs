// Ported from Jexer TSpinner.java and TProgressBar.java (MIT, © Autumn Lamonte).
using Nexer.Input;

namespace Nexer.Ui.Controls;

/// <summary>
/// A two-cell up/down spinner (Jexer <c>TSpinner</c>), usually placed beside a <see cref="Field"/>. Raises
/// <see cref="SpinUp"/>/<see cref="SpinDown"/> on Up/Down keys or clicks on the arrows.
/// </summary>
public class Spinner : Widget
{
    /// <summary>Creates a spinner.</summary>
    public Spinner()
    {
        Width = 2;
        Height = 1;
    }

    /// <summary>Raised when the up arrow is clicked or Up is pressed.</summary>
    public event EventHandler? SpinUp;

    /// <summary>Raised when the down arrow is clicked or Down is pressed.</summary>
    public event EventHandler? SpinDown;

    /// <inheritdoc/>
    protected internal override void OnMouseDown(UiMouseEvent mouse)
    {
        if (mouse.Y != 0 || !mouse.IsLeft) return;
        if (mouse.X == Width - 2) SpinUp?.Invoke(this, EventArgs.Empty);
        else if (mouse.X == Width - 1) SpinDown?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    protected internal override void OnKeypress(KeyEvent key)
    {
        if (key.Modifiers == KeyModifiers.None)
        {
            if (key.Key == Key.Up)
            {
                SpinUp?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (key.Key == Key.Down)
            {
                SpinDown?.Invoke(this, EventArgs.Empty);
                return;
            }
        }

        base.OnKeypress(key);
    }

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        var color = GetColor(IsAbsoluteActive ? "tspinner.active" : "tspinner.inactive");
        context.PutChar(Width - 2, 0, GraphicsChars.UpArrow, color);
        context.PutChar(Width - 1, 0, GraphicsChars.DownArrow, color);
    }
}

/// <summary>
/// A horizontal progress bar <c>├■■■──┤</c> (Jexer <c>TProgressBar</c>). It is disabled (never focused); height is 1.
/// </summary>
public class ProgressBar : Widget
{
    private int value;

    /// <summary>Creates a progress bar (0..100).</summary>
    public ProgressBar()
    {
        Enabled = false;
        Height = 1;
    }

    /// <summary>Value at the left end (default 0).</summary>
    public int Minimum
    {
        get => minimum;
        set => SetProperty(ref minimum, value);
    }

    private int minimum;

    /// <summary>Value at the right end (default 100).</summary>
    public int Maximum
    {
        get => maximum;
        set => SetProperty(ref maximum, value);
    }

    private int maximum = 100;

    /// <summary>Current value.</summary>
    public int Value
    {
        get => value;
        set
        {
            if (SetProperty(ref this.value, value)) Invalidate();
        }
    }

    /// <summary>Left end glyph (default ├).</summary>
    public char LeftBorderChar
    {
        get => leftBorderChar;
        set => SetProperty(ref leftBorderChar, value);
    }

    private char leftBorderChar = GraphicsChars.CP437[0xC3];

    /// <summary>Glyph for the completed part (default ■).</summary>
    public char CompletedChar
    {
        get => completedChar;
        set => SetProperty(ref completedChar, value);
    }

    private char completedChar = GraphicsChars.Box;

    /// <summary>Glyph for the remaining part (default ─).</summary>
    public char RemainingChar
    {
        get => remainingChar;
        set => SetProperty(ref remainingChar, value);
    }

    private char remainingChar = GraphicsChars.SingleBar;

    /// <summary>Right end glyph (default ┤).</summary>
    public char RightBorderChar
    {
        get => rightBorderChar;
        set => SetProperty(ref rightBorderChar, value);
    }

    private char rightBorderChar = GraphicsChars.CP437[0xB4];

    /// <summary>When true the bar keeps the background already drawn beneath it (default false).</summary>
    public bool MatchWindowBackground
    {
        get => matchWindowBackground;
        set => SetProperty(ref matchWindowBackground, value);
    }

    private bool matchWindowBackground;

    private void Put(DrawContext context, int x, char ch, Nexer.Core.CellStyle style)
    {
        if (MatchWindowBackground) context.PutForegroundChar(x, 0, ch, style);
        else context.PutChar(x, 0, ch, style);
    }

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        int width = Width;
        if (width <= 2) return;
        var completeColor = GetColor("tprogressbar.complete");
        var incompleteColor = GetColor("tprogressbar.incomplete");
        float progress = Maximum == Minimum ? 1f : ((float)value - Minimum) / ((float)Maximum - Minimum);
        int progressInt = (int)(progress * 100);
        int progressUnit = 100 / (width - 2);
        Put(context, 0, LeftBorderChar, incompleteColor);
        for (int i = 1; i < width - 2; i++)
        {
            int iProgressInt = (int)((float)i / (width - 2) * 100);
            if (iProgressInt <= progressInt - progressUnit) Put(context, i, CompletedChar, completeColor);
            else Put(context, i, RemainingChar, incompleteColor);
        }

        if (value >= Maximum) Put(context, width - 2, CompletedChar, completeColor);
        else Put(context, width - 2, RemainingChar, incompleteColor);
        Put(context, width - 1, RightBorderChar, incompleteColor);
    }
}
