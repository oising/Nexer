using XamlX;
using XamlX.Ast;
using XamlX.Transform;
using XamlX.Transform.Transformers;
using XamlX.TypeSystem;

namespace Nexer.Xaml.Compiler.Transformers;

/// <summary>
/// Handles <c>x:Class</c> on the root element: the document's root type becomes the code-behind class, so event
/// handlers resolve against it and the generated populate method takes it as its target. Optionally applies a root
/// type override (the runtime loader uses the type of the instance being populated).
/// Must run first (before <see cref="TypeReferenceResolver"/>).
/// </summary>
internal sealed class XClassTransformer(IXamlType? rootTypeOverride) : IXamlAstTransformer
{
    /// <summary>The resolved <c>x:Class</c> type, if the document has one.</summary>
    public IXamlType? ClassType { get; private set; }

    public IXamlAstNode Transform(AstTransformationContext context, IXamlAstNode node)
    {
        if (node is not XamlAstObjectNode obj)
        {
            return node;
        }

        var isRoot = !context.ParentNodes().Any();
        var directive = obj.Children.OfType<XamlAstXmlDirective>()
            .FirstOrDefault(d => d.Namespace == XamlNamespaces.Xaml2006 && d.Name == "Class");
        if (directive is not null)
        {
            obj.Children.Remove(directive);
            if (!isRoot)
            {
                throw new NexerXamlException(NexerXamlDiagnosticCodes.InvalidDirective,
                    "x:Class is only allowed on the root element.", directive);
            }
        }

        if (!isRoot)
        {
            return node;
        }

        IXamlType? classType = null;
        if (directive is not null)
        {
            var name = GetDirectiveText(directive);
            classType = context.Configuration.TypeSystem.FindType(name)
                ?? context.Configuration.TypeSystem.FindType(name.Replace('+', '/'))
                ?? throw new NexerXamlException(NexerXamlDiagnosticCodes.ClassNotFound,
                    $"Unable to find the x:Class type '{name}'. Make sure the code-behind class exists.", directive);
            if (rootTypeOverride is not null && !classType.IsAssignableFrom(rootTypeOverride))
            {
                throw new NexerXamlException(NexerXamlDiagnosticCodes.ClassNotFound,
                    $"The root instance type '{rootTypeOverride.GetFqn()}' is not compatible with x:Class '{name}'.", directive);
            }
        }

        var rootType = rootTypeOverride ?? classType;
        ClassType = classType;
        if (rootType is null)
        {
            return node;
        }

        if (obj.Type is XamlAstXmlTypeReference xmlType)
        {
            var declared = TypeReferenceResolver.ResolveType(context, xmlType).Type;
            if (!declared.IsAssignableFrom(rootType))
            {
                throw new NexerXamlException(NexerXamlDiagnosticCodes.ClassNotFound,
                    $"The type '{rootType.GetFqn()}' must derive from the root element type '{declared.GetFqn()}'.", obj);
            }
        }

        var clrType = new XamlAstClrTypeReference(obj, rootType, false);
        obj.Type = clrType;
        if (context.RootObject is XamlRootObjectNode rootObject)
        {
            rootObject.Type = clrType;
        }

        return node;
    }

    internal static string GetDirectiveText(XamlAstXmlDirective directive)
    {
        if (directive.Values.Count == 1 && directive.Values[0] is XamlAstTextNode text && !string.IsNullOrWhiteSpace(text.Text))
        {
            return text.Text.Trim();
        }

        throw new NexerXamlException(NexerXamlDiagnosticCodes.InvalidDirective,
            $"x:{directive.Name} must be a non-empty string.", directive);
    }
}

/// <summary>Removes directives that only matter to tooling or code generation (x:ClassModifier, x:FieldModifier, x:Subclass, x:Uid).</summary>
internal sealed class IgnoredDirectivesTransformer : IXamlAstTransformer
{
    private static readonly HashSet<string> s_ignored = new(StringComparer.Ordinal) { "ClassModifier", "FieldModifier", "Subclass", "Uid" };

