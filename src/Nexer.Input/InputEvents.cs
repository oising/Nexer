using System.Text;

namespace Nexer.Input;

// CONTRACT: shared by Nexer.Input (producer), Nexer.Hosting and Nexer.Ui (consumers).
// Changes go through the coordinator; see docs\architecture.md.

public enum Key
{
    None,

    /// <summary>A printable character; see <see cref="KeyEvent.Char"/>.</summary>
    Character,
    Enter,
    Escape,
    Tab,
    BackTab,
    Backspace,
    Insert,
    Delete,
    Home,
    End,
    PageUp,
    PageDown,
    Up,
    Down,
    Left,
    Right,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
}

[Flags]
public enum KeyModifiers : byte
{
    None = 0,
    Shift = 1,
    Alt = 2,
    Ctrl = 4,
    Super = 8,
    Meta = 16,
}

public enum MouseButton : byte
{
    None,
    Left,
    Middle,
    Right,
}

public enum MouseEventKind : byte
{
    Down,
    Up,

    /// <summary>Motion with no button held.</summary>
    Move,

    /// <summary>Motion with <see cref="MouseEvent.Button"/> held.</summary>
    Drag,
    WheelUp,
    WheelDown,
    WheelLeft,
    WheelRight,
}

/// <summary>Base for all host input. Events are immutable; re-base coordinates with <c>with</c>.</summary>
/// <remarks><see cref="Timestamp"/> comes from <see cref="TimeProvider.GetTimestamp"/> of the producing host.</remarks>
public abstract record InputEvent
{
    public long Timestamp { get; init; }
}

/// <summary>
/// A key press. For <see cref="Key.Character"/>, <see cref="Char"/> holds the scalar (already shifted, e.g. 'A').
/// Ctrl+letter arrives as Key.Character with Char = lower-case letter and <see cref="KeyModifiers.Ctrl"/>.
/// </summary>
public sealed record KeyEvent(Key Key, Rune Char = default, KeyModifiers Modifiers = KeyModifiers.None) : InputEvent;

/// <summary>Mouse input in zero-based cell coordinates relative to the host screen.</summary>
public sealed record MouseEvent(
    MouseEventKind Kind,
    MouseButton Button,
    int X,
    int Y,
    KeyModifiers Modifiers = KeyModifiers.None) : InputEvent;

/// <summary>The host screen changed size.</summary>
public sealed record ResizeEvent(int Columns, int Rows) : InputEvent;

/// <summary>The host terminal window gained or lost focus (mode 1004).</summary>
public sealed record FocusEvent(bool Focused) : InputEvent;

/// <summary>Bracketed paste text (mode 2004). Never interpreted as keys.</summary>
/// <remarks>A large paste may arrive as several consecutive events; concatenate them in order.</remarks>
public sealed record PasteEvent(string Text) : InputEvent;
