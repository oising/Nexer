using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Nexer.Core;
using Nexer.Ui;
using XamlX.Ast;
using XamlX.Transform;
using XamlX.TypeSystem;

namespace Nexer.Xaml.Compiler;

/// <summary>
/// Compile-time value conversions for Nexer types (XamlX <c>CustomValueConverter</c>). Values are validated while
/// compiling, so typos are build errors (NXAML0002) instead of runtime exceptions, and most values are emitted as
/// constructor/factory calls instead of runtime parsing.
/// </summary>
/// <remarks>
/// Types are matched by name so the converter is independent of concrete control types:
/// <list type="bullet">
/// <item><c>Nexer.Core.CellStyle</c>: Jexer colour syntax via <see cref="StyleSyntax"/> → <c>new CellStyle(fg, bg, flags)</c>.</item>
/// <item><c>Nexer.Core.TerminalColor</c>: colour name/index/#RRGGBB → <c>TerminalColor.Default/Indexed/Rgb</c>.</item>
/// <item><c>Rect</c> (any <c>Nexer.*</c> namespace): <c>"x,y,w,h"</c> → 4-int constructor (else static <c>Parse</c>).</item>
/// <item><c>Thickness</c>: <c>"u"</c>, <c>"h,v"</c> or <c>"l,t,r,b"</c> → 4-int constructor (else static <c>Parse</c>).</item>
/// <item><c>KeyGesture</c>: syntax-checked (<c>Ctrl+Alt+Shift+Key</c>) then parsed at runtime by its static <c>Parse</c>.</item>
/// <item>Enums: case-insensitive names and comma-separated flags.</item>
/// </list>
/// Everything else falls through to XamlX's built-in conversions (primitives, static <c>Parse</c>, TypeConverter).
/// </remarks>
public static class NexerValueConverters
{
    private static readonly HashSet<string> s_modifiers = new(StringComparer.OrdinalIgnoreCase) { "Ctrl", "Control", "Alt", "Shift", "Meta" };

    /// <summary>The XamlX <c>CustomValueConverter</c> entry point.</summary>
    public static bool TryConvert(
        AstTransformationContext context,
        IXamlAstValueNode node,
        IReadOnlyList<IXamlCustomAttribute>? customAttributes,
        IXamlType type,
        [NotNullWhen(true)] out IXamlAstValueNode? result)
    {
        result = null;
        if (node is not XamlAstTextNode text)
        {
            return false;
        }

        if (type.IsEnum)
        {
            if (TypeSystemHelpers.TryGetEnumValueNode(type, text.Text, text, ignoreCase: true, out var enumNode))
            {
                result = enumNode;
                return true;
            }

            throw Invalid(text, type, "expected one of: " + string.Join(", ", type.Fields.Where(f => f.IsStatic && f.IsPublic).Select(f => f.Name)));
        }

        switch (type.FullName)
        {
            case "Nexer.Core.CellStyle":
                result = ConvertCellStyle(context, text, type);
                return true;
            case "Nexer.Core.TerminalColor":
                if (!StyleSyntax.TryParseColor(text.Text.Trim(), out var color))
                {
                    throw Invalid(text, type, "expected a colour name, 'default', an index (0-255) or #RRGGBB");
                }

                result = EmitColor(context, text, type, color);
                return true;
        }

        if (type.Namespace is not { } ns || !(ns == "Nexer" || ns.StartsWith("Nexer.", StringComparison.Ordinal)))
        {
            return false;
        }

        return type.Name switch
        {
            "Rect" => TryConvertInts(context, text, type, [4], ints => ints, "expected 'x,y,width,height'", out result),
            "Thickness" => TryConvertInts(context, text, type, [1, 2, 4], ExpandThickness, "expected 'uniform', 'horizontal,vertical' or 'left,top,right,bottom'", out result),
            "KeyGesture" => ValidateKeyGesture(text, type),
            _ => false,
        };
    }

    private static IXamlAstValueNode ConvertCellStyle(AstTransformationContext context, XamlAstTextNode text, IXamlType type)
    {
        if (!StyleSyntax.TryParse(text.Text, out var style, out var error))
        {
            throw Invalid(text, type, error);
        }

        var ctor = type.Constructors.FirstOrDefault(c => c.Parameters.Count == 3 && !c.IsStatic && c.IsPublic)
            ?? throw Invalid(text, type, "CellStyle has no (TerminalColor, TerminalColor, CellStyleFlags) constructor");
        var colorType = ctor.Parameters[0];
        var flagsType = ctor.Parameters[2];
        var flagsValue = ConvertFlags(flagsType, style.Flags);
        return new XamlAstNewClrObjectNode(text, new XamlAstClrTypeReference(text, type, false), ctor,
        [
            EmitColor(context, text, colorType, style.Foreground),
            EmitColor(context, text, colorType, style.Background),
            new XamlConstantNode(text, flagsType, flagsValue),
        ]);
    }

