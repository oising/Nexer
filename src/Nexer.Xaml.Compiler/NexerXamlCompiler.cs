using System.Reflection.Emit;
using System.Text;
using Nexer.Xaml.Compiler.Transformers;
using XamlX;
using XamlX.Ast;
using XamlX.Emit;
using XamlX.IL;
using XamlX.Parsers;
using XamlX.Transform;
using XamlX.Transform.Transformers;
using XamlX.TypeSystem;

namespace Nexer.Xaml.Compiler;

/// <summary>A parsed and transformed XAML document, ready to be emitted.</summary>
public sealed class NexerXamlDocument
{
    internal NexerXamlDocument(
        XamlDocument document,
        XamlILCompiler compiler,
        IXamlType rootType,
        IXamlType? classType,
        string? filePath,
        IReadOnlyCollection<string> accessedAssemblies)
    {
        AccessedAssemblies = accessedAssemblies;
        Document = document;
        Compiler = compiler;
        RootType = rootType;
        ClassType = classType;
        FilePath = filePath;
    }

    /// <summary>The XamlX document (after transformation).</summary>
    public XamlDocument Document { get; }

    /// <summary>The root object type (the <c>x:Class</c> type when present).</summary>
    public IXamlType RootType { get; }

    /// <summary>The <c>x:Class</c> type, if any.</summary>
    public IXamlType? ClassType { get; }

    /// <summary>The source file path, if any.</summary>
    public string? FilePath { get; }

    /// <summary>
    /// Names of the assemblies whose members compiled bindings access directly (view models, target widgets). Hosts
    /// emitting into a separate assembly must be granted access to non-public members of these assemblies.
    /// </summary>
    public IReadOnlyCollection<string> AccessedAssemblies { get; }

    internal XamlILCompiler Compiler { get; }

    /// <summary>Whether XamlX can emit a Build method (the root type has a public parameterless constructor).</summary>
    public bool CanBuild =>
        ((XamlValueWithManipulationNode)Document.Root).Value is XamlAstNewClrObjectNode newObject
        && newObject.Constructor.IsPublic
        && newObject.Constructor.Parameters.Count == newObject.Arguments.Count;
}

/// <summary>Methods emitted for a document.</summary>
/// <param name="Populate">Typed <c>void Populate(IServiceProvider, TRoot)</c>.</param>
/// <param name="Build">Typed <c>TRoot Build(IServiceProvider)</c>, if the root can be constructed.</param>
/// <param name="PopulateTrampoline">Untyped <c>void (IServiceProvider, object)</c> wrapper around <paramref name="Populate"/>.</param>
/// <param name="BuildTrampoline">Untyped <c>object (IServiceProvider)</c> factory, if the root can be constructed.</param>
public sealed record NexerXamlEmittedMethods(
    IXamlMethod Populate,
    IXamlMethod? Build,
    IXamlMethod PopulateTrampoline,
    IXamlMethod? BuildTrampoline);

/// <summary>
/// The Nexer XAML compiler: parses XAML, runs the XamlX transformer pipeline with the Nexer configuration and emits
/// IL through either the SRE or the Cecil backend. Not thread-safe; diagnostics accumulate until <see cref="ClearDiagnostics"/>.
/// </summary>
public sealed class NexerXamlCompiler
{
    private readonly NexerXamlDiagnosticSink _sink = new();
    private Lazy<NexerBindingTypes?>? _bindingTypes;
    private int _documentCounter;

    /// <summary>Creates a compiler for <paramref name="typeSystem"/>.</summary>
    /// <param name="typeSystem">SRE or Cecil type system.</param>
    /// <param name="defaultAssembly">The assembly that contains the XAML (used for <c>clr-namespace:</c> without <c>assembly=</c>).</param>
    public NexerXamlCompiler(IXamlTypeSystem typeSystem, IXamlAssembly? defaultAssembly)
    {
        TypeSystem = typeSystem ?? throw new ArgumentNullException(nameof(typeSystem));
        Configuration = NexerXamlConfiguration.Create(typeSystem, defaultAssembly, _sink.CreateHandler());
    }

