// Ported from Jexer TCommand.java (MIT, © Autumn Lamonte).
namespace Nexer.Ui;

/// <summary>
/// A named command dispatched through the widget tree (Jexer's <c>TCommand</c>). Commands compare by name.
/// </summary>
/// <remarks>XAML text form: the command name, e.g. <c>Command="Exit"</c>; well-known names map to the static instances.</remarks>
public sealed record Command(string Name)
{
    /// <summary>Exit the application (asks for confirmation, as Jexer).</summary>
    public static Command Exit { get; } = new("Exit");

    /// <summary>Tile the windows.</summary>
    public static Command Tile { get; } = new("Tile");

    /// <summary>Cascade the windows.</summary>
    public static Command Cascade { get; } = new("Cascade");

    /// <summary>Close every window.</summary>
    public static Command CloseAll { get; } = new("CloseAll");

    /// <summary>Close the active window.</summary>
    public static Command WindowClose { get; } = new("WindowClose");

    /// <summary>Activate the next window.</summary>
    public static Command WindowNext { get; } = new("WindowNext");

    /// <summary>Activate the previous window.</summary>
    public static Command WindowPrevious { get; } = new("WindowPrevious");

    /// <summary>Enter keyboard move/resize mode for the active window.</summary>
    public static Command WindowMove { get; } = new("WindowMove");

    /// <summary>Toggle maximise of the active window.</summary>
    public static Command WindowZoom { get; } = new("WindowZoom");

    /// <summary>Open the menu bar.</summary>
    public static Command Menu { get; } = new("Menu");

    /// <summary>Help (no built-in handler).</summary>
    public static Command Help { get; } = new("Help");

    /// <summary>Parses a command name, returning the well-known instance when one exists.</summary>
    public static Command Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        text = text.Trim();
        foreach (var c in WellKnown)
        {
            if (string.Equals(c.Name, text, StringComparison.OrdinalIgnoreCase)) return c;
        }

        return new Command(text);
    }

    private static readonly Command[] WellKnown =
        [Exit, Tile, Cascade, CloseAll, WindowClose, WindowNext, WindowPrevious, WindowMove, WindowZoom, Menu, Help];

    /// <inheritdoc/>
    public override string ToString() => Name;
}
