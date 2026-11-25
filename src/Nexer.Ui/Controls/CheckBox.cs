// Ported from Jexer TCheckBox.java (MIT, © Autumn Lamonte).
using System.Text;
using Nexer.Core;
using Nexer.Input;

namespace Nexer.Ui.Controls;

/// <summary>
/// A check box drawn as <c>[√] label</c> (Jexer <c>TCheckBox</c>). Toggled by a left click on the brackets, Space,
/// Enter or Alt+mnemonic; Esc unchecks. Every user change raises <see cref="Changed"/>.
/// </summary>
public class CheckBox : Widget
{
    private MnemonicString mnemonic = new("");

    /// <summary>Creates a check box.</summary>
    public CheckBox()
    {
        Height = 1;
        Width = 4;
        CursorVisible = true;
        CursorX = 1;
    }

    /// <summary>The label, with an optional <c>&amp;</c> mnemonic marker. Sets the width (label + 4).</summary>
    public string Text
    {
        get => mnemonic.RawLabel;
        set
        {
            mnemonic = new MnemonicString(value ?? "");
            Width = TextMetrics.Width(mnemonic.RawLabel) + 4;
        }
    }

    /// <summary>The parsed mnemonic.</summary>
    public MnemonicString Mnemonic => mnemonic;

    /// <summary>Whether the box is checked. Setting it does not raise <see cref="Changed"/> (as Jexer).</summary>
    public bool Checked { get; set; }

    /// <summary>When true (default) the check box keeps the background already drawn beneath it.</summary>
    public bool MatchWindowBackground { get; set; } = true;

    /// <summary>Command executed after each user change.</summary>
    public Command? Command { get; set; }

    /// <summary>Raised after each user change (click, Space, Enter, Esc, mnemonic).</summary>
    public event EventHandler? Changed;

    internal override int MnemonicPass => 3;

    internal override bool TryInvokeMnemonic(Widget parent, Rune c)
    {
        if (!Enabled || !Visible || !mnemonic.Matches(c)) return false;
        parent.Activate(this);
        OnKeypress(new KeyEvent(Key.Character, new Rune(' ')));
        return true;
    }

    /// <inheritdoc/>
    protected internal override void OnMouseDown(UiMouseEvent mouse)
    {
        if (mouse.Y == 0 && mouse.X >= 0 && mouse.X <= 2 && mouse.IsLeft)
        {
            Checked = !Checked;
            Dispatch();
        }
    }

    /// <inheritdoc/>
    protected internal override void OnKeypress(KeyEvent key)
    {
        if (key.Modifiers == KeyModifiers.None)
        {
            if (key.Key == Key.Enter || (key.Key == Key.Character && key.Char.Value == ' '))
            {
                Checked = !Checked;
                Dispatch();
                return;
            }

            if (key.Key == Key.Escape)
            {
                Checked = false;
                Dispatch();
                return;
            }
        }

        base.OnKeypress(key);
    }

    /// <summary>Raises <see cref="Changed"/> and executes <see cref="Command"/>.</summary>
    protected void Dispatch()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        if (Command is { } command) (Application ?? Application.Current)?.ExecuteCommand(command);
    }

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        bool active = IsAbsoluteActive;
        var color = GetColor(active ? "tcheckbox.active" : "tcheckbox.inactive");
        var mnemonicColor = GetColor(active ? "tcheckbox.mnemonic.highlighted" : "tcheckbox.mnemonic");
        ToggleDrawing.Draw(context, MatchWindowBackground, '[', Checked ? GraphicsChars.Check : ' ', ']', mnemonic, color, mnemonicColor);
    }
}

/// <summary>Shared drawing for check boxes and radio buttons: <c>[x] label</c> with the mnemonic highlighted.</summary>
internal static class ToggleDrawing
{
    public static void Draw(DrawContext context, bool matchBackground, char open, char mark, char close, MnemonicString mnemonic,
        CellStyle color, CellStyle mnemonicColor)
    {
        if (matchBackground)
        {
            context.PutForegroundChar(0, 0, open, color);
            context.PutForegroundChar(1, 0, mark, color);
            context.PutForegroundChar(2, 0, close, color);
            context.PutForegroundString(4, 0, mnemonic.RawLabel, color);
            if (mnemonic.ScreenShortcutIndex >= 0) context.PutForegroundChar(4 + mnemonic.ScreenShortcutIndex, 0, mnemonic.Shortcut, mnemonicColor);
        }
        else
        {
            context.PutChar(0, 0, open, color);
            context.PutChar(1, 0, mark, color);
            context.PutChar(2, 0, close, color);
            context.PutString(4, 0, mnemonic.RawLabel, color);
            if (mnemonic.ScreenShortcutIndex >= 0) context.PutChar(4 + mnemonic.ScreenShortcutIndex, 0, mnemonic.Shortcut, mnemonicColor);
        }
    }
}
