using System.Globalization;
using System.Reflection.Emit;
using XamlX;
using XamlX.Ast;
using XamlX.Emit;
using XamlX.IL;
using XamlX.Transform;
using XamlX.Transform.Transformers;
using XamlX.TypeSystem;

namespace Nexer.Xaml.Compiler.Transformers;

/// <summary>The Nexer.Ui.Data types used by compiled bindings, looked up by name in the active type system.</summary>
internal sealed class NexerBindingTypes
{
    public const string DataNamespace = "Nexer.Ui.Data";

    private NexerBindingTypes(IXamlTypeSystem ts)
    {
        IXamlType Data(string name) => ts.GetType(DataNamespace + "." + name);
        BindingExtension = Data("BindingExtension");
        RelativeSource = Data("RelativeSource");
        CompiledBinding = Data("CompiledBinding");
        CompiledBindingPath = Data("CompiledBindingPath");
        PathNode = Data("PathNode");
        IBinding = Data("IBinding");
        BindsTwoWayByDefault = Data("BindsTwoWayByDefaultAttribute");
        BindingOperations = Data("BindingOperations");
        Widget = ts.GetType("Nexer.Ui.Widget");
        Object = ts.GetType("System.Object");
        String = ts.GetType("System.String");
        Int32 = ts.GetType("System.Int32");
        EnumerableOfT = ts.GetType("System.Collections.Generic.IEnumerable`1");
        ListOfT = ts.GetType("System.Collections.Generic.IList`1");
        Getter = ts.GetType("System.Func`2").MakeGenericType([Object, Object]);
        Setter = ts.GetType("System.Action`2").MakeGenericType([Object, Object]);
        GetterConstructor = Getter.Constructors.First(c => c.Parameters.Count == 2);
        SetterConstructor = Setter.Constructors.First(c => c.Parameters.Count == 2);
        PathConstructor = CompiledBindingPath.Constructors.First(c => c.Parameters.Count == 5);
        PathNodeConstructor = PathNode.Constructors.First(c => c.Parameters.Count == 5);
        Bind = BindingOperations.GetMethod(m => m.Name == "Bind" && m.IsStatic && m.Parameters.Count == 6);
    }

    public IXamlType BindingExtension { get; }

    public IXamlType RelativeSource { get; }

    public IXamlType CompiledBinding { get; }

    public IXamlType CompiledBindingPath { get; }

    public IXamlType PathNode { get; }

    public IXamlType IBinding { get; }

    public IXamlType BindsTwoWayByDefault { get; }

    public IXamlType BindingOperations { get; }

    public IXamlType Widget { get; }

    public IXamlType Object { get; }

    public IXamlType String { get; }

    public IXamlType Int32 { get; }

    /// <summary><c>IEnumerable&lt;T&gt;</c> (open).</summary>
    public IXamlType EnumerableOfT { get; }

    /// <summary><c>IList&lt;T&gt;</c> (open), used for array indexers.</summary>
    public IXamlType ListOfT { get; }

    /// <summary><c>Func&lt;object, object?&gt;</c>.</summary>
    public IXamlType Getter { get; }

    /// <summary><c>Action&lt;object, object?&gt;</c>.</summary>
    public IXamlType Setter { get; }

    public IXamlConstructor GetterConstructor { get; }

    public IXamlConstructor SetterConstructor { get; }

    public IXamlConstructor PathConstructor { get; }

    public IXamlConstructor PathNodeConstructor { get; }

    /// <summary><c>BindingOperations.Bind(Widget, string, Func, Action, Type, IBinding)</c>.</summary>
    public IXamlMethod Bind { get; }

    /// <summary>Returns the binding types, or <see langword="null"/> when Nexer.Ui.Data is not visible (bindings disabled).</summary>
    public static NexerBindingTypes? TryCreate(IXamlTypeSystem typeSystem) =>
        typeSystem.FindType(DataNamespace + ".BindingExtension") is null ? null : new NexerBindingTypes(typeSystem);
}

/// <summary>Values of <c>Nexer.Ui.Data.BindingSourceKind</c> (emitted as integers).</summary>
internal enum NexerBindingSourceKind
{
    DataContext = 0,
    Self = 1,
    ElementName = 2,
    FindAncestor = 3,
}

/// <summary>What the syntax pass extracted from a <c>{Binding}</c>; completed by <see cref="BindingAssignmentTransformer"/>.</summary>
internal sealed class NexerBindingSpec(IXamlLineInfo lineInfo, NexerBindingPathNode pathNode)
{
    public IXamlLineInfo LineInfo { get; } = lineInfo;

    public NexerBindingPathNode PathNode { get; } = pathNode;

    public string Path { get; set; } = "";

    public NexerBindingSourceKind Source { get; set; }

    public string? ElementName { get; set; }

    public IXamlType? AncestorType { get; set; }

    public int AncestorLevel { get; set; } = 1;

    public IXamlType? DataType { get; set; }

    /// <summary>The <c>Mode</c> text, when it was given as text.</summary>
    public string? ModeText { get; set; }

    public bool HasMode { get; set; }

    public bool HasConverter { get; set; }

    /// <summary>The binding was assigned to a property (so it is not an orphan).</summary>
    public bool Handled { get; set; }

    /// <summary>Path resolution failed (an error was already reported).</summary>
    public bool Failed { get; set; }

    /// <summary>The compile-time type of the value the path produces (the source type for an empty path).</summary>
    public IXamlType? LeafType { get; set; }
}

/// <summary>Per-document state shared by the binding transformers.</summary>
internal sealed class NexerBindingState(NexerBindingTypes? types)
{
    public NexerBindingTypes? Types { get; } = types;

