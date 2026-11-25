namespace Nexer.Ui.Markup;

// CONTRACT: marker attributes and service interfaces mapped into XamlX's XamlLanguageTypeMappings
// by Nexer.Xaml.Compiler. XamlX matches them by type identity and member NAME, so do not rename
// members. Changes go through the coordinator; see docs\architecture.md.

public static class XamlNamespaces
{
    public const string Ui = "https://nexer.dev/ui";
}

/// <summary>Maps an XML namespace to a CLR namespace in this assembly.</summary>
/// <remarks>XamlX requires the constructor to take exactly (xmlNamespace, clrNamespace).</remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) : Attribute
{
    public string XmlNamespace { get; } = xmlNamespace;
    public string ClrNamespace { get; } = clrNamespace;
}

/// <summary>
/// On a property: marks it as the content property. On a class: <see cref="Name"/> MUST be set
/// to the content property's name. Inherited by derived types; one per type.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
public sealed class ContentAttribute : Attribute
{
    public string? Name { get; set; }
}

/// <summary>
/// The property receives a <c>Func&lt;IServiceProvider, object&gt;</c> factory (or the value returned by the
/// configured deferred-content customization) instead of an instance. The property must be typed
/// <see cref="object"/> or a type the factory/customization result is assignable to.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DeferredContentAttribute : Attribute
{
}

/// <summary>Objects of this type are added to their parent before their own properties are set (top-down).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class UsableDuringInitializationAttribute(bool usable = true) : Attribute
{
    public bool Usable { get; } = usable;
}

/// <summary>Whitespace between mixed text and elements in this collection is preserved.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class WhitespaceSignificantCollectionAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class TrimSurroundingWhitespaceAttribute : Attribute
{
}

/// <summary>Available from markup extensions and type converters via the service provider.</summary>
public interface IRootObjectProvider
{
    object? RootObject { get; }
}

public interface IProvideValueTarget
{
    object? TargetObject { get; }
    object? TargetProperty { get; }
}

public interface IUriContext
{
    Uri? BaseUri { get; set; }
}

/// <summary>Objects currently being constructed, nearest ancestor first.</summary>
/// <remarks>
/// A caller may pass an implementation into Populate/Build; its parents are appended after the
/// in-document parents (use this to load XAML into an existing tree).
/// </remarks>
public interface IParentStackProvider
{
    IEnumerable<object> Parents { get; }
}

public sealed class XmlNamespaceInfo
{
    public string ClrNamespace { get; set; } = "";
    public string ClrAssemblyName { get; set; } = "";
}

public interface IXmlNamespaceInfoProvider
{
    IReadOnlyDictionary<string, IReadOnlyList<XmlNamespaceInfo>> XmlNamespaces { get; }
}

/// <summary>Used for child elements when the parent has no content property.</summary>
public interface IAddChild
{
    void AddChild(object child);
}

public interface IAddChild<in T> : IAddChild
{
    void AddChild(T child);
}
