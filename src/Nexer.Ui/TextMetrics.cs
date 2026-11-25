using System.Text;
using Nexer.Core.Text;

namespace Nexer.Ui;

/// <summary>Grapheme- and width-aware text measurement (the same width policy the renderer uses).</summary>
public static class TextMetrics
{
    /// <summary>Display width of a single scalar.</summary>
    public static int RuneWidth(Rune rune)
    {
        Span<Rune> one = [rune];
        return UnicodeText.GetWidth(one);
    }

    /// <summary>Display width of <paramref name="text"/> in cells.</summary>
    public static int Width(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int width = 0;
        foreach (var (_, w) in Clusters(text)) width += w;
        return width;
    }

    /// <summary>Splits text into extended grapheme clusters with their display widths.</summary>
    public static IEnumerable<(Rune[] Cluster, int Width)> Clusters(string? text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        var segmenter = new GraphemeSegmenter();
        var current = new List<Rune>(4);
        foreach (var rune in text.EnumerateRunes())
        {
            if (segmenter.Push(rune) && current.Count > 0)
            {
                var c = current.ToArray();
                yield return (c, UnicodeText.GetWidth(c));
                current.Clear();
            }

            current.Add(rune);
        }

        if (current.Count > 0)
        {
            var c = current.ToArray();
            yield return (c, UnicodeText.GetWidth(c));
        }
    }

    /// <summary>Returns the longest prefix of <paramref name="text"/> whose width is at most <paramref name="columns"/>.</summary>
    public static string Truncate(string? text, int columns)
    {
        if (string.IsNullOrEmpty(text) || columns <= 0) return "";
        var sb = new StringBuilder();
        int width = 0;
        foreach (var (cluster, w) in Clusters(text))
        {
            if (width + w > columns) break;
            foreach (var r in cluster) sb.Append(r.ToString());
            width += w;
        }

        return sb.ToString();
    }

    /// <summary>Splits text into runes (scalars).</summary>
    public static List<Rune> ToRunes(string? text)
    {
        var list = new List<Rune>();
        if (text != null) foreach (var r in text.EnumerateRunes()) list.Add(r);
        return list;
    }

    /// <summary>Word-wraps <paramref name="text"/> into lines of at most <paramref name="width"/> cells (Jexer StringUtils.left).</summary>
    public static List<string> WrapLeft(string text, int width)
    {
        var result = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = new StringBuilder();
            int lineWidth = 0;
            foreach (var word in paragraph.Split(' '))
            {
                int w = Width(word);
                if (lineWidth > 0 && lineWidth + 1 + w > width)
                {
                    result.Add(line.ToString());
                    line.Clear();
                    lineWidth = 0;
                }

                if (lineWidth > 0)
                {
                    line.Append(' ');
                    lineWidth++;
                }

                line.Append(word);
                lineWidth += w;
            }

            result.Add(line.ToString());
        }

        return result;
    }

    /// <summary>Word-wraps and right-justifies (Jexer StringUtils.right).</summary>
    public static List<string> WrapRight(string text, int width) =>
        WrapLeft(text, width).ConvertAll(line => new string(' ', Math.Max(0, width - Width(line))) + line);

    /// <summary>Word-wraps and centres (Jexer StringUtils.center).</summary>
    public static List<string> WrapCenter(string text, int width) =>
        WrapLeft(text, width).ConvertAll(line =>
        {
            int l = Math.Max(0, (width - Width(line)) / 2);
            int r = Math.Max(0, width - Width(line) - l);
            return new string(' ', l) + line + new string(' ', r);
        });

    /// <summary>Word-wraps and fully justifies all but the last line (Jexer StringUtils.full).</summary>
    public static List<string> WrapFull(string text, int width)
    {
        var lines = WrapLeft(text, width);
        var result = new List<string>(lines.Count);
        for (int i = 0; i < lines.Count - 1; i++)
        {
            var words = lines[i].Split(' ');
            if (words.Length <= 1)
            {
                result.Add(lines[i]);
                continue;
            }

            int charCount = 0;
            foreach (var w in words) charCount += Width(w);
            int spaceCount = Math.Max(0, width - charCount);
            int q = spaceCount / (words.Length - 1);
            int r = spaceCount % (words.Length - 1);
            var sb = new StringBuilder();
            for (int j = 0; j < words.Length - 1; j++)
            {
                sb.Append(words[j]).Append(' ', q);
                if (r > 0)
                {
                    sb.Append(' ');
                    r--;
                }
            }

            sb.Append(words[^1]);
            result.Add(sb.ToString());
        }

        if (lines.Count > 0) result.Add(lines[^1]);
        return result;
    }
}
