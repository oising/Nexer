// CONTRACT: Nexer.Ui.Data public surface (bindings). See docs\binding.md. Additive changes only, via the coordinator.
using System.Globalization;

namespace Nexer.Ui.Data;

/// <summary>
/// A binding description that can be attached to a widget property with
/// <see cref="BindingOperations.Bind(Widget, string, Func{object, object?}?, Action{object, object?}, Type, IBinding)"/>.
/// </summary>
/// <remarks>
/// In v1 the only implementation is <see cref="CompiledBinding"/> (produced by the XAML compiler for <c>{Binding}</c>,
/// or built in code). The interface exists so properties such as <c>DisplayMemberBinding</c> can be typed independently
/// of the implementation.
/// </remarks>
public interface IBinding
{
    /// <summary>The binding direction (<see cref="BindingMode.Default"/> resolves to <see cref="BindingMode.OneWay"/> at runtime).</summary>
    BindingMode Mode { get; }
}

/// <summary>Direction of the data flow of a binding.</summary>
public enum BindingMode
{
    /// <summary>
    /// The default for the target property. The XAML compiler resolves it to <see cref="TwoWay"/> for properties marked
    /// <see cref="BindsTwoWayByDefaultAttribute"/>; at runtime an unresolved <c>Default</c> means <see cref="OneWay"/>.
    /// </summary>
    Default,

    /// <summary>Source → target, updated whenever the source changes.</summary>
    OneWay,

    /// <summary>Source → target and target → source.</summary>
    TwoWay,

    /// <summary>Source → target once, when the binding first resolves a source (and again on DataContext change).</summary>
    OneTime,

    /// <summary>Target → source only.</summary>
    OneWayToSource,
}

/// <summary>When a target → source update happens for <see cref="BindingMode.TwoWay"/> and <see cref="BindingMode.OneWayToSource"/>.</summary>
public enum UpdateSourceTrigger
{
    /// <summary><see cref="PropertyChanged"/> (every widget in v1).</summary>
    Default,

    /// <summary>Whenever the target raises <c>PropertyChanged</c> for the bound property.</summary>
    PropertyChanged,

    /// <summary>When the target loses focus (only supported by <c>Field</c>; other widgets treat it as <see cref="PropertyChanged"/>).</summary>
    LostFocus,

    /// <summary>Only when <see cref="BindingExpression.UpdateSource"/> is called.</summary>
    Explicit,
}

/// <summary>Converts values between a binding source and its target.</summary>
public interface IValueConverter
{
    /// <summary>Converts a source value for the target.</summary>
    /// <param name="value">The source value.</param>
    /// <param name="targetType">The type of the target property.</param>
    /// <param name="parameter">The binding's <c>ConverterParameter</c>.</param>
    /// <param name="culture">The culture (<see cref="CultureInfo.CurrentCulture"/>).</param>
    /// <returns>
    /// The converted value, <see cref="BindingOperations.UnsetValue"/> to use the fallback value, or
    /// <see cref="BindingOperations.DoNothing"/> to leave the target unchanged.
    /// </returns>
    object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture);

    /// <summary>Converts a target value back for the source.</summary>
    /// <param name="value">The target value.</param>
    /// <param name="targetType">The type of the source property.</param>
    /// <param name="parameter">The binding's <c>ConverterParameter</c>.</param>
    /// <param name="culture">The culture (<see cref="CultureInfo.CurrentCulture"/>).</param>
    /// <returns>The converted value, or <see cref="BindingOperations.DoNothing"/> to leave the source unchanged.</returns>
    object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture);
}

/// <summary>
/// Marks a widget property whose <c>{Binding}</c> defaults to <see cref="BindingMode.TwoWay"/>. Read by the XAML compiler.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class BindsTwoWayByDefaultAttribute : Attribute
{
}

/// <summary>Where a binding path starts.</summary>
public enum BindingSourceKind
{
    /// <summary>The target widget's (inherited) <see cref="Widget.DataContext"/>.</summary>
    DataContext,

    /// <summary>The target widget itself (<c>{RelativeSource Self}</c>).</summary>
    Self,

    /// <summary>A named element of the target's XAML name scope (<c>ElementName=…</c>).</summary>
    ElementName,

    /// <summary>The <c>AncestorLevel</c>-th ancestor of the target of type <c>AncestorType</c>.</summary>
    FindAncestor,
}

/// <summary>Kinds of <see cref="RelativeSource"/>.</summary>
public enum RelativeSourceMode
{
    /// <summary>The binding target itself.</summary>
    Self,

    /// <summary>An ancestor of the binding target (<see cref="RelativeSource.AncestorType"/>, <see cref="RelativeSource.AncestorLevel"/>).</summary>
    FindAncestor,
}
