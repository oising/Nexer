// Ported from Jexer TField.java and TPasswordField.java (MIT, © Autumn Lamonte).
using System.Text;
using Nexer.Input;
using Nexer.Ui.Data;

namespace Nexer.Ui.Controls;

/// <summary>
/// A single-line text entry field (Jexer <c>TField</c>). <see cref="Fixed"/> fields cannot hold more text than fits;
/// otherwise the view scrolls. Raises <see cref="Changed"/> on every edit and <see cref="Submitted"/> on Enter.
/// Height is always 1.
/// </summary>
public class Field : Widget, ILostFocusSource
{
    private string text = "";
    private string notifiedText = "";

    /// <summary>Text (UTF-16) index of the cursor.</summary>
    protected int position;

    /// <summary>Column of the cursor within the whole text.</summary>
    protected int screenPosition;

    /// <summary>First visible column of the text (non-fixed fields scroll).</summary>
    protected int windowStart;
    /// <summary>Creates an empty field.</summary>
    public Field()
    {
        Height = 1;
        CursorVisible = true;
    }

    /// <summary>
    /// The text. Setting a different value moves the cursor home (and truncates a fixed field to its width). Raises
    /// <see cref="Widget.PropertyChanged"/> for every change, including typing. Binds two-way by default.
    /// </summary>
    [BindsTwoWayByDefault]
    public string Text
    {
        get => text;
        set
        {
            value ??= "";
            if (value == text) return;
            text = value;
            position = 0;
            screenPosition = 0;
            windowStart = 0;
            if (Fixed && text.Length > Width && Width > 0) text = TextMetrics.Truncate(text, Width);
            UpdateCursor();
            Invalidate();
            NotifyTextChanged();
        }
    }

    /// <summary>When true the text cannot be longer than the field width.</summary>
    public bool Fixed
    {
        get => fixedValue;
        set => SetProperty(ref fixedValue, value);
    }

    private bool fixedValue;

    /// <summary>Insert (true, default) or overwrite mode; toggled by Ins.</summary>
    public bool InsertMode
    {
        get => insertMode;
        set => SetProperty(ref insertMode, value);
    }

    private bool insertMode = true;

    /// <summary>Glyph shown in empty cells (default ░).</summary>
    public char BackgroundChar
    {
        get => backgroundChar;
        set => SetProperty(ref backgroundChar, value);
    }

    private char backgroundChar = GraphicsChars.Hatch;

    /// <summary>Theme key used when focused (default <c>"tfield.active"</c>).</summary>
    public string ActiveColorKey
    {
        get => activeColorKey;
        set => SetProperty(ref activeColorKey, value);
    }

    private string activeColorKey = "tfield.active";

    /// <summary>Theme key used when not focused (default <c>"tfield.inactive"</c>).</summary>
    public string InactiveColorKey
    {
        get => inactiveColorKey;
        set => SetProperty(ref inactiveColorKey, value);
    }

    private string inactiveColorKey = "tfield.inactive";

    /// <summary>Cursor position as a UTF-16 index into <see cref="Text"/>.</summary>
    public int Position => position;

    /// <summary>Raised after every edit (typing, Backspace, Del, paste).</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when Enter is pressed.</summary>
    public event EventHandler? Submitted;

    /// <summary>
    /// Raised when the field leaves the focus path (it, or an ancestor, is deactivated); bindings with
    /// <c>UpdateSourceTrigger=LostFocus</c> push the text then.
    /// </summary>
    public event EventHandler? LostFocus;

    /// <inheritdoc/>
    private protected override void OnLostFocus() => LostFocus?.Invoke(this, EventArgs.Empty);

    private static int CellWidth(Rune r) => TextMetrics.RuneWidth(r);

    private Rune RuneAt(int index) => Rune.GetRuneAt(text, index);

    private Rune RuneBefore(int index)
    {
        Rune.DecodeLastFromUtf16(text.AsSpan(0, index), out var r, out _);
        return r;
    }

    /// <inheritdoc/>
    protected internal override void OnMouseDown(UiMouseEvent mouse)
    {
        if (mouse.Y == 0 && mouse.X >= 0 && mouse.X <= base.Width - 1 && mouse.IsLeft)
        {
            int deltaX = mouse.X - CursorX;
            screenPosition += deltaX;
            int textWidth = TextMetrics.Width(text);
            if (screenPosition > textWidth) screenPosition = textWidth;
            if (screenPosition < 0) screenPosition = 0;
            position = ScreenToTextPosition(screenPosition);
            UpdateCursor();
        }
    }

