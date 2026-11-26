// CONTRACT: public surface of Nexer.Ui.Data.BindingExpression (see docs\binding.md). The body below is the binding
// spike's minimal implementation (source → target on attach/DataContext change, no subscriptions); the binding runtime
// agent replaces it. Public signatures may only change via the coordinator.
using System.Globalization;

namespace Nexer.Ui.Data;

/// <summary>An active binding between a widget property and a source path.</summary>
public sealed class BindingExpression : IDisposable
{
    private readonly Func<object, object?>? targetGetter;
    private readonly Action<object, object?> targetSetter;
    private bool disposed;

    internal BindingExpression(Widget target, string targetProperty, Func<object, object?>? targetGetter,
        Action<object, object?> targetSetter, Type targetType, IBinding binding)
    {
        Target = target;
        TargetProperty = targetProperty;
        this.targetGetter = targetGetter;
        this.targetSetter = targetSetter;
        TargetType = targetType;
        Binding = binding;
        EffectiveMode = binding.Mode == BindingMode.Default ? BindingMode.OneWay : binding.Mode;
        if (targetGetter is null && EffectiveMode is BindingMode.TwoWay or BindingMode.OneWayToSource)
        {
            throw new InvalidOperationException($"A {EffectiveMode} binding of '{targetProperty}' needs a target getter.");
        }
    }

    /// <summary>The target widget.</summary>
    public Widget Target { get; }

    /// <summary>The target property name.</summary>
    public string TargetProperty { get; }

    /// <summary>The declared type of the target property.</summary>
    public Type TargetType { get; }

    /// <summary>The binding.</summary>
    public IBinding Binding { get; }

    /// <summary>The resolved mode (<see cref="BindingMode.Default"/> resolves to <see cref="BindingMode.OneWay"/>).</summary>
    public BindingMode EffectiveMode { get; }

    /// <summary>The object the path currently starts from (DataContext, element, ancestor or the target), if resolved.</summary>
    public object? DataItem { get; private set; }

    /// <summary>Whether this expression currently has validation errors (see <see cref="Validation"/>).</summary>
    public bool HasError => Validation.GetErrors(Target).Any(e => ReferenceEquals(e.BindingExpression, this));

    /// <summary>Whether the expression has been disposed (cleared).</summary>
    public bool IsDisposed => disposed;

    /// <summary>Re-reads the source and writes the target.</summary>
    public void UpdateTarget()
    {
        if (disposed || EffectiveMode == BindingMode.OneWayToSource)
        {
            return;
        }

        DataItem = ResolveSource();
        var value = EvaluateCore(Binding, DataItem, TargetType);
        if (!ReferenceEquals(value, BindingOperations.DoNothing))
        {
            targetSetter(Target, value);
        }
    }

    /// <summary>Writes the current target value to the source (TwoWay/OneWayToSource; also with <see cref="UpdateSourceTrigger.Explicit"/>).</summary>
    public void UpdateSource()
    {
        if (disposed || targetGetter is null || EffectiveMode is not (BindingMode.TwoWay or BindingMode.OneWayToSource))
        {
            return;
        }

        throw new NotImplementedException("Target → source updates are implemented by the binding runtime.");
    }

    /// <summary>Detaches the expression from its source and target. Called by <see cref="BindingOperations.ClearBinding"/>.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Target.DataContextChanged -= OnDataContextChanged;
        Validation.ClearErrors(this);
    }

    internal void Attach()
    {
        Target.DataContextChanged += OnDataContextChanged;
        UpdateTarget();
    }

    private void OnDataContextChanged(object? sender, EventArgs e) => UpdateTarget();

    private object? ResolveSource()
    {
        var path = (Binding as CompiledBinding)?.Path;
        return path?.Source switch
        {
            BindingSourceKind.Self => Target,
            BindingSourceKind.DataContext or null => Target.DataContext,
            _ => null,
        };
    }

    internal static object? EvaluateCore(IBinding binding, object? source, Type targetType)
    {
        if (binding is not CompiledBinding compiled)
        {
            throw new NotSupportedException($"Binding type '{binding.GetType()}' is not supported.");
        }

        var value = source;
        var resolved = source is not null;
        foreach (var node in compiled.Path?.Nodes ?? [])
        {
            if (value is null)
            {
                resolved = false;
                break;
            }

            value = node.Get(value);
            if (ReferenceEquals(value, BindingOperations.UnsetValue))
            {
                resolved = false;
                break;
            }
        }

        if (!resolved)
        {
            return Fallback(compiled, targetType);
        }

        if (compiled.Converter is { } converter)
        {
            value = converter.Convert(value, targetType, compiled.ConverterParameter, CultureInfo.CurrentCulture);
            if (ReferenceEquals(value, BindingOperations.UnsetValue))
            {
                return Fallback(compiled, targetType);
            }

            if (ReferenceEquals(value, BindingOperations.DoNothing))
            {
                return value;
            }
        }

        if (value is null && !ReferenceEquals(compiled.TargetNullValue, BindingOperations.UnsetValue))
        {
            value = compiled.TargetNullValue;
        }

        return ConvertTo(value, targetType, compiled.StringFormat);
    }

    private static object? Fallback(CompiledBinding binding, Type targetType) =>
        ReferenceEquals(binding.FallbackValue, BindingOperations.UnsetValue)
            ? (targetType.IsValueType ? Activator.CreateInstance(targetType) : null)
            : ConvertTo(binding.FallbackValue, targetType, null);

    private static object? ConvertTo(object? value, Type targetType, string? format)
    {
        if (targetType == typeof(string))
        {
            if (value is null)
            {
                return null;
            }

            if (format is not null)
            {
                return format.Contains('{', StringComparison.Ordinal)
                    ? string.Format(CultureInfo.CurrentCulture, format, value)
                    : value is IFormattable formattable ? formattable.ToString(format, CultureInfo.CurrentCulture) : value.ToString();
            }

            return value as string ?? Convert.ToString(value, CultureInfo.CurrentCulture);
        }

        if (value is null || targetType.IsInstanceOfType(value))
        {
            return value;
        }

        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return underlying.IsEnum && value is string text
            ? Enum.Parse(underlying, text, ignoreCase: true)
            : Convert.ChangeType(value, underlying, CultureInfo.CurrentCulture);
    }
}
