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
        var obj = ts.GetType("System.Object");
        Getter = ts.GetType("System.Func`2").MakeGenericType([obj, obj]);
        Setter = ts.GetType("System.Action`2").MakeGenericType([obj, obj]);
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
}

/// <summary>Per-document state shared by the binding transformers.</summary>
internal sealed class NexerBindingState(NexerBindingTypes? types)
{
    public NexerBindingTypes? Types { get; } = types;

    /// <summary>x:DataType per element (by reference).</summary>
    public Dictionary<XamlAstObjectNode, IXamlType> DataTypes { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Element types by x:Name / Name, for <c>ElementName</c> bindings.</summary>
    public Dictionary<string, IXamlType> Names { get; } = new(StringComparer.Ordinal);

    /// <summary>The converted binding object nodes (retyped to <c>CompiledBinding</c>).</summary>
    public Dictionary<XamlAstObjectNode, NexerBindingSpec> Bindings { get; } = new(ReferenceEqualityComparer.Instance);
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
                Error(context, NexerXamlDiagnosticCodes.InvalidRelativeSource, "RelativeSource FindAncestor requires AncestorType.", value);
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
/// type (x:DataType for DataContext bindings), applies <c>[BindsTwoWayByDefault]</c>, and replaces the value with a
/// <see cref="XamlPropertyAssignmentNode"/> whose only setter is <see cref="NexerBindingSetter"/> (a call to
/// <c>BindingOperations.Bind</c>). Bindings assigned to <c>IBinding</c>-typed properties (e.g. <c>DisplayMemberBinding</c>)
/// are left as ordinary values, resolved against their <c>DataType</c>.
/// Must run after <see cref="PropertyReferenceResolver"/> and before <see cref="ConvertPropertyValuesToAssignmentsTransformer"/>.
/// </summary>
internal sealed class BindingAssignmentTransformer(NexerBindingState state) : IXamlAstTransformer
{
    public IXamlAstNode Transform(AstTransformationContext context, IXamlAstNode node)
    {
        if (state.Types is not { } types)
        {
            return node;
        }

        if (node is XamlAstObjectNode obj && state.Bindings.TryGetValue(obj, out var orphan) && !orphan.PathNode.IsResolved)
        {
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
            return BindingSyntaxTransformer.Error(context, NexerXamlDiagnosticCodes.InvalidBindingTarget,
                "A property set by a {Binding} cannot have other values.", node);
        }

        var spec = state.Bindings[bindingNode];
        var property = value.Property.GetClrProperty();
        var targetObject = context.ParentNodes().OfType<XamlAstObjectNode>().FirstOrDefault();
        var targetType = targetObject?.Type.GetClrType();
        var clrProperty = targetType?.GetAllProperties().FirstOrDefault(p =>
            p.Name == property.Name && p.Setter is { IsPublic: true, IsStatic: false } && p.Setter.Parameters.Count == 1);
        if (clrProperty is not null && clrProperty.PropertyType.IsAssignableFrom(types.CompiledBinding))
        {
            // The binding object itself is the value (per-item bindings such as DisplayMemberBinding).
            if (spec.DataType is null || spec.Source != NexerBindingSourceKind.DataContext)
            {
                return BindingSyntaxTransformer.Error(context, NexerXamlDiagnosticCodes.MissingDataType,
                    $"The binding assigned to '{property.Name}' is evaluated per item; specify its DataType (e.g. {{Binding Name, DataType=local:Item}}) and no ElementName/RelativeSource.", node);
            }

            ResolvePath(context, types, spec, spec.DataType, bindingNode);
            return node;
        }

        if (targetType is null || clrProperty is null || !types.Widget.IsAssignableFrom(targetType))
        {
            return BindingSyntaxTransformer.Error(context, NexerXamlDiagnosticCodes.InvalidBindingTarget,
                $"'{property.Name}' cannot be bound: a binding target must be a public settable instance property of a widget.", node);
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
                    return BindingSyntaxTransformer.Error(context, NexerXamlDiagnosticCodes.UnknownElementName,
                        $"No element named '{spec.ElementName}' exists in this document.", node);
                }

                break;
            case NexerBindingSourceKind.FindAncestor:
                sourceType = spec.AncestorType;
                break;
            default:
                sourceType = spec.DataType ?? context.ParentNodes().OfType<XamlAstObjectNode>()
                    .Select(o => state.DataTypes.TryGetValue(o, out var t) ? t : null).FirstOrDefault(t => t is not null);
                if (sourceType is null)
                {
                    return BindingSyntaxTransformer.Error(context, NexerXamlDiagnosticCodes.MissingDataType,
                        $"Cannot compile the binding '{spec.Path}': no x:DataType is in scope. Set x:DataType on this element or an ancestor, or DataType on the binding.", node);
                }

                break;
        }

        if (!ResolvePath(context, types, spec, sourceType!, bindingNode))
        {
            return node;
        }

        var twoWay = spec.ModeText is "TwoWay" or "OneWayToSource";
        if (!spec.HasMode && property.CustomAttributes.Any(a => a.Type.Equals(types.BindsTwoWayByDefault)))
        {
            twoWay = true;
            bindingNode.Children.Add(new XamlAstXamlPropertyValueNode(bindingNode,
                new XamlAstClrProperty(bindingNode, types.CompiledBinding.GetAllProperties().First(p => p.Name == "Mode"), context.Configuration),
                new XamlAstTextNode(bindingNode, "TwoWay", type: context.Configuration.WellKnownTypes.String), isAttributeSyntax: true));
        }

        if (twoWay && !spec.PathNode.IsWritable)
        {
            return BindingSyntaxTransformer.Error(context, NexerXamlDiagnosticCodes.TwoWayToReadOnly,
                $"The binding '{spec.Path}' on '{property.Name}' is two-way but the source path is not writable. Use Mode=OneWay.", node);
        }

        return new XamlPropertyAssignmentNode(value, property, [new NexerBindingSetter(types, targetType, clrProperty)], [bindingNode]);
    }

    private static bool ResolvePath(AstTransformationContext context, NexerBindingTypes types, NexerBindingSpec spec, IXamlType sourceType, IXamlAstNode node)
    {
        var steps = new List<NexerBindingPathStep>();
        var current = sourceType;
        var path = spec.Path;
        if (path is "." or "")
        {
            path = "";
        }

        foreach (var rawSegment in path.Length == 0 ? [] : path.Split('.'))
        {
            var segment = rawSegment.Trim();
            if (segment.Length == 0 || segment.Contains('[', StringComparison.Ordinal) || segment.Contains('(', StringComparison.Ordinal))
            {
                BindingSyntaxTransformer.Error(context, NexerXamlDiagnosticCodes.InvalidBindingPath,
                    $"Invalid or unsupported binding path '{spec.Path}'. Use dotted property names.", node);
                return false;
            }

            var property = current.GetAllProperties().FirstOrDefault(p =>
                p.Name == segment && p.Getter is { IsStatic: false } && p.Getter.Parameters.Count == 0);
            if (property is null)
            {
                BindingSyntaxTransformer.Error(context, NexerXamlDiagnosticCodes.UnknownMember,
                    $"'{current.GetFqn()}' has no readable property '{segment}' (binding path '{spec.Path}').", node);
                return false;
            }

            if (!property.Getter!.IsPublic)
            {
                BindingSyntaxTransformer.Error(context, NexerXamlDiagnosticCodes.InaccessibleMember,
                    $"'{current.GetFqn()}.{segment}' is not public (binding path '{spec.Path}').", node);
                return false;
            }

            var setter = property.Setter is { IsPublic: true, IsStatic: false } s && s.Parameters.Count == 1 && !current.IsValueType ? s : null;
            steps.Add(new NexerBindingPathStep(segment, current, property.PropertyType, property.Getter, setter, Notifies(current)));
            current = property.PropertyType;
        }

        spec.PathNode.Resolve(spec, steps);
        return true;
    }

    private static bool Notifies(IXamlType type) =>
        type.FullName == "System.ComponentModel.INotifyPropertyChanged"
        || type.GetAllInterfaces().Any(i => i.FullName == "System.ComponentModel.INotifyPropertyChanged");
}

/// <summary>One resolved path segment.</summary>
internal sealed record NexerBindingPathStep(string Name, IXamlType DeclaringType, IXamlType ValueType, IXamlMethod Getter, IXamlMethod? Setter, bool Notifies);

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
            NexerBindingAccessors.EmitDelegate(codeGen, accessors.DefineGetter(step.DeclaringType, step.Getter), types.GetterConstructor);
            if (step.Setter is null)
            {
                codeGen.Emit(OpCodes.Ldnull);
            }
            else
            {
                NexerBindingAccessors.EmitDelegate(codeGen, accessors.DefineSetter(step.DeclaringType, step.ValueType, step.Setter), types.SetterConstructor);
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
            NexerBindingAccessors.EmitDelegate(emitter, accessors.DefineGetter(declaringType, getter), types.GetterConstructor);
        }
        else
        {
            emitter.Emit(OpCodes.Ldnull);
        }

