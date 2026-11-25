using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using Nexer.Xaml.Compiler;
using XamlX.IL;
using XamlX.TypeSystem;

namespace Nexer.Xaml;

/// <summary>
/// Compiles and loads XAML at runtime (System.Reflection.Emit). Intended for tests, tools and dynamic UI; apps
/// normally compile XAML at build time (Nexer.Xaml.targets), which needs no runtime code generation.
/// </summary>
/// <remarks>
/// <para>Each distinct (XAML text, root type, base URI) is compiled once into a dynamic assembly and cached.</para>
/// <para>Types are resolved from loaded assemblies. The loader force-loads Nexer.Ui, the root instance's assembly,
/// the calling assembly and their references, so xmlns definitions in those assemblies are visible; load other
/// assemblies first (or reference one of their types) if XAML uses them via <c>clr-namespace:</c>.</para>
/// <para>Compiled code may bind to non-public event handlers and members of the root type's assembly
/// (<c>IgnoresAccessChecksTo</c>).</para>
/// </remarks>
public static class XamlLoader
{
    private static readonly object s_lock = new();
    private static readonly ConcurrentDictionary<CacheKey, CompiledXaml> s_cache = new();
    private static readonly HashSet<string> s_forceLoaded = new(StringComparer.Ordinal);
    private static NexerXamlCompiler? s_compiler;
    private static SreTypeSystem? s_typeSystem;
    private static int s_assemblyCount = -1;
    private static int s_assemblyCounter;

    /// <summary>
    /// Compiles (or reuses) <paramref name="xaml"/> and loads it. When <paramref name="rootInstance"/> is supplied it is
    /// populated (code-behind style) and returned; otherwise a new root object is created.
    /// </summary>
    /// <param name="xaml">The XAML text.</param>
    /// <param name="rootInstance">Existing root object to populate, or <see langword="null"/> to create one.</param>
    /// <param name="baseUri">Document URI (exposed through <c>IUriContext</c> and used in diagnostics).</param>
    /// <param name="services">Optional service provider passed to markup extensions (e.g. an application-level <c>IResourceProvider</c>).</param>
    /// <returns>The root object.</returns>
    /// <exception cref="NexerXamlCompilationException">The XAML has errors; <see cref="NexerXamlCompilationException.Diagnostics"/> lists them.</exception>
    public static object Load(string xaml, object? rootInstance = null, Uri? baseUri = null, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(xaml);
        var compiled = Compile(xaml, rootInstance?.GetType(), baseUri, Assembly.GetCallingAssembly());
        if (rootInstance is not null)
        {
            compiled.Populate(services, rootInstance);
            return rootInstance;
        }

        return compiled.Build is { } build
            ? build(services)
            : throw new InvalidOperationException(
                $"The XAML root type '{compiled.RootType}' has no public parameterless constructor; pass a root instance to populate.");
    }

    /// <summary>Like <see cref="Load(string, object?, Uri?, IServiceProvider?)"/>, typed.</summary>
    /// <typeparam name="T">Expected root type.</typeparam>
    /// <exception cref="InvalidCastException">The root object is not a <typeparamref name="T"/>.</exception>
    public static T Load<T>(string xaml, T? rootInstance = null, Uri? baseUri = null, IServiceProvider? services = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(xaml);
        var compiled = Compile(xaml, rootInstance?.GetType(), baseUri, Assembly.GetCallingAssembly());
        if (rootInstance is not null)
        {
            compiled.Populate(services, rootInstance);
            return rootInstance;
        }

        var result = compiled.Build is { } build
            ? build(services)
            : throw new InvalidOperationException(
                $"The XAML root type '{compiled.RootType}' has no public parameterless constructor; pass a root instance to populate.");
        return result as T ?? throw new InvalidCastException(
            $"The XAML root is a '{result.GetType().FullName}', not a '{typeof(T).FullName}'.");
    }

    /// <summary>Compiles <paramref name="xaml"/> without loading it, returning the diagnostics (empty when it compiles).</summary>
    /// <param name="xaml">The XAML text.</param>
    /// <param name="rootType">Root type override (as when loading into an instance of that type).</param>
    /// <param name="baseUri">Document URI used in diagnostics.</param>
    public static IReadOnlyList<NexerXamlDiagnostic> Validate(string xaml, Type? rootType = null, Uri? baseUri = null)
    {
        ArgumentNullException.ThrowIfNull(xaml);
        try
        {
            var compiled = Compile(xaml, rootType, baseUri, Assembly.GetCallingAssembly());
            return compiled.Warnings;
        }
        catch (NexerXamlCompilationException e)
        {
            return e.Diagnostics;
        }
    }

    /// <summary>Number of cached compiled documents.</summary>
    public static int CacheCount => s_cache.Count;

    /// <summary>Clears the compiled-document cache (the dynamic assemblies stay loaded).</summary>
    public static void ClearCache() => s_cache.Clear();

