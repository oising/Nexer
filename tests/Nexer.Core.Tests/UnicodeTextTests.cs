using System.Globalization;
using System.Text;
using Nexer.Core.Text;

namespace Nexer.Core.Tests;

public sealed class UnicodeTextTests
{
    [Fact]
    public void SegmenterPassesUnicode17OfficialExtendedGraphemeFixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestData", "GraphemeBreakTest-17.0.0.txt");
        int cases = 0;
        foreach (string line in File.ReadLines(path))
        {
            string[] fields = line.Split('#')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0) continue;
            var segmenter = new GraphemeSegmenter();
            for (int i = 1; i < fields.Length; i += 2)
            {
                var rune = new Rune(int.Parse(fields[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                bool expected = fields[i - 1] == "\u00f7";
                Assert.True(segmenter.Push(rune) == expected, $"Unexpected boundary at scalar {i / 2}: {line}");
            }

            cases++;
        }

        Assert.True(cases > 500);
    }

    [Theory]
    [InlineData("A", 1)]
    [InlineData("\u754c", 2)]
    [InlineData("e\u0301", 1)]
    [InlineData("\u0301", 0)]
    [InlineData("\u093e", 0)]
    [InlineData("\U0001f600", 2)]
    [InlineData("\u2764", 1)]
    [InlineData("\u2764\ufe0f", 2)]
    [InlineData("\u2764\ufe0e", 1)]
    [InlineData("\U0001f469\u200d\U0001f4bb", 2)]
    [InlineData("\U0001f1fa\U0001f1f8", 2)]
    [InlineData("#\ufe0f\u20e3", 2)]
    [InlineData("\u1100\u1161\u11a8", 2)]
    public void WidthPolicyHandlesTextEmojiAndCombiningClusters(string text, int expected)
    {
        Assert.Equal(expected, UnicodeText.GetWidth(text.EnumerateRunes().ToArray()));
    }

    [Fact]
    public void AmbiguousWidthIsAnExplicitPolicy()
    {
        Rune[] cluster = [new Rune(0x00a1)];
        Assert.Equal(1, UnicodeText.GetWidth(cluster));
        Assert.Equal(2, UnicodeText.GetWidth(cluster, ambiguousIsWide: true));
    }
}