    /// <summary>x:DataType per element (by reference).</summary>
    public Dictionary<XamlAstObjectNode, IXamlType> DataTypes { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Element types by x:Name / Name, for <c>ElementName</c> bindings (names are unique per document).</summary>
    public Dictionary<string, IXamlType> Names { get; } = new(StringComparer.Ordinal);

    /// <summary>The converted binding object nodes (retyped to <c>CompiledBinding</c>).</summary>
    public Dictionary<XamlAstObjectNode, NexerBindingSpec> Bindings { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Names of the assemblies whose types are accessed by binding accessors (the SRE loader waives access checks for them).</summary>
    public HashSet<string> AccessedAssemblies { get; } = new(StringComparer.Ordinal);

    public void TrackAccess(IXamlType type)
    {
        if (type.Assembly?.Name is { } name)
        {
            AccessedAssemblies.Add(name);
        }

        foreach (var argument in type.GenericArguments)
        {
            TrackAccess(argument);
        }

        if (type.ArrayElementType is { } element)
        {
            TrackAccess(element);
        }
    }
}

/// <summary>
/// Binding pass 1 (syntax). Records <c>x:DataType</c> (removing the directive) and element names, and rewrites every
/// <c>BindingExtension</c> object node into a <c>CompiledBinding</c> object node: the path/source parts
/// (<c>Path</c>, <c>ElementName</c>, <c>RelativeSource</c>, <c>DataType</c>) are removed and replaced by a
/// <see cref="NexerBindingPathNode"/> assigned to <c>CompiledBinding.Path</c>; the remaining properties (Mode,
/// Converter, ...) are left to XamlX. Must run after <see cref="TypeReferenceResolver"/> and before
/// <see cref="MarkupExtensionTransformer"/> (which rejects markup-extension types without <c>ProvideValue</c>).
/// </summary>
internal sealed class BindingSyntaxTransformer(NexerBindingState state) : IXamlAstTransformer
{
    public IXamlAstNode Transform(AstTransformationContext context, IXamlAstNode node)
    {
        if (state.Types is not { } types || node is not XamlAstObjectNode obj || obj.Type is not XamlAstClrTypeReference clrType)
        {
            return node;
        }

        var dataType = obj.Children.OfType<XamlAstXmlDirective>()
            .FirstOrDefault(d => d.Namespace == XamlNamespaces.Xaml2006 && d.Name == "DataType");
        if (dataType is not null)
        {
            obj.Children.Remove(dataType);
            if (dataType.Values.Count == 1 && ResolveTypeValue(context, dataType.Values[0]) is { } resolved)
            {
                state.DataTypes[obj] = resolved;
            }
            else
            {
                context.ReportError(new NexerXamlException(NexerXamlDiagnosticCodes.InvalidDirective,
                    "x:DataType must be a type name or {x:Type}.", dataType), node);
            }
        }

        RecordName(obj, clrType.Type);
        return clrType.Type.Equals(types.BindingExtension) ? ConvertBinding(context, types, obj) : node;
    }

    private void RecordName(XamlAstObjectNode obj, IXamlType type)
    {
        foreach (var child in obj.Children)
        {
            var name = child switch
            {
                XamlAstXmlDirective { Namespace: XamlNamespaces.Xaml2006, Name: "Name", Values: [XamlAstTextNode text] } => text.Text,
                XamlAstXamlPropertyValueNode { Property: XamlAstNamePropertyReference { Name: "Name" }, Values: [XamlAstTextNode text] } => text.Text,
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(name))
            {
                state.Names[name.Trim()] = type;
            }
        }
    }

    private IXamlAstNode ConvertBinding(AstTransformationContext context, NexerBindingTypes types, XamlAstObjectNode obj)
    {
        var compiledType = new XamlAstClrTypeReference(obj, types.CompiledBinding, false);
        var pathNode = new NexerBindingPathNode(obj, types);
        var spec = new NexerBindingSpec(obj, pathNode);
        var hasRelativeSource = false;
        if (obj.Arguments.Count > 1 || (obj.Arguments.Count == 1 && obj.Arguments[0] is not XamlAstTextNode))
        {
            return Error(context, NexerXamlDiagnosticCodes.InvalidBindingPath, "A binding takes at most one positional argument (the path).", obj);
        }

        if (obj.Arguments is [XamlAstTextNode argument])
        {
            spec.Path = argument.Text.Trim();
        }

        obj.Arguments.Clear();
        for (var i = 0; i < obj.Children.Count; i++)
        {
            if (obj.Children[i] is not XamlAstXamlPropertyValueNode { Property: XamlAstNamePropertyReference reference } value)
            {
                continue;
            }

            var remove = true;
            switch (reference.Name)
            {
                case "Path":
                    spec.Path = GetText(context, value, "Path")?.Trim() ?? "";
                    break;
                case "ElementName":
                    spec.ElementName = GetText(context, value, "ElementName")?.Trim();
                    spec.Source = NexerBindingSourceKind.ElementName;
                    break;
                case "DataType":
                    spec.DataType = value.Values.Count == 1 ? ResolveTypeValue(context, value.Values[0]) : null;
                    if (spec.DataType is null)
                    {
                        Error(context, NexerXamlDiagnosticCodes.InvalidBindingPath, "DataType must be a type name or {x:Type}.", value);
                    }

                    break;
                case "RelativeSource":
                    hasRelativeSource = true;
                    ReadRelativeSource(context, types, value, spec);
                    break;
                default:
                    remove = false;
                    if (reference.Name == "Mode")
                    {
                        spec.HasMode = true;
                        spec.ModeText = value.Values is [XamlAstTextNode mode] ? mode.Text.Trim() : null;
                    }
                    else if (reference.Name == "Converter")
                    {
                        spec.HasConverter = true;
                    }

                    if (reference.DeclaringType is XamlAstClrTypeReference declaring && declaring.Type.Equals(types.BindingExtension))
                    {
                        reference.DeclaringType = compiledType;
                        reference.TargetType = compiledType;
                    }

                    break;
            }

            if (remove)
            {
                obj.Children.RemoveAt(i);
                i--;
            }
        }

        if (hasRelativeSource && spec.ElementName is not null)
        {
            return Error(context, NexerXamlDiagnosticCodes.InvalidRelativeSource, "ElementName and RelativeSource cannot both be set.", obj);
        }

        obj.Type = compiledType;
        obj.Children.Add(new XamlAstXamlPropertyValueNode(obj,
            new XamlAstNamePropertyReference(obj, compiledType, "Path", compiledType), pathNode, isAttributeSyntax: true));
        state.Bindings[obj] = spec;
        return obj;
    }

    private static void ReadRelativeSource(AstTransformationContext context, NexerBindingTypes types, XamlAstXamlPropertyValueNode value, NexerBindingSpec spec)
    {
        if (value.Values is not [XamlAstObjectNode source] || source.Type.GetClrType() is not { } sourceType || !sourceType.Equals(types.RelativeSource))
        {
            Error(context, NexerXamlDiagnosticCodes.InvalidRelativeSource, "RelativeSource must be {RelativeSource Self} or {RelativeSource FindAncestor, AncestorType=...}.", value);
            return;
        }

        string? mode = source.Arguments is [XamlAstTextNode modeArgument] ? modeArgument.Text.Trim() : null;
        foreach (var child in source.Children.OfType<XamlAstXamlPropertyValueNode>())
        {
            var name = (child.Property as XamlAstNamePropertyReference)?.Name;
            switch (name)
            {
                case "Mode":
                    mode = GetText(context, child, "Mode")?.Trim();
                    break;
                case "AncestorType":
                    spec.AncestorType = child.Values.Count == 1 ? ResolveTypeValue(context, child.Values[0]) : null;
                    if (spec.AncestorType is null)
                    {
                        Error(context, NexerXamlDiagnosticCodes.InvalidRelativeSource, "AncestorType must be a type name or {x:Type}.", child);
                    }
                    else if (!types.Widget.IsAssignableFrom(spec.AncestorType))
                    {
                        Error(context, NexerXamlDiagnosticCodes.InvalidRelativeSource,
                            $"AncestorType '{spec.AncestorType.GetFqn()}' is not a widget type.", child);
                        spec.AncestorType = null;
                    }

                    break;
                case "AncestorLevel":
                    if (GetText(context, child, "AncestorLevel") is { } levelText
                        && int.TryParse(levelText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) && level >= 1)
                    {
                        spec.AncestorLevel = level;
                    }
                    else
                    {
                        Error(context, NexerXamlDiagnosticCodes.InvalidRelativeSource, "AncestorLevel must be a positive integer.", child);
                    }

                    break;
                default:
                    Error(context, NexerXamlDiagnosticCodes.InvalidRelativeSource, $"Unknown RelativeSource property '{name}'.", child);
                    break;
            }
        }

        mode ??= spec.AncestorType is null ? null : "FindAncestor";
        switch (mode)
        {
            case "Self":
                spec.Source = NexerBindingSourceKind.Self;
                break;
            case "FindAncestor" when spec.AncestorType is not null:
                spec.Source = NexerBindingSourceKind.FindAncestor;
                break;
            case "FindAncestor":
                Error(context, NexerXamlDiagnosticCodes.InvalidRelativeSource, "RelativeSource FindAncestor requires a widget AncestorType.", value);
                break;
            default:
                Error(context, NexerXamlDiagnosticCodes.InvalidRelativeSource,
                    $"Unsupported RelativeSource mode '{mode}'. Supported modes are Self and FindAncestor.", value);
                break;
        }
    }

    private static string? GetText(AstTransformationContext context, XamlAstXamlPropertyValueNode value, string name)
    {
        if (value.Values is [XamlAstTextNode text])
        {
            return text.Text;
        }

        Error(context, NexerXamlDiagnosticCodes.InvalidBindingPath, $"{name} must be a string.", value);
        return null;
    }

    internal static IXamlType? ResolveTypeValue(AstTransformationContext context, IXamlAstValueNode value) => value switch
    {
        XamlAstTextNode text when !string.IsNullOrWhiteSpace(text.Text) =>
            TypeReferenceResolver.ResolveType(context, text.Text.Trim(), false, text, true).Type,
        XamlTypeExtensionNode typeExtension => typeExtension.Value.GetClrType(),
        _ => null,
    };

    internal static IXamlAstNode Error(AstTransformationContext context, string code, string message, IXamlAstNode node) =>
        context.ReportError(new NexerXamlException(code, message, node), node);
}

/// <summary>
/// Binding pass 2 (semantics). For a property value that is a converted binding: resolves the path against the source
/// type (x:DataType for DataContext bindings), applies <c>[BindsTwoWayByDefault]</c>, checks the types, and replaces the
/// value with a <see cref="XamlPropertyAssignmentNode"/> whose only setter is <see cref="NexerBindingSetter"/> (a call to
/// <c>BindingOperations.Bind</c>). Bindings assigned to <c>IBinding</c>-typed properties (e.g. <c>DisplayMemberBinding</c>)
/// are left as ordinary values, resolved against the item type (their <c>DataType</c>, or the element type of a sibling
/// <c>ItemsSource</c> binding).
/// Must run after <see cref="PropertyReferenceResolver"/> and before <see cref="ConvertPropertyValuesToAssignmentsTransformer"/>.
/// </summary>
internal sealed class BindingAssignmentTransformer(NexerBindingState state) : IXamlAstTransformer
{
    private const string ItemsSourceProperty = "ItemsSource";

    public IXamlAstNode Transform(AstTransformationContext context, IXamlAstNode node)
    {
        if (state.Types is not { } types)
        {
            return node;
        }

        if (node is XamlAstObjectNode obj && state.Bindings.TryGetValue(obj, out var orphan) && !orphan.Handled)
        {
            orphan.Handled = true;
            return BindingSyntaxTransformer.Error(context, NexerXamlDiagnosticCodes.InvalidBindingTarget,
                "A {Binding} can only be assigned to a property.", node);
        }

        if (node is not XamlAstXamlPropertyValueNode value
            || !value.Values.Any(v => v is XamlAstObjectNode o && state.Bindings.ContainsKey(o)))
        {
            return node;
        }

        if (value.Values is not [XamlAstObjectNode bindingNode])
        {
            foreach (var other in value.Values.OfType<XamlAstObjectNode>())
            {
                if (state.Bindings.TryGetValue(other, out var otherSpec))
                {
                    otherSpec.Handled = true;
                }
            }

            return BindingSyntaxTransformer.Error(context, NexerXamlDiagnosticCodes.InvalidBindingTarget,
                "A property set by a {Binding} cannot have other values.", node);
        }

        var spec = state.Bindings[bindingNode];
        spec.Handled = true;
        var property = value.Property.GetClrProperty();
        var targetObject = context.ParentNodes().OfType<XamlAstObjectNode>().FirstOrDefault();
        var targetType = targetObject?.Type.GetClrType();
        var clrProperty = targetType?.GetAllProperties().FirstOrDefault(p =>
            p.Name == property.Name && p.Setter is { IsPublic: true, IsStatic: false } && p.Setter.Parameters.Count == 1);
        if (targetObject is not null && clrProperty is not null && IsItemBindingProperty(types, clrProperty.PropertyType))
        {
            ResolveItemBinding(context, types, spec, property.Name, targetObject, targetType!, value);
            return node;
        }

        if (targetType is null || clrProperty is null || !types.Widget.IsAssignableFrom(targetType))
        {
            return BindingSyntaxTransformer.Error(context, NexerXamlDiagnosticCodes.InvalidBindingTarget,
                $"'{property.Name}' cannot be bound: a binding target must be a public settable instance property of a widget.", node);
        }

        if (!Resolve(context, types, spec, targetType, value))
        {
            return node;
        }

        var twoWay = spec.ModeText is "TwoWay" or "OneWayToSource";
        if ((!spec.HasMode || spec.ModeText == "Default") && property.CustomAttributes.Any(a => a.Type.Equals(types.BindsTwoWayByDefault)))
        {
            twoWay = true;
            SetModeTwoWay(context, types, bindingNode);
        }

        if (twoWay && !spec.PathNode.IsWritable)
        {
            return BindingSyntaxTransformer.Error(context, NexerXamlDiagnosticCodes.TwoWayToReadOnly,
                $"The binding '{spec.Path}' on '{property.Name}' is two-way but the source path is not writable. Use Mode=OneWay.", node);
        }

        if (twoWay && clrProperty.Getter is not { IsPublic: true, IsStatic: false })
        {
            return BindingSyntaxTransformer.Error(context, NexerXamlDiagnosticCodes.InvalidBindingTarget,
                $"The binding '{spec.Path}' on '{property.Name}' is two-way but '{property.Name}' has no public getter. Use Mode=OneWay.", node);
        }

        CheckTypes(context, types, spec, clrProperty.PropertyType, property.Name, twoWay, value);
        state.TrackAccess(clrProperty.Setter!.DeclaringType);
        state.TrackAccess(clrProperty.PropertyType);
        return new XamlPropertyAssignmentNode(value, property, [new NexerBindingSetter(types, targetType, clrProperty)], [bindingNode]);
    }

    /// <summary>Properties typed to hold a binding (IBinding, CompiledBinding) receive the binding itself, evaluated per item.</summary>
    private static bool IsItemBindingProperty(NexerBindingTypes types, IXamlType type) =>
        !type.Equals(types.Object) && type.IsAssignableFrom(types.CompiledBinding);

    /// <summary>A binding used as a value (DisplayMemberBinding, ChildrenBinding): resolved against the item type.</summary>
    private void ResolveItemBinding(
        AstTransformationContext context,
        NexerBindingTypes types,
        NexerBindingSpec spec,
        string propertyName,
        XamlAstObjectNode targetObject,
        IXamlType targetType,
        IXamlAstNode node)
    {
        if (spec.Source != NexerBindingSourceKind.DataContext)
        {
            BindingSyntaxTransformer.Error(context, NexerXamlDiagnosticCodes.InvalidBindingTarget,
                $"The binding assigned to '{propertyName}' is evaluated per item: ElementName and RelativeSource are not supported.", node);
            return;
        }

        var itemType = spec.DataType;
        if (itemType is null && FindItemsSourceBinding(targetObject) is ({ } itemsSpec, { } itemsNode))
        {
            if (Resolve(context, types, itemsSpec, targetType, itemsNode))
            {
                itemType = GetElementType(types, itemsSpec.LeafType!);
            }
            else
            {
                spec.Failed = true;
                return;
            }
        }

        if (itemType is null)
        {
            BindingSyntaxTransformer.Error(context, NexerXamlDiagnosticCodes.MissingDataType,
                $"The binding assigned to '{propertyName}' is evaluated per item, and the item type cannot be inferred (ItemsSource is not a binding to an IEnumerable<T>). Specify its DataType (e.g. {{Binding Name, DataType=local:Item}}).", node);
            return;
        }

        state.TrackAccess(itemType);
        NexerBindingPathParser.TryResolve(context, types, state, spec, itemType, node);
    }

    private (NexerBindingSpec? Spec, IXamlAstNode? Node) FindItemsSourceBinding(XamlAstObjectNode targetObject)
    {
        foreach (var child in targetObject.Children)
        {
            var (name, values) = child switch
            {
                XamlAstXamlPropertyValueNode p => (p.Property.GetClrProperty().Name, p.Values),
                XamlPropertyAssignmentNode a => (a.Property.Name, a.Values),
                _ => (null, null),
            };
            if (name == ItemsSourceProperty && values is [XamlAstObjectNode o] && state.Bindings.TryGetValue(o, out var spec))
            {
                return (spec, child);
            }
        }

        return (null, null);
    }

    /// <summary>Resolves the source type and the path of a property binding (idempotent).</summary>
    private bool Resolve(AstTransformationContext context, NexerBindingTypes types, NexerBindingSpec spec, IXamlType targetType, IXamlAstNode node)
    {
        if (spec.PathNode.IsResolved)
        {
            return true;
        }

        if (spec.Failed)
        {
            return false;
        }

        IXamlType? sourceType;
        switch (spec.Source)
        {
            case NexerBindingSourceKind.Self:
                sourceType = targetType;
                break;
            case NexerBindingSourceKind.ElementName:
                if (spec.ElementName is null || !state.Names.TryGetValue(spec.ElementName, out sourceType))
                {
                    spec.Failed = true;
                    BindingSyntaxTransformer.Error(context, NexerXamlDiagnosticCodes.UnknownElementName,
                        $"No element named '{spec.ElementName}' exists in this document.", node);
                    return false;
                }

                break;
            case NexerBindingSourceKind.FindAncestor:
                sourceType = spec.AncestorType;
                if (sourceType is null)
                {
                    spec.Failed = true;
                    return false;
                }

                break;
            default:
                sourceType = spec.DataType ?? FindDataType(context);
                if (sourceType is null)
                {
                    spec.Failed = true;
                    BindingSyntaxTransformer.Error(context, NexerXamlDiagnosticCodes.MissingDataType,
                        $"Cannot compile the binding '{spec.Path}': no x:DataType is in scope. Set x:DataType on this element or an ancestor, or DataType on the binding.", node);
                    return false;
                }

                break;
        }

        state.TrackAccess(sourceType);
        return NexerBindingPathParser.TryResolve(context, types, state, spec, sourceType, node);
    }

    /// <summary>The nearest x:DataType, not crossing into the parent of [DeferredContent] (templates start a new scope).</summary>
    private IXamlType? FindDataType(AstTransformationContext context)
    {
        var deferred = context.Configuration.TypeMappings.DeferredContentPropertyAttributes;
        foreach (var parent in context.ParentNodes())
        {
            if (parent is XamlAstObjectNode o && state.DataTypes.TryGetValue(o, out var type))
            {
                return type;
            }

            if (parent is XamlAstXamlPropertyValueNode { Property: XamlAstClrProperty clr }
                && clr.CustomAttributes.Any(a => deferred.Any(d => d.Equals(a.Type))))
            {
                return null;
            }
        }

        return null;
    }

    private static void SetModeTwoWay(AstTransformationContext context, NexerBindingTypes types, XamlAstObjectNode bindingNode)
    {
        var text = new XamlAstTextNode(bindingNode, "TwoWay", type: context.Configuration.WellKnownTypes.String);
        var existing = bindingNode.Children.OfType<XamlAstXamlPropertyValueNode>().FirstOrDefault(c =>
            c.Property is XamlAstClrProperty { Name: "Mode" } or XamlAstNamePropertyReference { Name: "Mode" });
        if (existing is not null)
        {
            existing.Values = [text];
            return;
        }

        bindingNode.Children.Add(new XamlAstXamlPropertyValueNode(bindingNode,
            new XamlAstClrProperty(bindingNode, types.CompiledBinding.GetAllProperties().First(p => p.Name == "Mode"), context.Configuration),
            text, isAttributeSyntax: true));
    }

    /// <summary>NXAML0105: warns when the runtime conversion rules cannot convert between the path and the target property.</summary>
    private static void CheckTypes(AstTransformationContext context, NexerBindingTypes types, NexerBindingSpec spec, IXamlType targetType, string propertyName, bool twoWay, IXamlAstNode node)
    {
        if (spec.HasConverter || spec.LeafType is not { } leaf)
        {
            return;
        }

        var toTarget = spec.ModeText != "OneWayToSource" && !NexerBindingConversions.CanConvert(types, leaf, targetType);
        var toSource = twoWay && !NexerBindingConversions.CanConvert(types, targetType, leaf);
        if (toTarget || toSource)
        {
            var (from, to) = toTarget ? (leaf, targetType) : (targetType, leaf);
            context.ReportDiagnostic(NexerXamlDiagnosticCodes.BindingTypeMismatch, XamlDiagnosticSeverity.Warning,
                $"The binding '{spec.Path}' on '{propertyName}' converts '{from.GetFqn()}' to '{to.GetFqn()}', which has no built-in conversion. Add a Converter.",
                node);
        }
    }

    /// <summary>The <c>T</c> of the first <c>IEnumerable&lt;T&gt;</c> implemented by <paramref name="type"/> (or its array element type).</summary>
    internal static IXamlType? GetElementType(NexerBindingTypes types, IXamlType type)
    {
        if (type.ArrayElementType is { } element)
        {
            return element;
        }

        return NexerBindingPathParser.SelfAndInterfaces(type)
            .FirstOrDefault(t => t.GenericTypeDefinition?.Equals(types.EnumerableOfT) == true)?.GenericArguments[0];
    }
}

/// <summary>Compile-time mirror of the runtime conversion rules (docs\binding.md, "Conversion rules").</summary>
internal static class NexerBindingConversions
{
    private static readonly HashSet<string> s_primitives = new(StringComparer.Ordinal)
    {
        "System.Boolean", "System.Char", "System.SByte", "System.Byte", "System.Int16", "System.UInt16", "System.Int32",
        "System.UInt32", "System.Int64", "System.UInt64", "System.Single", "System.Double", "System.Decimal",
    };

    public static bool CanConvert(NexerBindingTypes types, IXamlType from, IXamlType to)
    {
        if (to.IsAssignableFrom(from) || to.Equals(types.String) || to.Equals(types.Object) || from.Equals(types.Object))
        {
            return true;
        }

        // A value of a base/interface type may hold an instance of the target type at runtime.
        if (!from.IsValueType && !to.IsValueType && (from.IsAssignableFrom(to) || from.IsInterface || to.IsInterface))
        {
            return true;
        }

        var f = Unwrap(from);
        var t = Unwrap(to);
        if (t.IsAssignableFrom(f))
        {
            return true;
        }

        // string -> primitive/enum via Parse; primitive/enum -> primitive via Convert.ChangeType.
        return t.IsEnum ? f.Equals(types.String) : IsPrimitive(t) && (IsPrimitive(f) || f.IsEnum || f.Equals(types.String));
    }

    private static bool IsPrimitive(IXamlType type) => s_primitives.Contains(type.FullName);

    private static IXamlType Unwrap(IXamlType type) =>
        type.GenericTypeDefinition?.FullName == "System.Nullable`1" ? type.GenericArguments[0] : type;
}

/// <summary>The kind of a resolved path segment.</summary>
internal enum NexerBindingStepKind
{
    Property,
    Indexer,
    Attached,
}

/// <summary>One resolved path segment.</summary>
/// <param name="Name">The <see cref="NexerBindingPathNode"/> name (property name, <c>Item[]</c> or <c>(Type.Prop)</c>).</param>
/// <param name="DeclaringType">The owner type the accessor checks with <c>isinst</c>.</param>
/// <param name="ValueType">The member type.</param>
/// <param name="Getter">Instance getter (static <c>GetX(owner)</c> for attached properties).</param>
/// <param name="Setter">Instance setter, or <see langword="null"/> when read-only.</param>
/// <param name="Notifies">Whether the owner type implements <c>INotifyPropertyChanged</c>.</param>
/// <param name="Kind">The segment kind.</param>
/// <param name="Argument">The indexer argument (<see cref="int"/> or <see cref="string"/>).</param>
internal sealed record NexerBindingPathStep(
    string Name,
    IXamlType DeclaringType,
    IXamlType ValueType,
    IXamlMethod Getter,
    IXamlMethod? Setter,
    bool Notifies,
    NexerBindingStepKind Kind = NexerBindingStepKind.Property,
    object? Argument = null);

/// <summary>
/// Parses and resolves a binding path against a source type. Grammar:
/// <c>path := '' | '.' | segment (('.' segment) | indexer)*</c>;
/// <c>segment := identifier | '(' type '.' identifier ')' | '((' type ')' path ')' | indexer</c>;
/// <c>indexer := '[' (integer | key | 'key' | "key") ']'</c>.
/// </summary>
internal sealed class NexerBindingPathParser
{
    private const string NotifyPropertyChanged = "System.ComponentModel.INotifyPropertyChanged";

    private readonly AstTransformationContext _context;
    private readonly NexerBindingTypes _types;
    private readonly NexerBindingState _state;
    private readonly IXamlLineInfo _lineInfo;
    private readonly string _path;
    private readonly List<NexerBindingPathStep> _steps = [];
    private int _position;

    private NexerBindingPathParser(AstTransformationContext context, NexerBindingTypes types, NexerBindingState state, string path, IXamlLineInfo lineInfo)
    {
        _context = context;
        _types = types;
        _state = state;
        _path = path;
        _lineInfo = lineInfo;
    }

    private bool AtEnd => _position >= _path.Length;

    private char Current => _path[_position];

    public static bool TryResolve(AstTransformationContext context, NexerBindingTypes types, NexerBindingState state, NexerBindingSpec spec, IXamlType sourceType, IXamlAstNode node)
    {
        var parser = new NexerBindingPathParser(context, types, state, spec.Path, node);
        try
        {
            var leaf = parser.Parse(sourceType);
            spec.LeafType = leaf;
            spec.PathNode.Resolve(spec, parser._steps);
            return true;
        }
        catch (PathException e)
        {
            spec.Failed = true;
            BindingSyntaxTransformer.Error(context, e.Code, e.Message, node);
            return false;
        }
    }

    /// <summary>The type itself (when it is an interface) and every interface it implements, recursively.</summary>
    internal static IEnumerable<IXamlType> SelfAndInterfaces(IXamlType type)
    {
        var seen = new HashSet<IXamlType>();
        var queue = new Queue<IXamlType>();
        if (type.IsInterface)
        {
            queue.Enqueue(type);
        }

        foreach (var i in type.GetAllInterfaces())
        {
            queue.Enqueue(i);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current))
            {
                continue;
            }

            yield return current;
            foreach (var i in current.Interfaces)
            {
                queue.Enqueue(i);
            }
        }
    }

