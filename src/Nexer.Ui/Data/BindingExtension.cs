// CONTRACT: Nexer.Ui.Data public surface (bindings). See docs\binding.md. Additive changes only, via the coordinator.
namespace Nexer.Ui.Data;

/// <summary>
/// The XAML-facing <c>{Binding}</c> markup. It exists only so the XAML compiler can resolve the syntax: compiled XAML
/// never instantiates it. The compiler replaces it with a <see cref="CompiledBinding"/> whose path is resolved against
/// the <c>x:DataType</c> in scope (or the target, named element or ancestor type), and attaches it to the target
/// property with <see cref="BindingOperations.Bind(Widget, string, Func{object, object?}?, Action{object, object?}, Type, IBinding)"/>.
/// </summary>
/// <remarks>Deliberately has no <c>ProvideValue</c>: it must never be treated as a runtime markup extension.</remarks>
public sealed class BindingExtension
{
    /// <summary>Creates a binding to the source itself (<c>{Binding}</c>).</summary>
    public BindingExtension()
    {
    }

    /// <summary>Creates a binding with a path (<c>{Binding Customer.Name}</c>).</summary>
    public BindingExtension(string path)
    {
        Path = path;
    }

    /// <summary>The member path (dotted properties, <c>[int]</c>/<c>["key"]</c> indexers). Empty means the source itself.</summary>
    public string? Path { get; set; }

    /// <summary>The binding direction (<see cref="BindingMode.Default"/> uses the target property's default).</summary>
    public BindingMode Mode { get; set; }

    /// <summary>Converter between source and target values.</summary>
    public IValueConverter? Converter { get; set; }

    /// <summary>Parameter passed to <see cref="Converter"/>.</summary>
    public object? ConverterParameter { get; set; }

    /// <summary>Format applied for <see cref="string"/> targets.</summary>
    public string? StringFormat { get; set; }

    /// <summary>Value used when the path cannot be resolved.</summary>
    public object? FallbackValue { get; set; }

    /// <summary>Value used when the source value is <see langword="null"/>.</summary>
    public object? TargetNullValue { get; set; }

    /// <summary>When target → source updates happen.</summary>
    public UpdateSourceTrigger UpdateSourceTrigger { get; set; }

    /// <summary>Whether exceptions while updating the source become validation errors (default <see langword="true"/>).</summary>
    public bool ValidatesOnExceptions { get; set; } = true;

    /// <summary>Whether <see cref="System.ComponentModel.INotifyDataErrorInfo"/> errors become validation errors (default <see langword="true"/>).</summary>
    public bool ValidatesOnNotifyDataErrors { get; set; } = true;

    /// <summary>Binds to a named element of the document instead of the DataContext. The path resolves against the element's type.</summary>
    public string? ElementName { get; set; }

    /// <summary>Binds relative to the target (<c>{RelativeSource Self}</c>, <c>{RelativeSource FindAncestor, AncestorType=…}</c>).</summary>
    public RelativeSource? RelativeSource { get; set; }

    /// <summary>
    /// The type the path is resolved against when the source is the DataContext, overriding the <c>x:DataType</c> in
    /// scope (e.g. per-item bindings such as <c>DisplayMemberBinding</c>).
    /// </summary>
    public Type? DataType { get; set; }
}

/// <summary>
/// A binding source relative to the binding target. In XAML: <c>{RelativeSource Self}</c> or
/// <c>{RelativeSource FindAncestor, AncestorType=local:MyWindow, AncestorLevel=1}</c>. Like
/// <see cref="BindingExtension"/>, it is consumed by the XAML compiler (no <c>ProvideValue</c>).
/// </summary>
public sealed class RelativeSource
{
    /// <summary>Creates a <see cref="RelativeSourceMode.Self"/> relative source.</summary>
    public RelativeSource()
    {
    }

    /// <summary>Creates a relative source of the given mode.</summary>
    public RelativeSource(RelativeSourceMode mode)
    {
        Mode = mode;
    }

    /// <summary>The mode.</summary>
    public RelativeSourceMode Mode { get; set; }

    /// <summary>The ancestor type (<see cref="RelativeSourceMode.FindAncestor"/>).</summary>
    public Type? AncestorType { get; set; }

    /// <summary>The 1-based ancestor level (<see cref="RelativeSourceMode.FindAncestor"/>; default 1).</summary>
    public int AncestorLevel { get; set; } = 1;
}
