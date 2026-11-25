using System.Text;

namespace Nexer.Core.Text;

internal sealed class ClusterStore(int maxBytes)
{
    private sealed class Entry(string text)
    {
        internal string Text = text;
        internal int References = 1;
    }

    private readonly Dictionary<string, int> lookup = new(StringComparer.Ordinal);
    private readonly List<Entry?> entries = [];
    private readonly Stack<int> free = [];
    internal int RetainedBytes { get; private set; }

    internal int Acquire(ReadOnlySpan<Rune> cluster)
    {
        Span<char> buffer = stackalloc char[CellSurface.MaxClusterScalars * 2];
        int length = 0;
        foreach (Rune rune in cluster)
        {
            length += rune.EncodeToUtf16(buffer[length..]);
        }

        ReadOnlySpan<char> text = buffer[..length];
        if (lookup.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(text, out int id))
        {
            Retain(id);
            return id;
        }

        int bytes = length * sizeof(char);
        if (bytes > maxBytes - RetainedBytes)
        {
            return 0;
        }

        var entry = new Entry(new string(text));
        if (free.TryPop(out int index))
        {
            entries[index] = entry;
        }
        else
        {
            index = entries.Count;
            entries.Add(entry);
        }

        id = index + 1;
        lookup.Add(entry.Text, id);
        RetainedBytes += bytes;
        return id;
    }

    internal string Resolve(int id) => entries[id - 1]!.Text;

    internal void Retain(int id)
    {
        if (id != 0) entries[id - 1]!.References++;
    }

    internal void Release(int id)
    {
        if (id == 0) return;
        Entry entry = entries[id - 1]!;
        if (--entry.References != 0) return;
        lookup.Remove(entry.Text);
        RetainedBytes -= entry.Text.Length * sizeof(char);
        entries[id - 1] = null;
        free.Push(id - 1);
    }
}