    private static object ConvertFlags(IXamlType flagsType, CellStyleFlags flags)
    {
        var underlying = flagsType.IsEnum ? flagsType.GetEnumUnderlyingType().Name : "UInt16";
        return underlying switch
        {
            "Byte" => (byte)flags,
            "UInt16" => (ushort)flags,
            "Int16" => (short)flags,
            "UInt32" => (uint)flags,
            "Int64" => (long)flags,
            _ => (int)flags,
        };
    }

    private static IXamlAstValueNode EmitColor(AstTransformationContext context, IXamlLineInfo line, IXamlType colorType, TerminalColor color)
    {
        var byteType = context.Configuration.TypeSystem.GetType("System.Byte");
        IXamlMethod Factory(string name, int arity) => colorType.Methods.FirstOrDefault(m => m.Name == name && m.IsStatic && m.IsPublic && m.Parameters.Count == arity)
            ?? throw new NexerXamlException(NexerXamlDiagnosticCodes.InvalidValue, $"TerminalColor has no static '{name}' member.", line);
        XamlConstantNode Byte(uint value) => new(line, byteType, (byte)value);

        return color.Kind switch
        {
            TerminalColorKind.Indexed => new XamlStaticOrTargetedReturnMethodCallNode(line, Factory("Indexed", 1), [Byte(color.Value)]),
            TerminalColorKind.Rgb => new XamlStaticOrTargetedReturnMethodCallNode(line, Factory("Rgb", 3),
                [Byte((color.Value >> 16) & 0xFF), Byte((color.Value >> 8) & 0xFF), Byte(color.Value & 0xFF)]),
            _ => new XamlStaticOrTargetedReturnMethodCallNode(line, Factory("get_Default", 0), []),
        };
    }

    private static int[] ExpandThickness(int[] values) => values.Length switch
    {
        1 => [values[0], values[0], values[0], values[0]],
        2 => [values[0], values[1], values[0], values[1]],
        _ => values,
    };

    private static bool TryConvertInts(
        AstTransformationContext context,
        XamlAstTextNode text,
        IXamlType type,
        int[] allowedCounts,
        Func<int[], int[]> expand,
        string expected,
        out IXamlAstValueNode? result)
    {
        result = null;
        var parts = text.Text.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var values = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
            {
                throw Invalid(text, type, expected);
            }
        }

        if (!allowedCounts.Contains(values.Length))
        {
            throw Invalid(text, type, expected);
        }

        var int32 = context.Configuration.WellKnownTypes.Int32;
        var ctor = type.Constructors.FirstOrDefault(c => !c.IsStatic && c.IsPublic && c.Parameters.Count == 4 && c.Parameters.All(p => p.Equals(int32)));
        if (ctor is null)
        {
            // Validated; let XamlX fall back to the type's static Parse method.
            return false;
        }

        result = new XamlAstNewClrObjectNode(text, new XamlAstClrTypeReference(text, type, false), ctor,
            expand(values).Select(v => (IXamlAstValueNode)new XamlConstantNode(text, int32, v)).ToList());
        return true;
    }

    private static bool ValidateKeyGesture(XamlAstTextNode text, IXamlType type)
    {
        var value = text.Text.Trim();
        var plus = value.Length > 1 && value.EndsWith("++", StringComparison.Ordinal);
        var parts = (plus ? value[..^2] : value).Split('+', StringSplitOptions.TrimEntries);
        var key = plus ? "+" : parts[^1];
        var modifiers = plus ? parts.Where(p => p.Length > 0) : parts[..^1];
        if (key.Length == 0 || modifiers.Any(m => !s_modifiers.Contains(m)))
        {
            throw Invalid(text, type, "expected '[Ctrl+][Alt+][Shift+]Key', e.g. 'Ctrl+Q' or 'F10'");
        }

        // Syntax is valid; the type's static Parse method builds the value at runtime.
        return false;
    }

    private static NexerXamlException Invalid(IXamlLineInfo line, IXamlType type, string detail) =>
        new(NexerXamlDiagnosticCodes.InvalidValue,
            $"Invalid {type.Name} value '{(line as XamlAstTextNode)?.Text}': {detail.TrimEnd('.')}.", line);
}
