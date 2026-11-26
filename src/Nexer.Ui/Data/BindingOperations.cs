// CONTRACT: public surface of Nexer.Ui.Data.BindingOperations (see docs\binding.md). The implementation is owned by
// the binding runtime agent; signatures may only change via the coordinator.
using System.Runtime.CompilerServices;

namespace Nexer.Ui.Data;

/// <summary>Attaches, finds and removes bindings on widgets.</summary>
public static class BindingOperations
{
    private static readonly ConditionalWeakTable<Widget, Dictionary<string, BindingExpression>> s_expressions = new();

    /// <summary>
    /// Sentinel meaning "no value": the default of <see cref="CompiledBinding.FallbackValue"/> and
    /// <see cref="CompiledBinding.TargetNullValue"/>; a converter returning it makes the binding use its fallback.
    /// </summary>
    public static object UnsetValue { get; } = new Sentinel(nameof(UnsetValue));

    /// <summary>Sentinel a converter returns to leave the other side of the binding unchanged.</summary>
    public static object DoNothing { get; } = new Sentinel(nameof(DoNothing));

    /// <summary>
    /// Binds <paramref name="propertyName"/> of <paramref name="target"/>. This is the method compiled XAML calls for
    /// every <c>{Binding}</c>. Any existing binding of the property is cleared first.
    /// </summary>
    /// <param name="target">The target widget.</param>
    /// <param name="propertyName">The target property name (also the name the target raises in <c>PropertyChanged</c>).</param>
    /// <param name="targetGetter">
    /// Reads the target property (<c>(object target) => ((T)target).Prop</c>); required for
    /// <see cref="BindingMode.TwoWay"/> and <see cref="BindingMode.OneWayToSource"/>, may be <see langword="null"/> otherwise.
    /// </param>
    /// <param name="targetSetter">Writes the target property (<c>(object target, object? value) => ((T)target).Prop = (TProp)value</c>).</param>
    /// <param name="targetType">The declared type of the target property (values are converted to it).</param>
    /// <param name="binding">The binding.</param>
    /// <returns>The active expression.</returns>
    public static BindingExpression Bind(
        Widget target,
        string propertyName,
        Func<object, object?>? targetGetter,
        Action<object, object?> targetSetter,
        Type targetType,
        IBinding binding)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(propertyName);
        ArgumentNullException.ThrowIfNull(targetSetter);
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(binding);
        ClearBinding(target, propertyName);
        var expression = new BindingExpression(target, propertyName, targetGetter, targetSetter, targetType, binding);
        s_expressions.GetOrCreateValue(target)[propertyName] = expression;
        expression.Attach();
        return expression;
    }

    /// <summary>
    /// Binds <paramref name="propertyName"/> without a target getter (source → target modes only; TwoWay and
    /// OneWayToSource bindings throw <see cref="InvalidOperationException"/>).
    /// </summary>
    public static BindingExpression Bind(Widget target, string propertyName, Action<object, object?> targetSetter, Type targetType, IBinding binding) =>
        Bind(target, propertyName, null, targetSetter, targetType, binding);

    /// <summary>Returns the binding of <paramref name="propertyName"/> on <paramref name="target"/>, if any.</summary>
    public static BindingExpression? GetBindingExpression(Widget target, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(propertyName);
        return s_expressions.TryGetValue(target, out var map) && map.TryGetValue(propertyName, out var e) ? e : null;
    }

    /// <summary>Returns every binding on <paramref name="target"/>.</summary>
    public static IReadOnlyList<BindingExpression> GetBindingExpressions(Widget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return s_expressions.TryGetValue(target, out var map) ? [.. map.Values] : [];
    }

    /// <summary>Removes (and disposes) the binding of <paramref name="propertyName"/>; the property keeps its current value.</summary>
    public static void ClearBinding(Widget target, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(propertyName);
        if (s_expressions.TryGetValue(target, out var map) && map.Remove(propertyName, out var expression))
        {
            expression.Dispose();
        }
    }

    /// <summary>Removes every binding of <paramref name="target"/>.</summary>
    public static void ClearAllBindings(Widget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (s_expressions.TryGetValue(target, out var map))
        {
            var all = map.Values.ToList();
            map.Clear();
            foreach (var expression in all)
            {
                expression.Dispose();
            }
        }
    }

    /// <summary>
    /// Evaluates <paramref name="binding"/> once against <paramref name="source"/> (path, converter, null/fallback values,
    /// <c>StringFormat</c> and conversion to <paramref name="targetType"/>) without subscribing to anything. Used for
    /// per-item bindings such as <c>DisplayMemberBinding</c> and <c>ChildrenBinding</c>. The path's source kind is ignored:
    /// <paramref name="source"/> is the start of the path.
    /// </summary>
    public static object? Evaluate(IBinding binding, object? source, Type targetType)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(targetType);
        return BindingExpression.EvaluateCore(binding, source, targetType);
    }

    private sealed class Sentinel(string name)
    {
        public override string ToString() => "{" + name + "}";
    }
}