    /// <summary>The type system.</summary>
    public IXamlTypeSystem TypeSystem { get; }

    /// <summary>The XamlX configuration.</summary>
    public TransformerConfiguration Configuration { get; }

    /// <summary>Diagnostics reported since the last <see cref="ClearDiagnostics"/>.</summary>
    public IReadOnlyList<NexerXamlDiagnostic> Diagnostics => _sink.Diagnostics;

    /// <summary>Whether any error has been reported since the last <see cref="ClearDiagnostics"/>.</summary>
    public bool HasErrors => _sink.HasErrors;

    /// <summary>Clears the diagnostics.</summary>
    public void ClearDiagnostics() => _sink.Clear();

    /// <summary>Reports an error that is not tied to a position in a document.</summary>
    public void ReportError(string code, string message, string? filePath) =>
        _sink.Add(new NexerXamlDiagnostic(code, NexerXamlSeverity.Error, message, filePath, 0, 0));

    /// <summary>Creates the XamlX runtime context type (one per assembly is enough).</summary>
    public IXamlType CreateContextType(IXamlTypeBuilder<IXamlILEmitter> builder) => CreateILCompiler(null, null, out _).CreateContextType(builder);

    /// <summary>
    /// Parses and transforms <paramref name="xaml"/>. Returns <see langword="null"/> when errors were reported
    /// (see <see cref="Diagnostics"/>).
    /// </summary>
    /// <param name="xaml">The XAML text.</param>
    /// <param name="filePath">Path used in diagnostics.</param>
    /// <param name="rootTypeOverride">Forces the root type (runtime loading into an existing instance).</param>
    public NexerXamlDocument? Parse(string xaml, string? filePath, IXamlType? rootTypeOverride = null)
    {
        ArgumentNullException.ThrowIfNull(xaml);
        _sink.CurrentFile = filePath;
        var errorCount = ErrorCount();
        XamlDocument document;
        try
        {
            document = XDocumentXamlParser.Parse(xaml);
            document.Document = filePath;
        }
        catch (Exception e)
        {
            _sink.AddException(e, NexerXamlDiagnosticCodes.XmlError);
            return null;
        }

        var classTransformer = new XClassTransformer(rootTypeOverride);
        var compiler = CreateILCompiler(classTransformer, new XNameTransformer(), out var bindingState);
        try
        {
            compiler.Transform(document);
        }
        catch (Exception e)
        {
            _sink.AddException(e, NexerXamlDiagnosticCodes.General);
        }

        if (ErrorCount() != errorCount)
        {
            return null;
        }

        var rootType = ((XamlValueWithManipulationNode)document.Root).Type.GetClrType();
        return new NexerXamlDocument(document, compiler, rootType, classTransformer.ClassType, filePath, bindingState.AccessedAssemblies);
    }

