using Mono.Cecil;
using Mono.Cecil.Cil;
using XamlX.IL;
using XamlX.TypeSystem;

namespace Nexer.Xaml.Compiler;

/// <summary>A XAML file to compile into an assembly.</summary>
/// <param name="FilePath">Full path of the file (used for reading and in diagnostics).</param>
/// <param name="LogicalName">Project-relative name with forward slashes, e.g. <c>Views/MainWindow.xaml</c>.</param>
public sealed record NexerXamlItem(string FilePath, string LogicalName);

/// <summary>Result of <see cref="NexerXamlAssemblyCompiler.Compile"/>.</summary>
/// <param name="Success">Whether the assembly was written (no errors).</param>
/// <param name="AlreadyCompiled">The input had already been processed; it was copied unchanged.</param>
/// <param name="Diagnostics">Errors and warnings.</param>
public sealed record NexerXamlAssemblyResult(bool Success, bool AlreadyCompiled, IReadOnlyList<NexerXamlDiagnostic> Diagnostics);

/// <summary>
/// Build-time XAML compilation: rewrites a compiled assembly with Mono.Cecil, emitting the populate/build methods of
/// every XAML document and a module initializer that registers them with <c>Nexer.Ui.Markup.XamlComponent</c>.
/// </summary>
/// <remarks>
/// Documents with <c>x:Class</c> are emitted into their code-behind type (so private event handlers work); others go
/// into generated helper types in the <c>CompiledNexerXaml</c> namespace.
/// </remarks>
public static class NexerXamlAssemblyCompiler
{
    /// <summary>Namespace of the generated helper types.</summary>
    public const string GeneratedNamespace = "CompiledNexerXaml";

    /// <summary>Name of the generated registration type; its presence marks an already-processed assembly.</summary>
    public const string RegistrationTypeName = "XamlRegistration";

    private const string MemberPrefix = "!NexerXaml.";

