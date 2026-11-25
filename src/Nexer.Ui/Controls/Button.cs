// Ported from Jexer TButton.java (MIT, © Autumn Lamonte).
using System.Text;
using Nexer.Core;
using Nexer.Input;

namespace Nexer.Ui.Controls;

/// <summary>
/// A push button with a drop shadow (Jexer <c>TButton</c>, square style). Activated by mouse release, Enter, Space or
/// Alt+mnemonic; raises <see cref="Click"/> and executes <see cref="Command"/>. The width is the label width + 3 and
/// the height is 2 (label row + shadow row).
/// </summary>
public class Button : Widget
{
    private MnemonicString mnemonic = new("");
    private UiMouseEvent? mouse;

    /// <summary>Creates a button.</summary>
    public Button()
    {
        Height = 2;
        Width = 3;
    }

    /// <summary>The label, with an optional <c>&amp;</c> mnemonic marker; reads back without the marker. Sets the width.</summary>
    public string Text
    {
        get => mnemonic.RawLabel;
        set
        {
            mnemonic = new MnemonicString(value ?? "");
            Width = TextMetrics.Width(mnemonic.RawLabel) + 3;
        }
    }

    /// <summary>The parsed mnemonic.</summary>
    public MnemonicString Mnemonic => mnemonic;

    /// <summary>Whether the shadow is drawn (Jexer <c>noButtonShadow</c> inverted).</summary>
    public bool ShowShadow { get; set; } = true;

    /// <summary>Command executed when the button is pressed.</summary>
    public Command? Command { get; set; }

    /// <summary>True while the left button is held down on the button.</summary>
    public bool IsPressed { get; private set; }

    /// <summary>Raised when the button is pressed.</summary>
    public event EventHandler? Click;

    internal override int MnemonicPass => 0;

    internal override bool TryInvokeMnemonic(Widget parent, Rune c)
    {
        if (!Enabled || !Visible || !mnemonic.Matches(c)) return false;
        OnKeypress(new KeyEvent(Key.Enter));
        return true;
    }

    private bool MouseOnButton()
    {
        int rightEdge = Width - 1;
        if (IsPressed) rightEdge++;
        return mouse is { } m && m.Y == 0 && m.X >= 0 && m.X < rightEdge;
    }

    /// <inheritdoc/>
    protected internal override void OnMouseDown(UiMouseEvent mouse)
    {
        this.mouse = mouse;
        if (MouseOnButton() && mouse.IsLeft) IsPressed = true;
    }

    /// <inheritdoc/>
    protected internal override void OnMouseUp(UiMouseEvent mouse)
    {
        this.mouse = mouse;
        if (IsPressed && mouse.IsLeft) Dispatch();
    }

    /// <inheritdoc/>
    protected internal override void OnMouseMotion(UiMouseEvent mouse)
    {
        this.mouse = mouse;
        if (!MouseOnButton()) IsPressed = false;
    }

    /// <inheritdoc/>
    protected internal override void OnKeypress(KeyEvent key)
    {
        if (key.Modifiers == KeyModifiers.None && (key.Key == Key.Enter || (key.Key == Key.Character && key.Char.Value == ' ')))
        {
            Dispatch();
            return;
        }

        base.OnKeypress(key);
    }

    /// <summary>Releases the button, raises <see cref="Click"/> and executes <see cref="Command"/>.</summary>
    public void Dispatch()
    {
        IsPressed = false;
        Click?.Invoke(this, EventArgs.Empty);
        if (Command is { } command) (Application ?? Application.Current)?.ExecuteCommand(command);
    }

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        CellStyle buttonColor, mnemonicColor;
        if (!Enabled)
        {
            buttonColor = GetColor("tbutton.disabled");
            mnemonicColor = buttonColor;
        }
        else if (IsAbsoluteActive)
        {
            buttonColor = GetColor("tbutton.active");
            mnemonicColor = GetColor("tbutton.mnemonic.highlighted");
        }
        else
        {
            buttonColor = GetColor("tbutton.inactive");
            mnemonicColor = GetColor("tbutton.mnemonic");
        }

        int w = Width;
        if (IsPressed)
        {
            context.PutChar(1, 0, ' ', buttonColor);
            context.PutString(2, 0, mnemonic.RawLabel, buttonColor);
            context.PutChar(w - 1, 0, ' ', buttonColor);
        }
        else
        {
            context.PutChar(0, 0, ' ', buttonColor);
            context.PutString(1, 0, mnemonic.RawLabel, buttonColor);
            context.PutChar(w - 2, 0, ' ', buttonColor);
            if (ShowShadow)
            {
                // Shadow glyphs keep the background beneath them with a black foreground.
                PutShadow(context, 1, 1, GraphicsChars.CP437[0xDF]);
                for (int i = 0; i < w - 3; i++) PutShadow(context, i + 2, 1, GraphicsChars.CP437[0xDF]);
                PutShadow(context, w - 1, 0, GraphicsChars.CP437[0xDC]);
                PutShadow(context, w - 1, 1, GraphicsChars.CP437[0xDF]);
            }
        }

        if (mnemonic.ScreenShortcutIndex >= 0)
        {
            context.PutChar((IsPressed ? 2 : 1) + mnemonic.ScreenShortcutIndex, 0, mnemonic.Shortcut, mnemonicColor);
        }
    }

    private static void PutShadow(DrawContext context, int x, int y, char glyph) =>
        context.PutChar(x, y, glyph, context.MatchBackground(x, y, new CellStyle(TerminalColor.Indexed(0), TerminalColor.Default, CellStyleFlags.None)));
}