    /// <summary>
    /// Emits the populate/build methods (and untyped trampolines) for <paramref name="document"/> into
    /// <paramref name="declaringType"/>. Returns <see langword="null"/> when emitting failed (see <see cref="Diagnostics"/>).
    /// </summary>
    /// <param name="document">A document returned by <see cref="Parse"/>.</param>
    /// <param name="contextType">The runtime context type from <see cref="CreateContextType"/>.</param>
    /// <param name="declaringType">The type that receives the methods (the code-behind class or a helper type).</param>
    /// <param name="methodPrefix">Prefix for the emitted member names (unique within <paramref name="declaringType"/>).</param>
    /// <param name="baseUri">Value exposed through <c>IUriContext.BaseUri</c>.</param>
    /// <param name="emitBuild">Whether to emit a build method/trampoline when possible.</param>
    /// <param name="classBuildUsesConstructor">
    /// For <c>x:Class</c> documents, build by calling the class constructor only (it is expected to call
    /// <c>InitializeComponent</c>, as compiled code-behind does) instead of constructing and populating.
    /// </param>
    public NexerXamlEmittedMethods? Emit(
        NexerXamlDocument document,
        IXamlType contextType,
        IXamlTypeBuilder<IXamlILEmitter> declaringType,
        string methodPrefix,
        string? baseUri,
        bool emitBuild = true,
        bool classBuildUsesConstructor = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        _sink.CurrentFile = document.FilePath;
        try
        {
            var compiler = document.Compiler;
            var visibility = XamlVisibility.Assembly;
            var populate = compiler.DefinePopulateMethod(declaringType, document.Document, methodPrefix + "Populate", visibility);
            var canBuildWithXamlX = emitBuild && document.CanBuild && (document.ClassType is null || !classBuildUsesConstructor);
            var build = canBuildWithXamlX ? compiler.DefineBuildMethod(declaringType, document.Document, methodPrefix + "Build", visibility) : null;
            var namespaceInfo = Configuration.TypeMappings.XmlNamespaceInfoProvider is null
                ? null
                : declaringType.DefineSubType(Configuration.WellKnownTypes.Object,
                    methodPrefix + "NamespaceInfo" + Interlocked.Increment(ref _documentCounter), XamlVisibility.Private);
            var fileSource = document.FilePath is null ? null : new FileSource(document.FilePath);
            compiler.Compile(document.Document, contextType, populate, declaringType, build, build is null ? null : declaringType,
                namespaceInfo, baseUri, fileSource);

            var populateTrampoline = EmitPopulateTrampoline(declaringType, methodPrefix, document.RootType, populate);
            IXamlMethod? buildTrampoline = null;
            if (build is not null)
            {
                buildTrampoline = EmitBuildTrampoline(declaringType, methodPrefix, build, null, document.RootType);
            }
            else if (emitBuild && classBuildUsesConstructor && document.ClassType is not null)
            {
                // A code-behind class populates itself from its constructor (InitializeComponent), so "build" is just "new T()".
                var ctor = document.ClassType.Constructors.FirstOrDefault(c => c.IsPublic && !c.IsStatic && c.Parameters.Count == 0)
                    ?? document.ClassType.Constructors.FirstOrDefault(c => c.IsPublic && !c.IsStatic && c.Parameters.Count == 1
                        && c.Parameters[0].Equals(Configuration.TypeMappings.ServiceProvider));
                if (ctor is not null)
                {
                    buildTrampoline = EmitBuildTrampoline(declaringType, methodPrefix, null, ctor, document.RootType);
                }
            }

            return new NexerXamlEmittedMethods(populate, build, populateTrampoline, buildTrampoline);
        }
        catch (Exception e)
        {
            _sink.AddException(e, NexerXamlDiagnosticCodes.EmitError);
            return null;
        }
    }

    /// <summary>
    /// Emits a call to <c>Nexer.Ui.Markup.XamlComponent.Register(anchor, logicalName, componentType, populate, build)</c>.
    /// Used by the build step to register compiled documents from a module initializer.
    /// </summary>
    public void EmitRegistration(IXamlILEmitter il, IXamlType assemblyAnchor, string logicalName, IXamlType? componentType, NexerXamlEmittedMethods methods)
    {
        ArgumentNullException.ThrowIfNull(il);
        ArgumentNullException.ThrowIfNull(methods);
        var wkt = Configuration.WellKnownTypes;
        var serviceProvider = Configuration.TypeMappings.ServiceProvider;
        var getTypeFromHandle = wkt.Type.GetMethod(m => m.Name == "GetTypeFromHandle" && m.IsStatic && m.Parameters.Count == 1);
        var action = TypeSystem.GetType("System.Action`2").MakeGenericType([serviceProvider, wkt.Object]);
        var func = TypeSystem.GetType("System.Func`2").MakeGenericType([serviceProvider, wkt.Object]);
        var register = TypeSystem.GetType(NexerXamlConfiguration.XamlComponentTypeName)
            .GetMethod(m => m.Name == "Register" && m.IsStatic && m.Parameters.Count == 5);

        il.Emit(OpCodes.Ldtoken, assemblyAnchor).EmitCall(getTypeFromHandle);
        il.Emit(OpCodes.Ldstr, logicalName);
        if (componentType is null)
        {
            il.Emit(OpCodes.Ldnull);
        }
        else
        {
            il.Emit(OpCodes.Ldtoken, componentType).EmitCall(getTypeFromHandle);
        }

        il.Emit(OpCodes.Ldnull).Emit(OpCodes.Ldftn, methods.PopulateTrampoline)
            .Emit(OpCodes.Newobj, action.Constructors.First(c => c.Parameters.Count == 2));
        if (methods.BuildTrampoline is null)
        {
            il.Emit(OpCodes.Ldnull);
        }
        else
        {
            il.Emit(OpCodes.Ldnull).Emit(OpCodes.Ldftn, methods.BuildTrampoline)
                .Emit(OpCodes.Newobj, func.Constructors.First(c => c.Parameters.Count == 2));
        }

        il.EmitCall(register);
    }

