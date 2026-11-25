using XamlX;
using XamlX.Transform;
using XamlX.TypeSystem;

namespace Nexer.Xaml.Compiler;

/// <summary>
/// Builds the XamlX configuration for Nexer XAML. Every language type is looked up by full name in the supplied
/// type system, so the same code works with the runtime (System.Reflection.Emit) and build-time (Cecil) type systems,
/// and nothing depends on concrete control types.
/// </summary>
public static class NexerXamlConfiguration
{
    /// <summary>The namespace containing the markup contracts (attributes and service interfaces).</summary>
    public const string MarkupNamespace = "Nexer.Ui.Markup";

    /// <summary>Full name of the runtime name scope type used by <c>x:Name</c>.</summary>
    public const string NameScopeTypeName = MarkupNamespace + ".NameScope";

    /// <summary>Full name of the compiled-XAML registry type.</summary>
    public const string XamlComponentTypeName = MarkupNamespace + ".XamlComponent";

    /// <summary>Returns whether <paramref name="typeSystem"/> can see the Nexer markup contracts (i.e. Nexer.Ui is referenced).</summary>
    public static bool HasMarkupContracts(IXamlTypeSystem typeSystem) =>
        typeSystem.FindType(MarkupNamespace + ".XmlnsDefinitionAttribute") is not null;

    /// <summary>Creates the language type mappings (attributes and service-provider interfaces).</summary>
    /// <exception cref="XamlTypeSystemException">Nexer.Ui (the markup contracts) is not visible to the type system.</exception>
    public static XamlLanguageTypeMappings CreateTypeMappings(IXamlTypeSystem typeSystem)
    {
        ArgumentNullException.ThrowIfNull(typeSystem);
        if (!HasMarkupContracts(typeSystem))
        {
            throw new XamlTypeSystemException(
                $"The Nexer markup contracts ({MarkupNamespace}.*) were not found. Reference Nexer.Ui from the project containing XAML.");
        }

        IXamlType Markup(string name) => typeSystem.GetType(MarkupNamespace + "." + name);

        var mappings = new XamlLanguageTypeMappings(typeSystem)
        {
            XmlnsAttributes = { Markup("XmlnsDefinitionAttribute") },
            ContentAttributes = { Markup("ContentAttribute") },
            DeferredContentPropertyAttributes = { Markup("DeferredContentAttribute") },
            UsableDuringInitializationAttributes = { Markup("UsableDuringInitializationAttribute") },
            WhitespaceSignificantCollectionAttributes = { Markup("WhitespaceSignificantCollectionAttribute") },
            TrimSurroundingWhitespaceAttributes = { Markup("TrimSurroundingWhitespaceAttribute") },
            RootObjectProvider = Markup("IRootObjectProvider"),
            ProvideValueTarget = Markup("IProvideValueTarget"),
            UriContextProvider = Markup("IUriContext"),
            ParentStackProvider = Markup("IParentStackProvider"),
            XmlNamespaceInfoProvider = Markup("IXmlNamespaceInfoProvider"),
            IAddChild = Markup("IAddChild"),
            IAddChildOfT = Markup("IAddChild`1"),
        };
        return mappings;
    }

    /// <summary>Creates the transformer configuration.</summary>
    /// <param name="typeSystem">The type system (SRE or Cecil).</param>
    /// <param name="defaultAssembly">The assembly whose types are resolved by <c>clr-namespace:</c> without <c>assembly=</c>.</param>
    /// <param name="diagnosticsHandler">Receives diagnostics.</param>
    public static TransformerConfiguration Create(
        IXamlTypeSystem typeSystem,
        IXamlAssembly? defaultAssembly,
        XamlDiagnosticsHandler diagnosticsHandler)
    {
        ArgumentNullException.ThrowIfNull(diagnosticsHandler);
        var mappings = CreateTypeMappings(typeSystem);
        return new TransformerConfiguration(
            typeSystem,
            defaultAssembly,
            mappings,
            customValueConverter: NexerValueConverters.TryConvert,
            diagnosticsHandler: diagnosticsHandler);
    }
}
