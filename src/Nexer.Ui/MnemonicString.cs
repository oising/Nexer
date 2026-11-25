// Ported from Jexer bits/MnemonicString.java (MIT, © Autumn Lamonte).
using System.Text;

namespace Nexer.Ui;

/// <summary>
/// Parses a label containing a mnemonic marker: <c>"&amp;File"</c> displays <c>File</c> with <c>F</c> as the
/// mnemonic. A doubled ampersand (<c>"&amp;&amp;"</c>) is a literal <c>&amp;</c>. Only the first marker counts.
/// </summary>
/// <remarks>In XAML attributes the ampersand itself must be XML-escaped: <c>Text="&amp;amp;File"</c>, and a literal
/// ampersand is <c>&amp;amp;&amp;amp;</c>.</remarks>
public sealed class MnemonicString
{
    /// <summary>Parses <paramref name="label"/>.</summary>
    public MnemonicString(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        var sb = new StringBuilder();
        bool foundAmp = false, foundShortcut = false;
        int scanIdx = 0, scanScreenIdx = 0;
        foreach (var rune in label.EnumerateRunes())
        {
            if (rune.Value == '&')
            {
                if (foundAmp)
                {
                    sb.Append('&');
                    scanIdx++;
                    scanScreenIdx++;
                    foundAmp = false;
                }
                else
                {
                    foundAmp = true;
                }

                continue;
            }

            sb.Append(rune.ToString());
            if (foundAmp)
            {
                if (!foundShortcut)
                {
                    Shortcut = rune;
                    foundShortcut = true;
                    ShortcutIndex = scanIdx;
                    ScreenShortcutIndex = scanScreenIdx;
                }

                foundAmp = false;
            }

            scanIdx++;
            scanScreenIdx += TextMetrics.RuneWidth(rune);
        }

        RawLabel = sb.ToString();
    }

    /// <summary>The mnemonic character, or <c>default</c> when there is none.</summary>
    public Rune Shortcut { get; }

    /// <summary>True when a mnemonic was found.</summary>
    public bool HasShortcut => ShortcutIndex >= 0;

    /// <summary>Index (in scalars) of the mnemonic in <see cref="RawLabel"/>, or -1.</summary>
    public int ShortcutIndex { get; } = -1;

    /// <summary>Screen column of the mnemonic within <see cref="RawLabel"/>, or -1.</summary>
    public int ScreenShortcutIndex { get; } = -1;

    /// <summary>The label without markers.</summary>
    public string RawLabel { get; }

    /// <summary>True when <paramref name="c"/> matches the mnemonic case-insensitively.</summary>
    public bool Matches(Rune c) => HasShortcut && Rune.ToLowerInvariant(c) == Rune.ToLowerInvariant(Shortcut);
}