    private IXamlMethod EmitPopulateTrampoline(IXamlTypeBuilder<IXamlILEmitter> type, string prefix, IXamlType rootType, IXamlMethod populate)
    {
        var wkt = Configuration.WellKnownTypes;
        var method = type.DefineMethod(wkt.Void, [Configuration.TypeMappings.ServiceProvider, wkt.Object],
            prefix + "PopulateTrampoline", XamlVisibility.Assembly, true, false);
        method.Generator
            .Ldarg_0()
            .Ldarg(1)
            .Emit(OpCodes.Castclass, rootType)
            .EmitCall(populate)
            .Ret();
        return method;
    }

    private IXamlMethod EmitBuildTrampoline(IXamlTypeBuilder<IXamlILEmitter> type, string prefix, IXamlMethod? build, IXamlConstructor? ctor, IXamlType rootType)
    {
        var wkt = Configuration.WellKnownTypes;
        var method = type.DefineMethod(wkt.Object, [Configuration.TypeMappings.ServiceProvider],
            prefix + "BuildTrampoline", XamlVisibility.Assembly, true, false);
        var il = method.Generator;
        if (build is not null)
        {
            il.Ldarg_0().EmitCall(build);
        }
        else
        {
            if (ctor!.Parameters.Count == 1)
            {
                il.Ldarg_0();
            }

            il.Emit(OpCodes.Newobj, ctor);
        }

        if (rootType.IsValueType)
        {
            il.Emit(OpCodes.Box, rootType);
        }

        il.Ret();
        return method;
    }

    private XamlILCompiler CreateILCompiler(XClassTransformer? classTransformer, XNameTransformer? nameTransformer, out NexerBindingState bindingState)
    {
        var compiler = new XamlILCompiler(Configuration, new XamlLanguageEmitMappings<IXamlILEmitter, XamlILNodeEmitResult>(), true)
        {
            EnableIlVerification = true,
        };
        compiler.Transformers.Insert(0, new IgnoredDirectivesTransformer());
        if (classTransformer is not null)
        {
            compiler.Transformers.Insert(0, classTransformer);
        }

        if (nameTransformer is not null)
        {
            var index = compiler.Transformers.FindIndex(t => t is PropertyReferenceResolver);
            compiler.Transformers.Insert(index, nameTransformer);
        }

        _bindingTypes ??= new Lazy<NexerBindingTypes?>(() => NexerBindingTypes.TryCreate(TypeSystem));
        bindingState = new NexerBindingState(_bindingTypes.Value);
        compiler.Transformers.Insert(compiler.Transformers.FindIndex(t => t is MarkupExtensionTransformer), new BindingSyntaxTransformer(bindingState));
        compiler.Transformers.Insert(compiler.Transformers.FindIndex(t => t is ConvertPropertyValuesToAssignmentsTransformer),
            new BindingAssignmentTransformer(bindingState));
        return compiler;
    }

    private int ErrorCount() => _sink.Diagnostics.Count(d => d.Severity == NexerXamlSeverity.Error);

    private sealed class FileSource(string path) : IFileSource
    {
        private byte[]? _contents;

        public string FilePath { get; } = path;

        public byte[] FileContents => _contents ??= File.Exists(FilePath) ? File.ReadAllBytes(FilePath) : Encoding.UTF8.GetBytes("");
    }
}
