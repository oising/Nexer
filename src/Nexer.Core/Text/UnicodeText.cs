using System.Text;

namespace Nexer.Core.Text;

public static class UnicodeText
{
    public const string Version = "17.0.0";

    public static int GetWidth(ReadOnlySpan<Rune> cluster, bool ambiguousIsWide = false)
    {
        int width = 0;
        bool pictographic = false, emoji = false, textPresentation = false, emojiSelector = false, joiner = false, keycap = false;
        foreach (Rune rune in cluster)
        {
            int value = rune.Value;
            int property = Lookup(UnicodeData.Grapheme, value);
            if (property is not (1 or 2 or 3 or 4 or 5 or 7 or 8))
            {
                int eastAsian = Lookup(UnicodeData.EastAsian, value);
                width = Math.Max(width, eastAsian == 1 || (eastAsian == 2 && ambiguousIsWide) ? 2 : 1);
            }

            pictographic |= Lookup(UnicodeData.Pictographic, value) != 0;
            emoji |= Lookup(UnicodeData.EmojiPresentation, value) != 0;
            textPresentation |= value == 0xfe0e;
            emojiSelector |= value == 0xfe0f;
            joiner |= value == 0x200d;
            keycap |= value == 0x20e3 && cluster[0].Value is >= '0' and <= '9' or '#' or '*';
        }

        if (!textPresentation && (emoji || (pictographic && (emojiSelector || joiner)) || keycap))
        {
            width = 2;
        }

        return width;
    }

    internal static int Lookup(ReadOnlySpan<int> table, int scalar)
    {
        int low = 0, high = table.Length / 3 - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            int index = middle * 3;
            if (scalar < table[index])
            {
                high = middle - 1;
            }
            else if (scalar > table[index + 1])
            {
                low = middle + 1;
            }
            else
            {
                return table[index + 2];
            }
        }

        return 0;
    }
}

/// <summary>Unicode 17 extended grapheme boundaries (UAX #29), with constant retained state.</summary>
public struct GraphemeSegmenter
{
    private bool started, pictographicRun, previousZwjAfterPictographic, consonantRun, linkerSeen;
    private int previous, regionalCount;

    /// <returns>True when the scalar begins a new extended grapheme cluster.</returns>
    public bool Push(Rune rune)
    {
        int property = UnicodeText.Lookup(UnicodeData.Grapheme, rune.Value);
        int conjunct = UnicodeText.Lookup(UnicodeData.Conjunct, rune.Value);
        bool pictographic = UnicodeText.Lookup(UnicodeData.Pictographic, rune.Value) != 0;
        bool boundary = !started || BreakBefore(property, conjunct, pictographic);

        bool zwjAfterPictographic = property == 5 && pictographicRun;
        pictographicRun = pictographic || (property == 4 && pictographicRun);
        previousZwjAfterPictographic = zwjAfterPictographic;
        regionalCount = property == 6 ? regionalCount + 1 : 0;
        // Only parity matters; cap the counter for indefinitely long RI streams.
        if (regionalCount > 2)
        {
            regionalCount -= 2;
        }

        if (conjunct == 1)
        {
            consonantRun = true;
            linkerSeen = false;
        }
        else if (conjunct is 2 or 3)
        {
            linkerSeen |= consonantRun && conjunct == 3;
        }
        else
        {
            consonantRun = false;
            linkerSeen = false;
        }

        previous = property;
        started = true;
        return boundary;
    }

    private readonly bool BreakBefore(int current, int conjunct, bool pictographic)
    {
        if (previous == 1 && current == 2) return false;
        if (previous is 1 or 2 or 3 || current is 1 or 2 or 3) return true;
        if (previous == 9 && current is 9 or 10 or 12 or 13) return false;
        if (previous is 10 or 12 && current is 10 or 11) return false;
        if (previous is 11 or 13 && current == 11) return false;
        if (current is 4 or 5 or 8 || previous == 7) return false;
        if (conjunct == 1 && consonantRun && linkerSeen) return false;
        if (previous == 5 && previousZwjAfterPictographic && pictographic) return false;
        if (previous == 6 && current == 6 && regionalCount % 2 == 1) return false;
        return true;
    }
}