    private static bool Notifies(IXamlType type) =>
        type.FullName == NotifyPropertyChanged || SelfAndInterfaces(type).Any(i => i.FullName == NotifyPropertyChanged);

    private IXamlType Parse(IXamlType source)
    {
        var trimmed = _path.Trim();
        if (trimmed.Length == 0 || trimmed == ".")
        {
            return source;
        }

        var leaf = ParseChain(source, nested: false);
        if (!AtEnd)
        {
            throw Syntax($"unexpected '{Current}' at position {_position + 1}");
        }

        return leaf;
    }

    private IXamlType ParseChain(IXamlType current, bool nested)
    {
        var first = true;
        while (true)
        {
            SkipWhitespace();
            if (AtEnd || (nested && Current == ')'))
            {
                return first ? throw Syntax("empty path") : current;
            }

            if (Current == '[')
            {
                current = ParseIndexer(current);
            }
            else
            {
                if (!first)
                {
                    if (Current != '.')
                    {
                        throw Syntax($"expected '.' at position {_position + 1}");
                    }

                    _position++;
                    SkipWhitespace();
                }

                current = ParseElement(current);
            }

            first = false;
        }
    }

    private IXamlType ParseElement(IXamlType current)
    {
        if (AtEnd)
        {
            throw Syntax("the path ends with '.'");
        }

        if (Current == '(')
        {
            return _position + 1 < _path.Length && _path[_position + 1] == '(' ? ParseCast(current) : ParseAttached(current);
        }

        var start = _position;
        while (!AtEnd && (char.IsLetterOrDigit(Current) || Current == '_'))
        {
            _position++;
        }

        if (start == _position || char.IsDigit(_path[start]))
        {
            throw Syntax($"expected a property name at position {start + 1}");
        }

        return ResolveProperty(current, _path[start.._position]);
    }