    private static CompiledXaml Compile(string xaml, Type? rootType, Uri? baseUri, Assembly callingAssembly)
    {
        var key = new CacheKey(xaml, rootType, baseUri?.ToString());
        if (s_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        lock (s_lock)
        {
            if (s_cache.TryGetValue(key, out cached))
            {
                return cached;
            }

            EnsureAssembliesLoaded(rootType?.Assembly, callingAssembly);
            var compiler = GetCompiler();
            var typeSystem = s_typeSystem!;
            compiler.ClearDiagnostics();
            var file = baseUri?.ToString() ?? "XAML";
            var rootOverride = rootType is null ? null : typeSystem.GetType(rootType);
            var document = compiler.Parse(xaml, file, rootOverride);
            if (document is null)
            {
                throw new NexerXamlCompilationException([.. compiler.Diagnostics]);
            }

            var index = Interlocked.Increment(ref s_assemblyCounter);
            var assemblyName = new AssemblyName("Nexer.Xaml.Dynamic" + index);
            var assembly = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
            foreach (var target in GetAccessTargets(rootType, document))
            {
                assembly.SetCustomAttribute(new CustomAttributeBuilder(
                    typeof(System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute).GetConstructor([typeof(string)])!, [target]));
            }

            var module = assembly.DefineDynamicModule(assemblyName.Name!);
            var contextBuilder = module.DefineType("XamlContext", TypeAttributes.NotPublic | TypeAttributes.Class);
            var documentBuilder = module.DefineType("XamlDocument", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed);
            var contextType = compiler.CreateContextType(typeSystem.CreateTypeBuilder(contextBuilder));
            var methods = compiler.Emit(document, contextType, typeSystem.CreateTypeBuilder(documentBuilder), "", baseUri?.ToString(),
                emitBuild: true, classBuildUsesConstructor: false);
            if (methods is null || compiler.HasErrors)
            {
                throw new NexerXamlCompilationException([.. compiler.Diagnostics]);
            }

            contextBuilder.CreateType();
            var type = documentBuilder.CreateType();
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var populate = type.GetMethod(methods.PopulateTrampoline.Name, flags)!.CreateDelegate<Action<IServiceProvider?, object>>();
            var build = methods.BuildTrampoline is null
                ? null
                : type.GetMethod(methods.BuildTrampoline.Name, flags)!.CreateDelegate<Func<IServiceProvider?, object>>();
            var compiled = new CompiledXaml(document.RootType.GetFqn(), populate, build, [.. compiler.Diagnostics]);
            s_cache[key] = compiled;
            return compiled;
        }
    }

    private static IEnumerable<string> GetAccessTargets(Type? rootType, NexerXamlDocument document)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (rootType?.Assembly.GetName().Name is { } rootAssembly)
        {
            names.Add(rootAssembly);
        }

        if (document.RootType.Assembly?.Name is { } documentAssembly)
        {
            names.Add(documentAssembly);
        }

        return names;
    }

    private static NexerXamlCompiler GetCompiler()
    {
        var count = AppDomain.CurrentDomain.GetAssemblies().Count(a => !a.IsDynamic);
        if (s_compiler is null || count != s_assemblyCount)
        {
            s_typeSystem = new SreTypeSystem();
            s_compiler = new NexerXamlCompiler(s_typeSystem, null);
            s_assemblyCount = count;
        }

        return s_compiler;
    }

    private static void EnsureAssembliesLoaded(Assembly? rootAssembly, Assembly callingAssembly)
    {
        // SreTypeSystem only sees assemblies that are already loaded.
        _ = typeof(Uri).Assembly;
        _ = typeof(System.ComponentModel.ISupportInitialize).Assembly;
        _ = typeof(System.ComponentModel.TypeConverter).Assembly;
        _ = typeof(System.ComponentModel.TypeConverterAttribute).Assembly;
        _ = typeof(System.Collections.ObjectModel.Collection<>).Assembly;
        _ = typeof(Nexer.Ui.Markup.XamlNamespaces).Assembly;
        _ = typeof(Nexer.Core.CellStyle).Assembly;
        LoadWithReferences(typeof(Nexer.Ui.Markup.XamlNamespaces).Assembly);
        LoadWithReferences(callingAssembly);
        if (rootAssembly is not null)
        {
            LoadWithReferences(rootAssembly);
        }

        if (Assembly.GetEntryAssembly() is { } entry)
        {
            LoadWithReferences(entry);
        }
    }

    private static void LoadWithReferences(Assembly assembly)
    {
        if (assembly.IsDynamic || !s_forceLoaded.Add(assembly.FullName ?? assembly.GetName().Name ?? ""))
        {
            return;
        }

        foreach (var reference in assembly.GetReferencedAssemblies())
        {
            try
            {
                Assembly.Load(reference);
            }
            catch (Exception e) when (e is FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                // Optional references (e.g. trimmed or platform-specific) are not needed for XAML resolution.
            }
        }
    }

    private readonly record struct CacheKey(string Xaml, Type? RootType, string? BaseUri);

    private sealed record CompiledXaml(
        string RootType,
        Action<IServiceProvider?, object> Populate,
        Func<IServiceProvider?, object>? Build,
        IReadOnlyList<NexerXamlDiagnostic> Warnings);
}
