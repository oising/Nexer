// CONTRACT: Nexer.Ui.Data public surface (bindings). See docs\binding.md. Additive changes only, via the coordinator.
using System.ComponentModel;

namespace Nexer.Ui.Data;

/// <summary>
/// One step of a <see cref="CompiledBindingPath"/>: a property (or indexer) access resolved at compile time.
/// The XAML compiler emits the accessors as static methods; no reflection is used at runtime.
/// </summary>
public sealed class PathNode
{
    /// <summary>Creates a path node.</summary>
    /// <param name="name">
    /// The member name as raised by <see cref="INotifyPropertyChanged.PropertyChanged"/>: the property name, or
    /// <c>Item[]</c> for an indexer.
    /// </param>
    /// <param name="valueType">The declared type of the member.</param>
    /// <param name="get">
    /// Reads the member from a (non-null) owner object. Returns <see cref="BindingOperations.UnsetValue"/> when the
    /// owner is not an instance of the compile-time declaring type (e.g. a DataContext of the wrong type); the runtime
    /// then treats the path as unresolved (FallbackValue).
    /// </param>
    /// <param name="set">
    /// Writes the member on an owner object (a no-op when the owner has the wrong type; the value must already be of
    /// <paramref name="valueType"/>); <see langword="null"/> when read-only.
    /// </param>
    /// <param name="notifies">
    /// Whether the declaring type implements <see cref="INotifyPropertyChanged"/> (a compile-time hint; the runtime
    /// also checks the actual instance).
    /// </param>
    public PathNode(string name, Type valueType, Func<object, object?> get, Action<object, object?>? set, bool notifies)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(valueType);
        ArgumentNullException.ThrowIfNull(get);
        Name = name;
        ValueType = valueType;
        Get = get;
        Set = set;
        Notifies = notifies;
    }

    /// <summary>The member name (<c>Item[]</c> for indexers).</summary>
    public string Name { get; }

    /// <summary>The declared type of the member.</summary>
    public Type ValueType { get; }

    /// <summary>Reads the member from its owner.</summary>
    public Func<object, object?> Get { get; }

    /// <summary>Writes the member on its owner, or <see langword="null"/> when the member is read-only.</summary>
    public Action<object, object?>? Set { get; }

    /// <summary>Whether the declaring type implements <see cref="INotifyPropertyChanged"/>.</summary>
    public bool Notifies { get; }

    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>
/// A binding path resolved at compile time: where it starts (<see cref="Source"/>) and the ordered member accesses
/// (<see cref="Nodes"/>). An empty path (<c>{Binding}</c>) yields the source object itself.
/// </summary>
public sealed class CompiledBindingPath
{
    /// <summary>Creates a path. This is the constructor the XAML compiler emits.</summary>
    /// <param name="source">Where the path starts.</param>
    /// <param name="elementName">The element name for <see cref="BindingSourceKind.ElementName"/>, otherwise <see langword="null"/>.</param>
    /// <param name="ancestorType">The ancestor type for <see cref="BindingSourceKind.FindAncestor"/>, otherwise <see langword="null"/>.</param>
    /// <param name="ancestorLevel">The 1-based ancestor level for <see cref="BindingSourceKind.FindAncestor"/> (ignored otherwise).</param>
    /// <param name="nodes">The member accesses, outermost first.</param>
    public CompiledBindingPath(BindingSourceKind source, string? elementName, Type? ancestorType, int ancestorLevel, PathNode[] nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (source == BindingSourceKind.ElementName)
        {
            ArgumentException.ThrowIfNullOrEmpty(elementName);
        }

        if (source == BindingSourceKind.FindAncestor)
        {
            ArgumentNullException.ThrowIfNull(ancestorType);
            ArgumentOutOfRangeException.ThrowIfLessThan(ancestorLevel, 1);
        }

        Source = source;
        ElementName = elementName;
        AncestorType = ancestorType;
        AncestorLevel = source == BindingSourceKind.FindAncestor ? ancestorLevel : 0;
        Nodes = [.. nodes];
    }

    /// <summary>Creates a path starting at the target's <see cref="Widget.DataContext"/>.</summary>
    public CompiledBindingPath(params PathNode[] nodes)
        : this(BindingSourceKind.DataContext, null, null, 0, nodes)
    {
    }

    /// <summary>Where the path starts.</summary>
    public BindingSourceKind Source { get; }

    /// <summary>The element name (<see cref="BindingSourceKind.ElementName"/> only).</summary>
    public string? ElementName { get; }

    /// <summary>The ancestor type (<see cref="BindingSourceKind.FindAncestor"/> only).</summary>
    public Type? AncestorType { get; }

    /// <summary>The 1-based ancestor level (<see cref="BindingSourceKind.FindAncestor"/> only; 0 otherwise).</summary>
    public int AncestorLevel { get; }

    /// <summary>The member accesses, outermost first. Empty for <c>{Binding}</c>.</summary>
    public IReadOnlyList<PathNode> Nodes { get; }

    /// <summary>The declared type of the last node, or <see langword="null"/> for an empty path.</summary>
    public Type? ValueType => Nodes.Count == 0 ? null : Nodes[^1].ValueType;

    /// <summary>Whether the last node can be written (required for TwoWay/OneWayToSource).</summary>
    public bool IsWritable => Nodes.Count > 0 && Nodes[^1].Set is not null;

    /// <summary>The dotted path, e.g. <c>Customer.Name</c>.</summary>
    public override string ToString() => string.Join('.', Nodes.Select(n => n.Name));
}