    private IXamlType ParseCast(IXamlType current)
    {
        _position += 2;
        var typeName = ReadUntil(')').Trim();
        var castType = ResolveType(typeName);
        var inner = ParseChain(current, nested: true);
        Expect(')');
        if (!(inner.IsAssignableFrom(castType) || castType.IsAssignableFrom(inner) || inner.IsInterface || castType.IsInterface))
        {
            throw new PathException(NexerXamlDiagnosticCodes.InvalidBindingPath,
                $"Cannot cast '{inner.GetFqn()}' to '{castType.GetFqn()}' (binding path '{_path}').");
        }

        // A cast needs no node: the next segment's accessor checks the owner type (isinst) and yields UnsetValue otherwise.
        _state.TrackAccess(castType);
        return castType;
    }

    private IXamlType ParseAttached(IXamlType current)
    {
        _position++;
        var text = ReadUntil(')').Trim();
        var dot = text.LastIndexOf('.');
        if (dot <= 0 || dot == text.Length - 1)
        {
            throw Syntax($"'({text})' is not an attached property reference; use (prefix:Type.Property)");
        }

        var ownerType = ResolveType(text[..dot]);
        var name = text[(dot + 1)..].Trim();
        var getter = ownerType.Methods.FirstOrDefault(m => m.Name == "Get" + name && m.IsStatic && m.Parameters.Count == 1);
        if (getter is null)
        {
            throw new PathException(NexerXamlDiagnosticCodes.UnknownMember,
                $"'{ownerType.GetFqn()}' has no attached property '{name}' (no static Get{name}(object) method; binding path '{_path}').");
        }

        if (!getter.IsPublic)
        {
            throw new PathException(NexerXamlDiagnosticCodes.InaccessibleMember,
                $"'{ownerType.GetFqn()}.Get{name}' is not public (binding path '{_path}').");
        }

        var parameter = getter.Parameters[0];
        if (!(parameter.IsAssignableFrom(current) || current.IsAssignableFrom(parameter)))
        {
            throw new PathException(NexerXamlDiagnosticCodes.UnknownMember,
                $"The attached property '{ownerType.Name}.{name}' applies to '{parameter.GetFqn()}', not '{current.GetFqn()}' (binding path '{_path}').");
        }

        _state.TrackAccess(ownerType);
        _state.TrackAccess(getter.ReturnType);
        _steps.Add(new NexerBindingPathStep($"({ownerType.Name}.{name})", parameter, getter.ReturnType, getter, null, false, NexerBindingStepKind.Attached));
        return getter.ReturnType;
    }

