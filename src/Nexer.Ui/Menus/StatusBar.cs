// Ported from Jexer TStatusBar.java (MIT, © Autumn Lamonte).
using Nexer.Input;
using Nexer.Ui.Markup;

namespace Nexer.Ui.Menus;

/// <summary>
/// A shortcut shown on a <see cref="StatusBar"/>: pressing <see cref="Key"/> (or clicking it) raises
/// <see cref="Click"/> and posts <see cref="Command"/>.
/// </summary>
public class StatusKey
{
    /// <summary>The key that triggers this shortcut, e.g. <c>"F1"</c>.</summary>
    public KeyGesture Key { get; set; }

    /// <summary>The label shown after the key.</summary>
    public string Text { get; set; } = "";

    /// <summary>Command posted when triggered (optional).</summary>
    public Command? Command { get; set; }

    /// <summary>Raised when the shortcut is triggered.</summary>
    public event EventHandler? Click;

    internal bool Selected { get; set; }

    internal int X { get; set; }

    internal int DisplayWidth => TextMetrics.Width(Text) + TextMetrics.Width(Key.ToString()) + 3;

    internal void Raise(Application? app)
    {
        Click?.Invoke(this, EventArgs.Empty);
        if (Command is { } command) app?.PostCommand(command);
    }
}

/// <summary>
/// A window-specific status bar drawn on the bottom row of the screen while its window is on top
/// (Jexer <c>TStatusBar</c>). Assign to <see cref="Window.StatusBar"/>.
/// </summary>
public class StatusBar
{
    private UiMouseEvent? mouse;

    /// <summary>Creates a status bar.</summary>
    public StatusBar()
    {
        Keys = new StatusKeyCollection(this);
    }

    /// <summary>The shortcut keys, shown left to right.</summary>
    [Content]
    public StatusKeyCollection Keys { get; }

    /// <summary>Text shown after the keys.</summary>
    public string Text { get; set; } = "";

    /// <summary>The window owning this status bar.</summary>
    public Window? Owner { get; internal set; }

    internal void Relayout()
    {
        int x = 0;
        foreach (var key in Keys)
        {
            key.X = x;
            x += key.DisplayWidth;
        }
    }

    internal bool HandleKey(KeyEvent key)
    {
        foreach (var k in Keys)
        {
            if (k.Key.Matches(key))
            {
                k.Raise(Owner?.Application);
                return true;
            }
        }

        return false;
    }

    private bool MouseOnShortcut(StatusKey key)
    {
        var app = Owner?.Application;
        return mouse != null && app != null && mouse.AbsoluteY == app.DesktopBottom && mouse.AbsoluteX >= key.X &&
            mouse.AbsoluteX < key.X + key.DisplayWidth;
    }

    internal bool HandleMouseDown(UiMouseEvent mouse)
    {
        this.mouse = mouse;
        Relayout();
        foreach (var key in Keys)
        {
            if (MouseOnShortcut(key) && mouse.IsLeft)
            {
                key.Selected = true;
                return true;
            }
        }

        return false;
    }

    internal bool HandleMouseUp(UiMouseEvent mouse)
    {
        this.mouse = mouse;
        foreach (var key in Keys)
        {
            if (key.Selected && mouse.IsLeft)
            {
                key.Selected = false;
                key.Raise(Owner?.Application);
                return true;
            }
        }

        return false;
    }

    internal void HandleMouseMotion(UiMouseEvent mouse)
    {
        this.mouse = mouse;
        Relayout();
        foreach (var key in Keys)
        {
            if (!MouseOnShortcut(key)) key.Selected = false;
        }
    }

    internal void Draw(DrawContext context, ColorTheme theme)
    {
        Relayout();
        var barColor = theme.GetColor("tstatusbar.text");
        var keyColor = theme.GetColor("tstatusbar.button");
        var selectedColor = theme.GetColor("tstatusbar.selected");
        context.HLine(0, 0, context.Width, ' ', barColor);
        int col = 0;
        foreach (var key in Keys)
        {
            string keyText = key.Key.ToString();
            if (key.Selected)
            {
                context.PutChar(col++, 0, ' ', selectedColor);
                col += context.PutString(col, 0, keyText, selectedColor);
                context.PutChar(col++, 0, ' ', selectedColor);
                col += context.PutString(col, 0, key.Text, selectedColor);
                context.PutChar(col++, 0, ' ', selectedColor);
            }
            else
            {
                context.PutChar(col++, 0, ' ', barColor);
                context.PutString(col, 0, keyText, keyColor);
                col += TextMetrics.Width(keyText) + 1;
                context.PutString(col, 0, key.Text, barColor);
                col += TextMetrics.Width(key.Text);
                context.PutChar(col++, 0, ' ', barColor);
            }
        }

        if (Text.Length > 0)
        {
            if (Keys.Count > 0) context.PutChar(col++, 0, GraphicsChars.VerticalBar, barColor);
            context.PutChar(col++, 0, ' ', barColor);
            context.PutString(col, 0, Text, barColor);
        }
    }

    internal static void DrawEmpty(DrawContext context, ColorTheme theme) =>
        context.HLine(0, 0, context.Width, ' ', theme.GetColor("tstatusbar.text"));
}

/// <summary>Collection of <see cref="StatusKey"/> items.</summary>
public sealed class StatusKeyCollection : System.Collections.ObjectModel.Collection<StatusKey>
{
    private readonly StatusBar owner;

    internal StatusKeyCollection(StatusBar owner) => this.owner = owner;

    /// <inheritdoc/>
    protected override void InsertItem(int index, StatusKey item)
    {
        ArgumentNullException.ThrowIfNull(item);
        base.InsertItem(index, item);
        owner.Relayout();
    }

    /// <inheritdoc/>
    protected override void RemoveItem(int index)
    {
        base.RemoveItem(index);
        owner.Relayout();
    }
}
