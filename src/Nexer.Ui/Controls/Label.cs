// Ported from Jexer TLabel.java (MIT, © Autumn Lamonte).
using System.Text;

namespace Nexer.Ui.Controls;

/// <summary>
/// A one-line static text with an optional mnemonic (Jexer <c>TLabel</c>). Labels are disabled (never focused); pressing
/// Alt+mnemonic raises <see cref="Click"/> and executes <see cref="Command"/>. Use <c>&amp;&amp;</c> for a literal
/// ampersand.
/// </summary>
public class Label : Widget
{
    private MnemonicString mnemonic = new("");

    /// <summary>Creates an empty label.</summary>
    public Label()
    {
        Enabled = false;
        Height = 1;
        Width = 0;
    }

    /// <summary>The text, with an optional <c>&amp;</c> mnemonic marker; reads back without the marker. Sets the width.</summary>
    public string Text
    {
        get => mnemonic.RawLabel;
        set
        {
            mnemonic = new MnemonicString(value ?? "");
            Width = TextMetrics.Width(mnemonic.RawLabel);
        }
    }

    /// <summary>The parsed mnemonic.</summary>
    public MnemonicString Mnemonic => mnemonic;

    /// <summary>Theme key for the text colour (default <c>"tlabel"</c>).</summary>
    public string ColorKey { get; set; } = "tlabel";

    /// <summary>When true (default) the label keeps the background already drawn beneath it.</summary>
    public bool MatchWindowBackground { get; set; } = true;

    /// <summary>Command executed when the mnemonic is pressed.</summary>
    public Command? Command { get; set; }

    /// <summary>Raised when the mnemonic is pressed.</summary>
    public event EventHandler? Click;

    internal override int MnemonicPass => 1;

    internal override bool TryInvokeMnemonic(Widget parent, Rune c)
    {
        if (!Visible || !mnemonic.Matches(c)) return false;
        Dispatch();
        return true;
    }

    /// <summary>Raises <see cref="Click"/> and executes <see cref="Command"/>.</summary>
    public void Dispatch()
    {
        Click?.Invoke(this, EventArgs.Empty);
        if (Command is { } command) (Application ?? Application.Current)?.ExecuteCommand(command);
    }

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        var color = GetColor(ColorKey);
        var mnemonicColor = GetColor("tlabel.mnemonic");
        if (MatchWindowBackground)
        {
            context.PutForegroundString(0, 0, mnemonic.RawLabel, color);
            if (mnemonic.ScreenShortcutIndex >= 0)
            {
                context.PutForegroundChar(mnemonic.ScreenShortcutIndex, 0, mnemonic.Shortcut, mnemonicColor);
            }
        }
        else
        {
            context.PutString(0, 0, mnemonic.RawLabel, color);
            if (mnemonic.ScreenShortcutIndex >= 0) context.PutChar(mnemonic.ScreenShortcutIndex, 0, mnemonic.Shortcut, mnemonicColor);
        }
    }
}