        NexerBindingAccessors.EmitDelegate(emitter, accessors.DefineSetter(declaringType, property.PropertyType, property.Setter), types.SetterConstructor);
        emitter.Ldtype(property.PropertyType);
        context.Emit(arguments[^1], emitter, types.IBinding);
        emitter.EmitCall(types.Bind, true);
    }
}

/// <summary>
/// A private nested type (one per emit context) holding the static accessor methods that binding delegates point to:
/// <c>static object Get_N(object o) =&gt; o is T t ? t.Prop : BindingOperations.UnsetValue</c> and
/// <c>static void Set_N(object o, object v) { if (o is T t) t.Prop = (TProp)v; }</c>.
/// Mirrors XamlX's deferred-content closure type, so it works identically for Cecil and System.Reflection.Emit.
/// </summary>
internal sealed class NexerBindingAccessors
{
    private readonly IXamlTypeBuilder<IXamlILEmitter> _type;
    private readonly TransformerConfiguration _configuration;
    private readonly IXamlMethod _unsetValue;
    private readonly Dictionary<(IXamlMethod, bool), IXamlMethod> _methods = [];

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

    public IXamlMethod DefineGetter(IXamlType declaringType, IXamlMethod getter)
    {
        if (_methods.TryGetValue((getter, false), out var existing))
        {
            return existing;
        }

        var wkt = _configuration.WellKnownTypes;
        var method = _type.DefineMethod(wkt.Object, [wkt.Object], "Get_" + _methods.Count, XamlVisibility.Public, true, false);
        var il = method.Generator;
        var ok = il.DefineLabel();
        il.Ldarg_0().Isinst(declaringType).Brtrue(ok).EmitCall(_unsetValue).Ret().MarkLabel(ok).Ldarg_0();
        if (declaringType.IsValueType)
        {
            il.Unbox(declaringType).Emit(OpCodes.Call, getter);
        }
        else
        {
            il.Castclass(declaringType).EmitCall(getter);
        }

        if (getter.ReturnType.IsValueType)
        {
            il.Box(getter.ReturnType);
        }

        il.Ret();
        _methods[(getter, false)] = method;
        return method;
    }

    public IXamlMethod DefineSetter(IXamlType declaringType, IXamlType valueType, IXamlMethod setter)
    {
        if (_methods.TryGetValue((setter, true), out var existing))
        {
            return existing;
        }

        var wkt = _configuration.WellKnownTypes;
        var method = _type.DefineMethod(wkt.Void, [wkt.Object, wkt.Object], "Set_" + _methods.Count, XamlVisibility.Public, true, false);
        var il = method.Generator;
        var ok = il.DefineLabel();
        il.Ldarg_0().Isinst(declaringType).Brtrue(ok).Ret().MarkLabel(ok).Ldarg_0().Castclass(declaringType).Ldarg(1);
        if (valueType.IsValueType)
        {
            il.Unbox_Any(valueType);
        }
        else
        {
            il.Castclass(valueType);
        }

        il.EmitCall(setter, true).Ret();
        _methods[(setter, true)] = method;
        return method;
    }
}