    private IXamlType ParseIndexer(IXamlType current)
    {
        _position++;
        var raw = ReadUntil(']').Trim();
        string? key = null;
        int? index = null;
        if (raw.Length >= 2 && (raw[0] is '\'' or '"') && raw[^1] == raw[0])
        {
            key = raw[1..^1];
        }
        else if (int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
        {
            index = number;
        }
        else if (raw.Length > 0)
        {
            key = raw;
        }
        else
        {
            throw Syntax("empty indexer '[]'");
        }

        var owner = current;
        var indexers = new List<IXamlProperty>();
        if (current.ArrayElementType is { } element)
        {
            owner = _types.ListOfT.MakeGenericType([element]);
        }

        if (!owner.IsInterface)
        {
            indexers.AddRange(owner.GetAllProperties().Where(IsIndexer));
        }

        indexers.AddRange(SelfAndInterfaces(owner).SelectMany(t => t.Properties).Where(IsIndexer));

        IXamlProperty? indexer = null;
        object argument;
        if (index is { } i && indexers.FirstOrDefault(p => p.Getter!.Parameters[0].Equals(_types.Int32)) is { } intIndexer)
        {
            indexer = intIndexer;
            argument = i;
        }
        else
        {
            indexer = indexers.FirstOrDefault(p => p.Getter!.Parameters[0].Equals(_types.String));
            argument = key ?? raw;
        }

        if (indexer is null)
        {
            throw new PathException(NexerXamlDiagnosticCodes.UnknownMember,
                $"'{current.GetFqn()}' has no {(index is null ? "string" : "int or string")} indexer for '[{raw}]' (binding path '{_path}').");
        }

        if (!indexer.Getter!.IsPublic)
        {
            throw new PathException(NexerXamlDiagnosticCodes.InaccessibleMember,
                $"The indexer of '{current.GetFqn()}' is not public (binding path '{_path}').");
        }

        var declaring = indexer.Getter.DeclaringType.IsInterface ? indexer.Getter.DeclaringType : owner;
        var setter = indexer.Setter is { IsPublic: true, IsStatic: false } s && s.Parameters.Count == 2 && !declaring.IsValueType ? s : null;
        var valueType = indexer.Getter.ReturnType;
        _state.TrackAccess(declaring);
        _state.TrackAccess(valueType);
        _steps.Add(new NexerBindingPathStep("Item[]", declaring, valueType, indexer.Getter, setter, Notifies(current),
            NexerBindingStepKind.Indexer, argument));
        return valueType;
    }

    private static bool IsIndexer(IXamlProperty property) => property.Getter is { IsStatic: false } getter && getter.Parameters.Count == 1;

    private IXamlType ResolveProperty(IXamlType current, string name)
    {
        var candidates = current.GetAllProperties().Where(p => p.Name == name && p.Getter is not null && p.Getter.Parameters.Count == 0);
        if (current.IsInterface)
        {
            candidates = SelfAndInterfaces(current).SelectMany(i => i.Properties)
                .Where(p => p.Name == name && p.Getter is not null && p.Getter.Parameters.Count == 0);
        }

        var property = candidates.FirstOrDefault(p => p.Getter is { IsPublic: true, IsStatic: false }) ?? candidates.FirstOrDefault();
        if (property is null)
        {
            throw new PathException(NexerXamlDiagnosticCodes.UnknownMember,
                $"'{current.GetFqn()}' has no readable property '{name}' (binding path '{_path}').");
        }

        if (!property.Getter!.IsPublic || property.Getter.IsStatic)
        {
            throw new PathException(NexerXamlDiagnosticCodes.InaccessibleMember,
                $"'{current.GetFqn()}.{name}' is not a public instance property (binding path '{_path}').");
        }

        var setter = property.Setter is { IsPublic: true, IsStatic: false } s && s.Parameters.Count == 1 && !current.IsValueType ? s : null;
        var declaring = property.Getter.DeclaringType.IsInterface ? property.Getter.DeclaringType : current;
        _state.TrackAccess(declaring);
        _state.TrackAccess(property.PropertyType);
        _steps.Add(new NexerBindingPathStep(name, declaring, property.PropertyType, property.Getter, setter, Notifies(current)));
        return property.PropertyType;
    }

    private IXamlType ResolveType(string name)
    {
        if (name.Length == 0)
        {
            throw Syntax("missing type name");
        }

        try
        {
            return TypeReferenceResolver.ResolveType(_context, name, false, _lineInfo, true).Type;
        }
        catch (XamlTransformException e)
        {
            throw new PathException(NexerXamlDiagnosticCodes.InvalidBindingPath, $"{e.Message} (binding path '{_path}').");
        }
    }

    private string ReadUntil(char terminator)
    {
        var end = _path.IndexOf(terminator, _position);
        if (end < 0)
        {
            throw Syntax($"missing '{terminator}'");
        }

        var text = _path[_position..end];
        _position = end + 1;
        return text;
    }

    private void Expect(char c)
    {
        SkipWhitespace();
        if (AtEnd || Current != c)
        {
            throw Syntax($"expected '{c}' at position {_position + 1}");
        }

        _position++;
    }

    private void SkipWhitespace()
    {
        while (!AtEnd && char.IsWhiteSpace(Current))
        {
            _position++;
        }
    }

    private PathException Syntax(string detail) =>
        new(NexerXamlDiagnosticCodes.InvalidBindingPath, $"Invalid binding path '{_path}': {detail}.");

    private sealed class PathException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}

/// <summary>
/// The value of <c>CompiledBinding.Path</c>: emits <c>new CompiledBindingPath(kind, elementName, ancestorType, level,
/// new[] { new PathNode(name, typeof(T), getter, setter, notifies), ... })</c> with getter/setter delegates bound to
/// static helper methods (see <see cref="NexerBindingAccessors"/>).
/// </summary>
internal sealed class NexerBindingPathNode(IXamlLineInfo lineInfo, NexerBindingTypes types)
    : XamlAstNode(lineInfo), IXamlAstValueNode, IXamlAstEmitableNode<IXamlILEmitter, XamlILNodeEmitResult>
{
    private NexerBindingSpec? _spec;
    private IReadOnlyList<NexerBindingPathStep> _steps = [];

    public IXamlAstTypeReference Type { get; } = new XamlAstClrTypeReference(lineInfo, types.CompiledBindingPath, false);

    public bool IsResolved => _spec is not null;

    public bool IsWritable => _steps.Count > 0 && _steps[^1].Setter is not null;

    public IReadOnlyList<NexerBindingPathStep> Steps => _steps;

    public void Resolve(NexerBindingSpec spec, IReadOnlyList<NexerBindingPathStep> steps)
    {
        _spec = spec;
        _steps = steps;
    }

    public XamlILNodeEmitResult Emit(XamlEmitContext<IXamlILEmitter, XamlILNodeEmitResult> context, IXamlILEmitter codeGen)
    {
        if (_spec is null)
        {
            throw new NexerXamlException(NexerXamlDiagnosticCodes.InvalidBindingTarget, "The binding was not resolved.", this);
        }

        var accessors = NexerBindingAccessors.Get(context, types);
        codeGen.Ldc_I4((int)_spec.Source);
        if (_spec.ElementName is null)
        {
            codeGen.Emit(OpCodes.Ldnull);
        }
        else
        {
            codeGen.Ldstr(_spec.ElementName);
        }

        if (_spec.AncestorType is null)
        {
            codeGen.Emit(OpCodes.Ldnull);
        }
        else
        {
            codeGen.Ldtype(_spec.AncestorType);
        }

        codeGen.Ldc_I4(_spec.AncestorLevel).Ldc_I4(_steps.Count).Newarr(types.PathNode);
        for (var i = 0; i < _steps.Count; i++)
        {
            var step = _steps[i];
            codeGen.Dup().Ldc_I4(i).Ldstr(step.Name).Ldtype(step.ValueType);
            NexerBindingAccessors.EmitDelegate(codeGen, accessors.DefineGetter(step.DeclaringType, step.Getter, step.Argument), types.GetterConstructor);
            if (step.Setter is null)
            {
                codeGen.Emit(OpCodes.Ldnull);
            }
            else
            {
                NexerBindingAccessors.EmitDelegate(codeGen,
                    accessors.DefineSetter(step.DeclaringType, step.ValueType, step.Setter, step.Argument), types.SetterConstructor);
            }

            codeGen.Ldc_I4(step.Notifies ? 1 : 0).Newobj(types.PathNodeConstructor).Stelem_ref();
        }

        codeGen.Newobj(types.PathConstructor);
        return XamlILNodeEmitResult.Type(0, types.CompiledBindingPath);
    }
}

/// <summary>
/// The setter used for bound properties: with the target on the stack, emits
/// <c>BindingOperations.Bind(target, "Prop", getter, setter, typeof(TProp), binding)</c> and discards the result.
/// </summary>
internal sealed class NexerBindingSetter(NexerBindingTypes types, IXamlType targetType, IXamlProperty property) : IXamlILOptimizedEmitablePropertySetter
{
    public IXamlType TargetType { get; } = targetType;