    /// <inheritdoc/>
    protected internal override void OnKeypress(KeyEvent key)
    {
        bool plain = key.Modifiers == KeyModifiers.None;
        if (plain && HandleEditingKey(key.Key))
        {
            UpdateCursor();
            return;
        }

        if (key.Key == Key.Character && (key.Modifiers & (KeyModifiers.Alt | KeyModifiers.Ctrl)) == 0 && !Rune.IsControl(key.Char))
        {
            TypeChar(key.Char);
            UpdateCursor();
            Dispatch(false);
            return;
        }

        base.OnKeypress(key);
    }

    private bool HandleEditingKey(Key key)
    {
        int width = base.Width;
        switch (key)
        {
            case Key.Left:
                if (position > 0)
                {
                    var r = RuneBefore(position);
                    screenPosition -= CellWidth(r);
                    position -= r.Utf16SequenceLength;
                }

                NormalizeWindowStart();
                return true;
            case Key.Right:
                if (position < text.Length)
                {
                    int lastPosition = position;
                    var r = RuneAt(position);
                    screenPosition += CellWidth(r);
                    position += r.Utf16SequenceLength;
                    if (Fixed)
                    {
                        if (screenPosition == width)
                        {
                            screenPosition--;
                            position -= RuneAt(lastPosition).Utf16SequenceLength;
                        }
                    }
                    else
                    {
                        int lastWidth = CellWidth(RuneBefore(text.Length));
                        while (screenPosition - windowStart + lastWidth > width && ScreenToTextPosition(windowStart) < text.Length)
                        {
                            windowStart += CellWidth(RuneAt(ScreenToTextPosition(windowStart)));
                        }
                    }
                }

                return true;
            case Key.Enter:
                Dispatch(true);
                return true;
            case Key.Insert:
                InsertMode = !InsertMode;
                return true;
            case Key.Home:
                Home();
                return true;
            case Key.End:
                End();
                return true;
            case Key.Delete:
                if (text.Length > 0 && position < text.Length)
                {
                    text = text.Remove(position, RuneAt(position).Utf16SequenceLength);
                    screenPosition = TextMetrics.Width(text[..position]);
                }

                Dispatch(false);
                return true;
            case Key.Backspace:
                if (position > 0)
                {
                    var r = RuneBefore(position);
                    position -= r.Utf16SequenceLength;
                    text = text.Remove(position, r.Utf16SequenceLength);
                    screenPosition = TextMetrics.Width(text[..position]);
                }

                Dispatch(false);
                NormalizeWindowStart();
                return true;
            default:
                return false;
        }
    }

    private void TypeChar(Rune ch)
    {
        int width = base.Width;
        int textWidth = TextMetrics.Width(text);
        if (position == text.Length && textWidth < width)
        {
            AppendChar(ch);
        }
        else if (position < text.Length && textWidth < width)
        {
            if (!InsertMode) OverwriteChar(ch, true);
            else InsertChar(ch);
        }
        else if (position < text.Length && textWidth >= width)
        {
            if (Fixed && InsertMode)
            {
                // Full fixed field in insert mode: ignore.
            }
            else if (Fixed && !InsertMode)
            {
                OverwriteChar(ch, screenPosition < width - 1);
            }
            else if (!Fixed && !InsertMode)
            {
                OverwriteChar(ch, true);
            }
            else
            {
                InsertChar(ch);
            }
        }
        else if (!Fixed)
        {
            AppendChar(ch);
        }
    }

    private void OverwriteChar(Rune ch, bool advance)
    {
        text = text[..position] + ch.ToString() + text[(position + RuneAt(position).Utf16SequenceLength)..];
        if (advance)
        {
            screenPosition += CellWidth(ch);
            position += ch.Utf16SequenceLength;
        }
    }

    /// <summary>Appends a character at the end (Jexer <c>appendChar</c>).</summary>
    protected void AppendChar(Rune ch)
    {
        text += ch.ToString();
        position += ch.Utf16SequenceLength;
        screenPosition += CellWidth(ch);
        if (Fixed)
        {
            if (screenPosition >= base.Width)
            {
                position -= ch.Utf16SequenceLength;
                screenPosition -= CellWidth(ch);
            }
        }
        else if (screenPosition - windowStart >= base.Width)
        {
            windowStart++;
        }
    }

