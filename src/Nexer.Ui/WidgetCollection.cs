using System.Collections;

namespace Nexer.Ui;

/// <summary>
/// The children of a <see cref="Widget"/>. Adding a child sets its <see cref="Widget.Parent"/> and applies
/// Jexer's <c>addChild</c> rules (the new enabled child becomes active; tab order follows insertion order).
/// </summary>
public sealed class WidgetCollection : IList<Widget>, IReadOnlyList<Widget>
{
    private readonly Widget owner;
    private readonly List<Widget> items;

    internal WidgetCollection(Widget owner, List<Widget> items)
    {
        this.owner = owner;
        this.items = items;
    }

    /// <inheritdoc/>
    public Widget this[int index]
    {
        get => items[index];
        set
        {
            RemoveAt(index);
            Insert(index, value);
        }
    }

    /// <inheritdoc/>
    public int Count => items.Count;

    /// <inheritdoc/>
    public bool IsReadOnly => false;

    /// <inheritdoc/>
    public void Add(Widget item) => owner.AttachChild(item, items.Count);

    /// <inheritdoc/>
    public void Insert(int index, Widget item) => owner.AttachChild(item, index);

    /// <inheritdoc/>
    public bool Remove(Widget item)
    {
        if (!items.Contains(item)) return false;
        owner.DetachChild(item);
        return true;
    }

    /// <inheritdoc/>
    public void RemoveAt(int index) => owner.DetachChild(items[index]);

    /// <inheritdoc/>
    public void Clear()
    {
        while (items.Count > 0) owner.DetachChild(items[^1]);
    }

    /// <inheritdoc/>
    public bool Contains(Widget item) => items.Contains(item);

    /// <inheritdoc/>
    public int IndexOf(Widget item) => items.IndexOf(item);

    /// <inheritdoc/>
    public void CopyTo(Widget[] array, int arrayIndex) => items.CopyTo(array, arrayIndex);

    /// <summary>Returns a struct enumerator over a snapshot-free view (do not mutate while enumerating).</summary>
    public List<Widget>.Enumerator GetEnumerator() => items.GetEnumerator();

    IEnumerator<Widget> IEnumerable<Widget>.GetEnumerator() => items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();
}

/// <summary>A resource dictionary (string keys) authorable as XAML property-element content.</summary>
public sealed class ResourceDictionary : Dictionary<string, object?>
{
    /// <summary>Creates an empty dictionary.</summary>
    public ResourceDictionary() : base(StringComparer.Ordinal)
    {
    }
}