    public PropertySetterBinderParameters BinderParameters { get; } = new() { AllowMultiple = false, AllowXNull = false, AllowRuntimeNull = false };

    public IReadOnlyList<IXamlType> Parameters { get; } = [types.IBinding];

    public IReadOnlyList<IXamlCustomAttribute> CustomAttributes => property.CustomAttributes;

    public void Emit(IXamlILEmitter emitter) =>
        throw new InvalidOperationException("Binding setters are emitted with their arguments.");

    public void EmitWithArguments(
        XamlEmitContextWithLocals<IXamlILEmitter, XamlILNodeEmitResult> context,
        IXamlILEmitter emitter,
        IReadOnlyList<IXamlAstValueNode> arguments)
    {
        var accessors = NexerBindingAccessors.Get(context, types);
        var declaringType = property.Setter!.DeclaringType;
        emitter.Ldstr(property.Name);
        if (property.Getter is { IsPublic: true, IsStatic: false } getter && getter.Parameters.Count == 0)
        {
            NexerBindingAccessors.EmitDelegate(emitter, accessors.DefineGetter(declaringType, getter, null), types.GetterConstructor);
        }
        else
        {
            emitter.Emit(OpCodes.Ldnull);
        }

        NexerBindingAccessors.EmitDelegate(emitter, accessors.DefineSetter(declaringType, property.PropertyType, property.Setter, null), types.SetterConstructor);
        emitter.Ldtype(property.PropertyType);
        context.Emit(arguments[^1], emitter, types.IBinding);
        emitter.EmitCall(types.Bind, true);
    }
}

/// <summary>
/// A private nested type (one per emit context) holding the static accessor methods that binding delegates point to:
/// <c>static object Get_N(object o) =&gt; o is T t ? t.Prop : BindingOperations.UnsetValue</c> and
/// <c>static void Set_N(object o, object v) { if (o is T t) t.Prop = (TProp)v; }</c> (indexers pass their constant
/// argument; attached properties call the static <c>GetX(o)</c>).
/// Mirrors XamlX's deferred-content closure type, so it works identically for Cecil and System.Reflection.Emit.
/// </summary>
internal sealed class NexerBindingAccessors
{
    private readonly IXamlTypeBuilder<IXamlILEmitter> _type;
    private readonly TransformerConfiguration _configuration;
    private readonly IXamlMethod _unsetValue;
    private readonly Dictionary<(IXamlType, IXamlMethod, bool, object?), IXamlMethod> _methods = [];