    /// <summary>Inserts a character at the cursor (Jexer <c>insertChar</c>).</summary>
    protected void InsertChar(Rune ch)
    {
        text = text[..position] + ch.ToString() + text[position..];
        position += ch.Utf16SequenceLength;
        screenPosition += CellWidth(ch);
        if (screenPosition - windowStart >= base.Width && !Fixed) windowStart++;
    }

    /// <summary>Inserts pasted text as if typed (control characters are dropped).</summary>
    protected internal override void OnPaste(string text)
    {
        bool any = false;
        foreach (var r in text.EnumerateRunes())
        {
            if (Rune.IsControl(r)) continue;
            TypeChar(r);
            any = true;
        }

        UpdateCursor();
        if (any) Dispatch(false);
    }

    /// <summary>Moves the cursor to the start.</summary>
    public void Home()
    {
        position = 0;
        screenPosition = 0;
        windowStart = 0;
        UpdateCursor();
    }

    /// <summary>Moves the cursor to the end.</summary>
    public void End()
    {
        position = text.Length;
        screenPosition = TextMetrics.Width(text);
        if (Fixed)
        {
            if (screenPosition >= base.Width && position > 0)
            {
                position -= RuneBefore(position).Utf16SequenceLength;
                screenPosition = TextMetrics.Width(text) - 1;
            }
        }
        else
        {
            windowStart = Math.Max(0, TextMetrics.Width(text) - base.Width + 1);
        }

        UpdateCursor();
    }

    /// <summary>Raises <see cref="Submitted"/> (enter) or <see cref="Changed"/>.</summary>
    protected virtual void Dispatch(bool enter)
    {
        if (!enter) NotifyTextChanged();
        if (enter) Submitted?.Invoke(this, EventArgs.Empty);
        else Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raises <see cref="Widget.PropertyChanged"/> for <see cref="Text"/> if it differs from the last notified value.</summary>
    private void NotifyTextChanged()
    {
        if (notifiedText == text) return;
        notifiedText = text;
        OnPropertyChanged(nameof(Text));
    }

    /// <summary>Converts a column within the text to a UTF-16 index.</summary>
    protected int ScreenToTextPosition(int column)
    {
        if (column <= 0) return 0;
        int n = 0, i = 0;
        while (i < text.Length)
        {
            var r = RuneAt(i);
            n += CellWidth(r);
            i += r.Utf16SequenceLength;
            if (n >= column) return i;
        }

        return text.Length;
    }

    /// <summary>Places the cursor (Jexer <c>updateCursor</c>).</summary>
    protected void UpdateCursor()
    {
        int width = base.Width;
        if (screenPosition > width && Fixed) CursorX = width;
        else if (screenPosition - windowStart >= width && !Fixed) CursorX = width - 1;
        else CursorX = screenPosition - windowStart;
    }

    /// <summary>Scrolls so the cursor is at the right edge (Jexer <c>normalizeWindowStart</c>).</summary>
    protected void NormalizeWindowStart()
    {
        if (Fixed)
        {
            windowStart = 0;
            UpdateCursor();
            return;
        }

        windowStart = Math.Max(0, screenPosition - (base.Width - 1));
        UpdateCursor();
    }

    /// <summary>The visible part of the text.</summary>
    protected string VisibleText
    {
        get
        {
            int end = Math.Min(windowStart + base.Width, TextMetrics.Width(text));
            int from = ScreenToTextPosition(windowStart), to = ScreenToTextPosition(end);
            return to > from ? text[from..to] : "";
        }
    }

    /// <summary>The colour for the current focus state (<c>tfield.error</c> while <see cref="Validation.GetHasErrors"/>).</summary>
    protected Nexer.Core.CellStyle FieldColor =>
        (Validation.GetHasErrors(this) || (Parent is ComboBox combo && Validation.GetHasErrors(combo))) ? GetColor("tfield.error") : GetColor(IsAbsoluteActive ? ActiveColorKey : InactiveColorKey);

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        var color = FieldColor;
        context.HLine(0, 0, base.Width, BackgroundChar, color);
        context.PutString(0, 0, VisibleText, color);
        UpdateCursor();
    }
}

/// <summary>
/// A <see cref="Field"/> that shows asterisks instead of its text when not focused (Jexer <c>TPasswordField</c>).
/// </summary>
public class PasswordField : Field
{
    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        bool showStars = !IsAbsoluteActive;
        var color = FieldColor;
        context.HLine(0, 0, Width, BackgroundChar, color);
        if (showStars) context.HLine(0, 0, Width - 2, '*', color);
        else context.PutString(0, 0, VisibleText, color);
        UpdateCursor();
    }
}
