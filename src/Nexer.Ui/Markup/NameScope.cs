using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Nexer.Ui.Markup;

/// <summary>
/// Maps <c>x:Name</c> values to the objects created by a XAML document. The compiled XAML registers every
/// named element in the name scope of the document's root object, so code-behind can look elements up by name.
/// </summary>
/// <remarks>
/// Scopes are attached to root objects without requiring any base class (a <see cref="ConditionalWeakTable{TKey,TValue}"/>
/// keyed by the root). Registering a name twice replaces the earlier entry (templates realized repeatedly re-register).
/// </remarks>
public sealed class NameScope
{
    private static readonly ConditionalWeakTable<object, NameScope> s_scopes = new();
    private readonly Dictionary<string, object> _names = new(StringComparer.Ordinal);

    /// <summary>The registered names.</summary>
    public IEnumerable<string> Names => _names.Keys;

    /// <summary>Registers (or replaces) <paramref name="element"/> under <paramref name="name"/>.</summary>
    public void Register(string name, object element)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(element);
        _names[name] = element;
    }

    /// <summary>Removes <paramref name="name"/>; returns whether it was registered.</summary>
    public bool Unregister(string name) => _names.Remove(name);

    /// <summary>Returns the object registered under <paramref name="name"/>, or <see langword="null"/>.</summary>
    public object? Find(string name) => _names.TryGetValue(name, out var value) ? value : null;

    /// <summary>Returns the object registered under <paramref name="name"/> if it is a <typeparamref name="T"/>.</summary>
    public T? Find<T>(string name)
        where T : class => Find(name) as T;

    /// <summary>Returns the name scope attached to <paramref name="root"/>, or <see langword="null"/>.</summary>
    public static NameScope? GetNameScope(object root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return s_scopes.TryGetValue(root, out var scope) ? scope : null;
    }

    /// <summary>Attaches <paramref name="scope"/> to <paramref name="root"/> (or detaches it when <see langword="null"/>).</summary>
    public static void SetNameScope(object root, NameScope? scope)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (scope is null)
        {
            s_scopes.Remove(root);
        }
        else
        {
            s_scopes.AddOrUpdate(root, scope);
        }
    }

    /// <summary>Returns the name scope attached to <paramref name="root"/>, creating and attaching one if needed.</summary>
    public static NameScope GetOrCreateNameScope(object root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return s_scopes.GetValue(root, static _ => new NameScope());
    }

    /// <summary>Finds a named element in the name scope of <paramref name="root"/>.</summary>
    public static object? FindName(object root, string name) => GetNameScope(root)?.Find(name);

    /// <summary>Finds a named element of type <typeparamref name="T"/> in the name scope of <paramref name="root"/>.</summary>
    public static T? FindName<T>(object root, string name)
        where T : class => FindName(root, name) as T;

    /// <summary>Called by compiled XAML for every <c>x:Name</c>; not intended for direct use.</summary>
    /// <param name="element">The named element.</param>
    /// <param name="root">The document's root object (owner of the name scope).</param>
    /// <param name="name">The <c>x:Name</c> value.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RegisterXamlName(object element, object? root, string name)
    {
        ArgumentNullException.ThrowIfNull(element);
        GetOrCreateNameScope(root ?? element).Register(name, element);
    }
}
