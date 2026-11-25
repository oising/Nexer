using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;

namespace Nexer.Ui.Markup;

/// <summary>
/// Registry of XAML documents compiled into assemblies at build time, and the entry point code-behind uses to
/// load its markup (<c>InitializeComponent</c>).
/// </summary>
/// <remarks>
/// The Nexer XAML build step (Nexer.Xaml.Build) rewrites each assembly containing XAML and emits a module
/// initializer that calls <see cref="Register"/> for every document, so registration happens automatically the
/// first time anything in that assembly runs. Code-behind calls <see cref="InitializeComponent{T}"/> (the generated
/// <c>InitializeComponent()</c> method does this), which works with or without the source generator.
/// </remarks>
public static class XamlComponent
{
    private static readonly ConcurrentDictionary<Type, XamlDocumentRegistration> s_components = new();
    private static readonly ConcurrentDictionary<(Assembly Assembly, string LogicalName), XamlDocumentRegistration> s_documents = new();

    /// <summary>
    /// Optional fallback used when no compiled XAML is registered for a component type (for example to load XAML at
    /// runtime during development). Returns <see langword="true"/> if it populated the component.
    /// </summary>
    public static Func<Type, object, IServiceProvider?, bool>? Fallback { get; set; }

    /// <summary>Called by the module initializer emitted by the XAML build step; not intended for direct use.</summary>
    /// <param name="assemblyAnchor">Any type from the assembly containing the document.</param>
    /// <param name="logicalName">The document's logical name (project-relative path with forward slashes).</param>
    /// <param name="componentType">The <c>x:Class</c> type, or <see langword="null"/> when the document has none.</param>
    /// <param name="populate">Populates an existing root instance.</param>
    /// <param name="build">Creates and populates a new root instance, or <see langword="null"/> when the root cannot be constructed.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void Register(
        Type assemblyAnchor,
        string logicalName,
        Type? componentType,
        Action<IServiceProvider?, object> populate,
        Func<IServiceProvider?, object>? build)
    {
        ArgumentNullException.ThrowIfNull(assemblyAnchor);
        ArgumentNullException.ThrowIfNull(logicalName);
        ArgumentNullException.ThrowIfNull(populate);
        var registration = new XamlDocumentRegistration(assemblyAnchor.Assembly, logicalName, componentType, populate, build);
        s_documents[(registration.Assembly, NormalizeLogicalName(logicalName))] = registration;
        if (componentType is not null)
        {
            s_components[componentType] = registration;
        }
    }

    /// <summary>Returns whether compiled XAML is registered for <paramref name="componentType"/> (its <c>x:Class</c>).</summary>
    public static bool IsRegistered(Type componentType) => s_components.ContainsKey(componentType);

    /// <summary>
    /// Loads the XAML whose <c>x:Class</c> is <typeparamref name="T"/> into <paramref name="component"/>.
    /// Call it from the constructor of the code-behind class (as <c>XamlComponent.InitializeComponent(this)</c>).
    /// </summary>
    /// <typeparam name="T">The code-behind type; inferred from <c>this</c>, so derived classes still load their base's XAML.</typeparam>
    /// <exception cref="InvalidOperationException">No compiled XAML is registered for <typeparamref name="T"/>.</exception>
    public static void InitializeComponent<T>(T component, IServiceProvider? services = null)
        where T : class => InitializeComponent(typeof(T), component, services);

    /// <summary>Loads the XAML whose <c>x:Class</c> is <paramref name="componentType"/> into <paramref name="component"/>.</summary>
    /// <exception cref="InvalidOperationException">No compiled XAML is registered for <paramref name="componentType"/>.</exception>
    public static void InitializeComponent(Type componentType, object component, IServiceProvider? services = null)
    {
        if (!TryInitializeComponent(componentType, component, services))
        {
            throw new InvalidOperationException(
                $"No compiled XAML is registered for '{componentType.FullName}'. Make sure the project imports " +
                "Nexer.Xaml.targets, the .xaml file is a NexerXaml item and its x:Class is '" + componentType.FullName + "'.");
        }
    }

    /// <summary>Like <see cref="InitializeComponent(Type, object, IServiceProvider?)"/>, but returns <see langword="false"/> instead of throwing.</summary>
    public static bool TryInitializeComponent(Type componentType, object component, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        ArgumentNullException.ThrowIfNull(component);
        if (!componentType.IsInstanceOfType(component))
        {
            throw new ArgumentException($"The component is not a '{componentType.FullName}'.", nameof(component));
        }

        if (s_components.TryGetValue(componentType, out var registration))
        {
            registration.Populate(services, component);
            return true;
        }

        return Fallback?.Invoke(componentType, component, services) ?? false;
    }

    /// <summary>Creates the root object of a compiled XAML document by logical name (e.g. <c>"Views/About.xaml"</c>).</summary>
    /// <exception cref="InvalidOperationException">The document is not registered or its root cannot be constructed.</exception>
    public static object Build(Assembly assembly, string logicalName, IServiceProvider? services = null)
    {
        var registration = GetDocument(assembly, logicalName);
        return registration.Build is { } build
            ? build(services)
            : throw new InvalidOperationException($"The root of XAML document '{logicalName}' cannot be constructed; use Populate.");
    }

    /// <summary>Populates <paramref name="root"/> from a compiled XAML document by logical name.</summary>
    public static void Populate(Assembly assembly, string logicalName, object root, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        GetDocument(assembly, logicalName).Populate(services, root);
    }

    /// <summary>Returns the registrations of all compiled documents in <paramref name="assembly"/>.</summary>
    public static IReadOnlyList<XamlDocumentRegistration> GetDocuments(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        RunModuleInitializer(assembly);
        return s_documents.Values.Where(d => d.Assembly == assembly).OrderBy(d => d.LogicalName, StringComparer.Ordinal).ToList();
    }

    private static XamlDocumentRegistration GetDocument(Assembly assembly, string logicalName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(logicalName);
        RunModuleInitializer(assembly);
        return s_documents.TryGetValue((assembly, NormalizeLogicalName(logicalName)), out var registration)
            ? registration
            : throw new InvalidOperationException($"No compiled XAML document '{logicalName}' is registered for assembly '{assembly.GetName().Name}'.");
    }

    // Documents are registered by the assembly's module initializer, which only runs once code in that assembly runs.
    private static void RunModuleInitializer(Assembly assembly)
    {
        foreach (var module in assembly.GetModules())
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(module.ModuleHandle);
        }
    }

    private static string NormalizeLogicalName(string logicalName) => logicalName.Replace('\\', '/').TrimStart('/');
}

/// <summary>A compiled XAML document registered with <see cref="XamlComponent"/>.</summary>
/// <param name="Assembly">The assembly containing the document.</param>
/// <param name="LogicalName">The document's logical name.</param>
/// <param name="ComponentType">The <c>x:Class</c> type, if any.</param>
/// <param name="Populate">Populates an existing root instance.</param>
/// <param name="Build">Creates and populates a new root instance, if the root type can be constructed.</param>
public sealed record XamlDocumentRegistration(
    Assembly Assembly,
    string LogicalName,
    Type? ComponentType,
    Action<IServiceProvider?, object> Populate,
    Func<IServiceProvider?, object>? Build);
