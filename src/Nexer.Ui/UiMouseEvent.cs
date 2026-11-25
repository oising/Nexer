using Nexer.Input;

namespace Nexer.Ui;

/// <summary>What happened to the mouse (Jexer <c>TMouseEvent.Type</c>).</summary>
public enum MouseAction
{
    /// <summary>A button (or the wheel) was pressed.</summary>
    Down,

    /// <summary>A button was released.</summary>
    Up,

    /// <summary>The mouse moved (with or without a button held).</summary>
    Motion,

    /// <summary>A second press at the same cell within the double-click time.</summary>
    DoubleClick,
}

/// <summary>
/// Immutable mouse event delivered to widgets. <see cref="X"/>/<see cref="Y"/> are local to the receiving widget;
/// <see cref="AbsoluteX"/>/<see cref="AbsoluteY"/> are screen coordinates. Re-base with <see cref="At"/>.
/// </summary>
public sealed record UiMouseEvent(MouseAction Action, MouseButton Button, int X, int Y, int AbsoluteX, int AbsoluteY,
    KeyModifiers Modifiers = KeyModifiers.None)
{
    /// <summary>True for a wheel-up press (delivered as <see cref="MouseAction.Down"/>, as Jexer).</summary>
    public bool IsWheelUp { get; init; }

    /// <summary>True for a wheel-down press (delivered as <see cref="MouseAction.Down"/>, as Jexer).</summary>
    public bool IsWheelDown { get; init; }

    /// <summary>True when the left button is involved (Jexer <c>isMouse1</c>).</summary>
    public bool IsLeft => Button == MouseButton.Left;

    /// <summary>True when the right button is involved (Jexer <c>isMouse3</c>).</summary>
    public bool IsRight => Button == MouseButton.Right;

    /// <summary>True when the middle button is involved (Jexer <c>isMouse2</c>).</summary>
    public bool IsMiddle => Button == MouseButton.Middle;

    /// <summary>Returns a copy with new local coordinates.</summary>
    public UiMouseEvent At(int x, int y) => this with { X = x, Y = y };

    /// <summary>Returns a copy re-based into <paramref name="widget"/>'s local coordinates.</summary>
    public UiMouseEvent RelativeTo(Widget widget)
    {
        ArgumentNullException.ThrowIfNull(widget);
        return this with { X = AbsoluteX - widget.AbsoluteX, Y = AbsoluteY - widget.AbsoluteY };
    }
}
