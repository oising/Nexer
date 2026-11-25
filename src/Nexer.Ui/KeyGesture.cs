using System.Globalization;
using System.Text;
using Nexer.Input;

namespace Nexer.Ui;

/// <summary>A keyboard shortcut such as <c>Ctrl+Q</c>, <c>Alt+F</c>, <c>F10</c> or <c>Shift+Tab</c>.</summary>
/// <remarks>
/// Matching is case-insensitive for characters (as Jexer lower-cases accelerators), and
/// <c>Shift+Tab</c> is equivalent to <see cref="Key.BackTab"/>.
/// </remarks>
public readonly record struct KeyGesture
{
    /// <summary>Creates a gesture for a non-character key.</summary>
    public KeyGesture(Key key, KeyModifiers modifiers = KeyModifiers.None) : this(key, default, modifiers) { }

    /// <summary>Creates a gesture for a character key.</summary>
    public KeyGesture(Rune character, KeyModifiers modifiers = KeyModifiers.None) : this(Key.Character, character, modifiers) { }

    private KeyGesture(Key key, Rune character, KeyModifiers modifiers)
    {
        if (key == Key.Tab && (modifiers & KeyModifiers.Shift) != 0)
        {
            key = Key.BackTab;
        }

        if (key == Key.BackTab)
        {
            modifiers &= ~KeyModifiers.Shift;
        }

        if (key == Key.Character)
        {
            character = Rune.ToLowerInvariant(character);
        }
        else
        {
            character = default;
        }

        Key = key;
        Char = character;
        Modifiers = modifiers;
    }

    /// <summary>The key.</summary>
    public Key Key { get; }

    /// <summary>The (lower-cased) character for <see cref="Key.Character"/>.</summary>
    public Rune Char { get; }

    /// <summary>Modifiers.</summary>
    public KeyModifiers Modifiers { get; }

    /// <summary>Creates the normalised gesture of a key event.</summary>
    public static KeyGesture FromEvent(KeyEvent key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var modifiers = key.Modifiers;
        if (key.Key == Key.Character && Rune.IsLetter(key.Char) && Rune.IsUpper(key.Char))
        {
            // An upper-case letter already encodes Shift.
            modifiers &= ~KeyModifiers.Shift;
        }

        return new KeyGesture(key.Key, key.Char, modifiers);
    }

    /// <summary>True when <paramref name="key"/> is this gesture.</summary>
    public bool Matches(KeyEvent key) => FromEvent(key) == this;

    /// <summary>Parses text such as <c>"Ctrl+Q"</c>, <c>"Alt+F"</c>, <c>"F10"</c>, <c>"Shift+Tab"</c>, <c>"Ctrl+Plus"</c>.</summary>
    public static KeyGesture Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var parts = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            // A '+' directly after a separator (or at the start) is the key itself.
            if (text[i] == '+' && i > start)
            {
                parts.Add(text[start..i].Trim());
                start = i + 1;
            }
        }

        parts.Add(text[start..].Trim());
        var modifiers = KeyModifiers.None;
        for (int i = 0; i < parts.Count - 1; i++)
        {
            modifiers |= parts[i].ToLowerInvariant() switch
            {
                "ctrl" or "control" => KeyModifiers.Ctrl,
                "alt" or "meta" => KeyModifiers.Alt,
                "shift" => KeyModifiers.Shift,
                _ => throw new FormatException($"Unknown modifier '{parts[i]}' in key gesture '{text}'."),
            };
        }

        string name = parts[^1];
        if (name.Length == 0) throw new FormatException($"Missing key in key gesture '{text}'.");
        if (Rune.TryGetRuneAt(name, 0, out var rune) && rune.Utf16SequenceLength == name.Length)
        {
            return new KeyGesture(rune, modifiers);
        }

        switch (name.ToLowerInvariant())
        {
            case "space": return new KeyGesture(new Rune(' '), modifiers);
            case "plus": return new KeyGesture(new Rune('+'), modifiers);
            case "minus": return new KeyGesture(new Rune('-'), modifiers);
            case "enter" or "return": return new KeyGesture(Key.Enter, modifiers);
            case "esc" or "escape": return new KeyGesture(Key.Escape, modifiers);
            case "tab": return new KeyGesture(Key.Tab, modifiers);
            case "backtab": return new KeyGesture(Key.BackTab, modifiers);
            case "backspace" or "bksp": return new KeyGesture(Key.Backspace, modifiers);
            case "ins" or "insert": return new KeyGesture(Key.Insert, modifiers);
            case "del" or "delete": return new KeyGesture(Key.Delete, modifiers);
            case "home": return new KeyGesture(Key.Home, modifiers);
            case "end": return new KeyGesture(Key.End, modifiers);
            case "pgup" or "pageup": return new KeyGesture(Key.PageUp, modifiers);
            case "pgdn" or "pagedown": return new KeyGesture(Key.PageDown, modifiers);
            case "up": return new KeyGesture(Key.Up, modifiers);
            case "down": return new KeyGesture(Key.Down, modifiers);
            case "left": return new KeyGesture(Key.Left, modifiers);
            case "right": return new KeyGesture(Key.Right, modifiers);
        }

        if (name.Length is 2 or 3 && (name[0] is 'F' or 'f') &&
            int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int f) && f is >= 1 and <= 12)
        {
            return new KeyGesture(Key.F1 + (f - 1), modifiers);
        }

        throw new FormatException($"Unknown key '{name}' in key gesture '{text}'.");
    }

    /// <summary>Jexer-style display text, e.g. <c>Ctrl+Q</c>, <c>Shift+F6</c>.</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        if ((Modifiers & KeyModifiers.Ctrl) != 0) sb.Append("Ctrl+");
        if ((Modifiers & KeyModifiers.Alt) != 0) sb.Append("Alt+");
        if ((Modifiers & KeyModifiers.Shift) != 0) sb.Append("Shift+");
        sb.Append(Key switch
        {
            Key.Character when Char.Value == ' ' => "Space",
            Key.Character => Rune.ToUpperInvariant(Char).ToString(),
            Key.Escape => "Esc",
            Key.BackTab => "Shift+Tab",
            Key.PageUp => "PgUp",
            Key.PageDown => "PgDn",
            Key.Insert => "Ins",
            Key.Delete => "Del",
            _ => Key.ToString(),
        });
        return sb.ToString();
    }
}