    /// <summary>Compiles <paramref name="items"/> into <paramref name="assemblyPath"/> and writes the result to <paramref name="outputPath"/>.</summary>
    /// <param name="assemblyPath">The compiled (intermediate) assembly.</param>
    /// <param name="referencePaths">Paths of all referenced assemblies.</param>
    /// <param name="items">The XAML files.</param>
    /// <param name="outputPath">Where to write the rewritten assembly (its PDB is written next to it when the input has symbols).</param>
    public static NexerXamlAssemblyResult Compile(string assemblyPath, IEnumerable<string> referencePaths, IReadOnlyList<NexerXamlItem> items, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(assemblyPath);
        ArgumentNullException.ThrowIfNull(referencePaths);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(outputPath);
        assemblyPath = Path.GetFullPath(assemblyPath);
        outputPath = Path.GetFullPath(outputPath);
        var refs = referencePaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(Path.GetFullPath)
            .Where(p => File.Exists(p) && !string.Equals(p, assemblyPath, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var typeSystem = new CecilTypeSystem(refs, assemblyPath);
        var assembly = typeSystem.TargetAssemblyDefinition!;
        var module = assembly.MainModule;
        if (module.GetType(GeneratedNamespace, RegistrationTypeName) is not null)
        {
            CopyUnchanged(assemblyPath, outputPath);
            return new NexerXamlAssemblyResult(true, true, []);
        }

        if (!NexerXamlConfiguration.HasMarkupContracts(typeSystem))
        {
            var diagnostic = new NexerXamlDiagnostic(NexerXamlDiagnosticCodes.General, NexerXamlSeverity.Error,
                "Nexer.Ui is not referenced, so XAML cannot be compiled (the Nexer.Ui.Markup types were not found).",
                items.Count > 0 ? items[0].FilePath : assemblyPath, 0, 0);
            return new NexerXamlAssemblyResult(false, false, [diagnostic]);
        }

        var compiler = new NexerXamlCompiler(typeSystem, typeSystem.TargetAssembly);
        var documents = new List<(NexerXamlItem Item, NexerXamlDocument Document)>();
        var classes = new Dictionary<string, NexerXamlItem>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            string text;
            try
            {
                text = File.ReadAllText(item.FilePath);
            }
            catch (IOException e)
            {
                compiler.ReportError(NexerXamlDiagnosticCodes.General, $"Unable to read XAML file: {e.Message}", item.FilePath);
                continue;
            }

            var document = compiler.Parse(text, item.FilePath);
            if (document is null)
            {
                continue;
            }

            if (document.ClassType is { } classType)
            {
                if (!Equals(classType.Assembly, typeSystem.TargetAssembly))
                {
                    compiler.ReportError(NexerXamlDiagnosticCodes.ClassNotFound,
                        $"x:Class '{classType.FullName}' must be declared in assembly '{assembly.Name.Name}'.", item.FilePath);
                    continue;
                }

                if (!classes.TryAdd(classType.FullName, item))
                {
                    compiler.ReportError(NexerXamlDiagnosticCodes.ClassNotFound,
                        $"x:Class '{classType.FullName}' is also used by '{classes[classType.FullName].LogicalName}'.", item.FilePath);
                    continue;
                }
            }

            documents.Add((item, document));
        }

        if (compiler.HasErrors)
        {
            return new NexerXamlAssemblyResult(false, false, compiler.Diagnostics);
        }

        var objectType = module.TypeSystem.Object;
        TypeDefinition AddType(string name, TypeAttributes attributes)
        {
            var type = new TypeDefinition(GeneratedNamespace, name, attributes, objectType);
            module.Types.Add(type);
            return type;
        }

        var contextBuilder = typeSystem.CreateTypeBuilder(AddType("XamlContext", TypeAttributes.Class | TypeAttributes.NotPublic));
        var contextType = compiler.CreateContextType(contextBuilder);
        var staticClass = TypeAttributes.Class | TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit;
        var registrationDefinition = AddType(RegistrationTypeName, staticClass);
        var registrationBuilder = typeSystem.CreateTypeBuilder(registrationDefinition);
        var register = registrationBuilder.DefineMethod(compiler.Configuration.WellKnownTypes.Void, [], "Register", XamlVisibility.Assembly, true, false);
        var helperIndex = 0;
        var assemblyName = assembly.Name.Name;
        foreach (var (item, document) in documents)
        {
            IXamlTypeBuilder<IXamlILEmitter> declaringType;
            if (document.ClassType is { } classType)
            {
                declaringType = typeSystem.CreateTypeBuilder(typeSystem.GetTypeReference(classType).Resolve(), compilerGeneratedType: false);
            }
            else
            {
                declaringType = typeSystem.CreateTypeBuilder(AddType("Xaml_" + helperIndex++, staticClass));
            }

            var baseUri = $"nexer://{assemblyName}/{item.LogicalName.Replace('\\', '/').TrimStart('/')}";
            var methods = compiler.Emit(document, contextType, declaringType, MemberPrefix, baseUri);
            if (methods is not null)
            {
                compiler.EmitRegistration(register.Generator, registrationBuilder, item.LogicalName, document.ClassType, methods);
            }
        }

        if (compiler.HasErrors)
        {
            return new NexerXamlAssemblyResult(false, false, compiler.Diagnostics);
        }

        register.Generator.Emit(System.Reflection.Emit.OpCodes.Ret);
        CallFromModuleInitializer(module, registrationDefinition.Methods.Single(m => m.Name == "Register"));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var writerParameters = new WriterParameters();
        if (module.HasSymbols)
        {
            writerParameters.WriteSymbols = true;
            writerParameters.SymbolWriterProvider = new PortablePdbWriterProvider();
        }

        assembly.Write(outputPath, writerParameters);
        return new NexerXamlAssemblyResult(true, false, compiler.Diagnostics);
    }

    private static void CallFromModuleInitializer(ModuleDefinition module, MethodDefinition method)
    {
        var moduleType = module.GetType("<Module>");
        var cctor = moduleType.Methods.FirstOrDefault(m => m.IsConstructor && m.IsStatic);
        if (cctor is null)
        {
            cctor = new MethodDefinition(".cctor",
                MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            cctor.Body.GetILProcessor().Emit(OpCodes.Ret);
            moduleType.Methods.Add(cctor);
        }

        var il = cctor.Body.GetILProcessor();
        il.InsertBefore(cctor.Body.Instructions[0], il.Create(OpCodes.Call, method));
    }

    private static void CopyUnchanged(string assemblyPath, string outputPath)
    {
        if (string.Equals(assemblyPath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.Copy(assemblyPath, outputPath, true);
        var pdb = Path.ChangeExtension(assemblyPath, ".pdb");
        if (File.Exists(pdb))
        {
            File.Copy(pdb, Path.ChangeExtension(outputPath, ".pdb"), true);
        }
    }
}