    private NexerBindingAccessors(IXamlTypeBuilder<IXamlILEmitter> type, TransformerConfiguration configuration, NexerBindingTypes types)
    {
        _type = type;
        _configuration = configuration;
        _unsetValue = types.BindingOperations.GetAllProperties().First(p => p.Name == "UnsetValue").Getter!;
    }

    public static NexerBindingAccessors Get(XamlEmitContext<IXamlILEmitter, XamlILNodeEmitResult> context, NexerBindingTypes types)
    {
        if (!context.TryGetItem(out NexerBindingAccessors? accessors))
        {
            var type = context.DeclaringType.DefineSubType(context.Configuration.WellKnownTypes.Object,
                context.Configuration.IdentifierGenerator.CreateIdentifierFromPrefix("XamlBindingAccessors_"), XamlVisibility.Private);
            accessors = new NexerBindingAccessors(type, context.Configuration, types);
            context.AddAfterEmitCallbacks(() => type.CreateType());
            context.SetItem(accessors);
        }

        return accessors;
    }

    public static void EmitDelegate(IXamlILEmitter il, IXamlMethod method, IXamlConstructor delegateConstructor) =>
        il.Emit(OpCodes.Ldnull).Ldftn(method).Newobj(delegateConstructor);

    public IXamlMethod DefineGetter(IXamlType declaringType, IXamlMethod getter, object? argument)
    {
        if (_methods.TryGetValue((declaringType, getter, false, argument), out var existing))
        {
            return existing;
        }

        var wkt = _configuration.WellKnownTypes;
        var method = _type.DefineMethod(wkt.Object, [wkt.Object], "Get_" + _methods.Count, XamlVisibility.Public, true, false);
        var il = method.Generator;
        var ok = il.DefineLabel();
        il.Ldarg_0().Isinst(declaringType).Brtrue(ok).EmitCall(_unsetValue).Ret().MarkLabel(ok).Ldarg_0();
        if (getter.IsStatic)
        {
            ConvertFromObject(il, declaringType);
            il.EmitCall(getter);
        }
        else if (declaringType.IsValueType)
        {
            il.Unbox(declaringType);
            LoadArgument(il, argument);
            il.Emit(OpCodes.Call, getter);
        }
        else
        {
            il.Castclass(declaringType);
            LoadArgument(il, argument);
            il.EmitCall(getter);
        }

        if (getter.ReturnType.IsValueType)
        {
            il.Box(getter.ReturnType);
        }

        il.Ret();
        _methods[(declaringType, getter, false, argument)] = method;
        return method;
    }