    public IXamlAstNode Transform(AstTransformationContext context, IXamlAstNode node)
    {
        if (node is XamlAstObjectNode obj)
        {
            obj.Children.RemoveAll(c => c is XamlAstXmlDirective d && d.Namespace == XamlNamespaces.Xaml2006 && s_ignored.Contains(d.Name));
        }

        return node;
    }
}

/// <summary>
/// Implements <c>x:Name</c>: sets the element's <c>Name</c> property (when it has a settable string <c>Name</c>) and
/// registers the element in the name scope of the document root (<c>Nexer.Ui.Markup.NameScope</c>), which is what
/// code-behind fields are populated from. A plain <c>Name="..."</c> attribute on such an element registers too.
/// Must run after <see cref="TypeReferenceResolver"/> and before <see cref="PropertyReferenceResolver"/>.
/// </summary>
internal sealed class XNameTransformer : IXamlAstTransformer
{
    private readonly Dictionary<string, IXamlLineInfo> _names = new(StringComparer.Ordinal);
    private XamlAstObjectNode? _root;

    public IXamlAstNode Transform(AstTransformationContext context, IXamlAstNode node)
    {
        if (node is not XamlAstObjectNode obj || obj.Type is not XamlAstClrTypeReference clrType)
        {
            return node;
        }

        if (!context.ParentNodes().Any())
        {
            _root = obj;
        }

        var type = clrType.Type;
        var nameProperty = type.GetAllProperties().FirstOrDefault(p =>
            p.Name == "Name" && p.Setter is { IsPublic: true } && p.PropertyType.Equals(context.Configuration.WellKnownTypes.String));

        string? name = null;
        IXamlLineInfo? nameLine = null;
        for (var i = 0; i < obj.Children.Count; i++)
        {
            var child = obj.Children[i];
            if (child is XamlAstXmlDirective { Namespace: XamlNamespaces.Xaml2006, Name: "Name" } directive)
            {
                if (nameLine is not null)
                {
                    throw new NexerXamlException(NexerXamlDiagnosticCodes.InvalidDirective, "Both x:Name and Name are specified.", child);
                }

                name = XClassTransformer.GetDirectiveText(directive);
                nameLine = directive;
                if (nameProperty is not null)
                {
                    obj.Children[i] = new XamlAstXamlPropertyValueNode(directive,
                        new XamlAstNamePropertyReference(directive, clrType, "Name", clrType),
                        directive.Values, isAttributeSyntax: true);
                }
                else
                {
                    obj.Children.RemoveAt(i);
                    i--;
                }
            }
            else if (nameProperty is not null
                && child is XamlAstXamlPropertyValueNode
                {
                    Property: XamlAstNamePropertyReference { Name: "Name" } propertyReference,
                    Values: [XamlAstTextNode text],
                }
                && IsSameType(propertyReference.DeclaringType, clrType))
            {
                if (nameLine is not null)
                {
                    throw new NexerXamlException(NexerXamlDiagnosticCodes.InvalidDirective, "Both x:Name and Name are specified.", child);
                }

                name = text.Text;
                nameLine = child;
            }
        }

        if (name is null || nameLine is null)
        {
            return node;
        }

        if (_names.TryGetValue(name, out var previous))
        {
            throw new NexerXamlException(NexerXamlDiagnosticCodes.DuplicateName,
                $"The name '{name}' is already used in this document (line {previous.Line}).", nameLine);
        }

        _names.Add(name, nameLine);
        var nameScope = context.Configuration.TypeSystem.FindType(NexerXamlConfiguration.NameScopeTypeName);
        var register = nameScope?.FindMethod(m => m.Name == "RegisterXamlName" && m.IsStatic && m.Parameters.Count == 3);
        if (register is null || _root is null)
        {
            return node;
        }

        obj.Children.Add(new XamlNoReturnMethodCallNode(nameLine, register,
        [
            new XamlRootObjectNode(_root),
            new XamlConstantNode(nameLine, context.Configuration.WellKnownTypes.String, name),
        ]));
        return node;
    }

    private static bool IsSameType(IXamlAstTypeReference reference, XamlAstClrTypeReference type) =>
        ReferenceEquals(reference, type) || (reference is XamlAstClrTypeReference clr && clr.Type.Equals(type.Type));
}