    public IXamlMethod DefineSetter(IXamlType declaringType, IXamlType valueType, IXamlMethod setter, object? argument)
    {
        if (_methods.TryGetValue((declaringType, setter, true, argument), out var existing))
        {
            return existing;
        }

        var wkt = _configuration.WellKnownTypes;
        var method = _type.DefineMethod(wkt.Void, [wkt.Object, wkt.Object], "Set_" + _methods.Count, XamlVisibility.Public, true, false);
        var il = method.Generator;
        var ok = il.DefineLabel();
        il.Ldarg_0().Isinst(declaringType).Brtrue(ok).Ret().MarkLabel(ok).Ldarg_0().Castclass(declaringType);
        LoadArgument(il, argument);
        il.Ldarg(1);
        ConvertFromObject(il, valueType);
        il.EmitCall(setter, true).Ret();
        _methods[(declaringType, setter, true, argument)] = method;
        return method;
    }

    private static void ConvertFromObject(IXamlILEmitter il, IXamlType type)
    {
        if (type.IsValueType)
        {
            il.Unbox_Any(type);
        }
        else
        {
            il.Castclass(type);
        }
    }

    private static void LoadArgument(IXamlILEmitter il, object? argument)
    {
        switch (argument)
        {
            case int i:
                il.Ldc_I4(i);
                break;
            case string s:
                il.Ldstr(s);
                break;
        }
    }
}
