// CONTRACT: public surface of Nexer.Ui.Data.BindingExpression (see docs\binding.md). Public signatures may only change
// via the coordinator; the private implementation below is owned by the binding runtime agent.
using System.ComponentModel;
using System.Globalization;

namespace Nexer.Ui.Data;

/// <summary>An active binding between a widget property and a source path.</summary>
/// <remarks>
/// Subscriptions to the source (<see cref="INotifyPropertyChanged"/> on every node owner,
/// <see cref="INotifyDataErrorInfo.ErrorsChanged"/> on the leaf owner) and to the target's
/// <see cref="Widget.PropertyChanged"/> exist only while <see cref="Widget.IsAttachedToTree"/> is true, so a long-lived
/// source never keeps a detached widget alive. All widget access happens on the UI thread: source notifications raised
/// on another thread are marshalled with <see cref="Application.Post"/>.
/// </remarks>
public sealed class BindingExpression : IDisposable
{
    private readonly Func<object, object?>? targetGetter;
    private readonly Action<object, object?> targetSetter;
    private readonly CompiledBinding binding;
    private readonly PathNode[] nodes;

    // owners[i] is the object node i is read from (owners[0] = DataItem); null when unresolved.
    private readonly object?[] owners;

    // subscribed[i] is the INotifyPropertyChanged owner of node i we listen to (only while active).
    private readonly INotifyPropertyChanged?[] subscribed;
    private readonly PropertyChangedEventHandler sourceHandler;
    private readonly EventHandler<DataErrorsChangedEventArgs> errorsHandler;
    private readonly PropertyChangedEventHandler targetHandler;
    private readonly EventHandler lostFocusHandler;

    private INotifyDataErrorInfo? errorSource;
    private ILostFocusSource? lostFocusSource;
    private object? leafValue;
    private bool leafResolved;
    private bool active;
    private bool disposed;
    private bool updatingTarget;
    private bool updatingSource;
    private bool oneTimeDone;
    private object? oneTimeSource;
    private int marshalPending;
    private ValidationError? exceptionError;
    private IReadOnlyList<ValidationError> dataErrors = [];

    internal BindingExpression(Widget target, string targetProperty, Func<object, object?>? targetGetter,
        Action<object, object?> targetSetter, Type targetType, IBinding binding)
    {
        this.binding = binding as CompiledBinding
            ?? throw new NotSupportedException($"Binding type '{binding.GetType()}' is not supported.");
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

        nodes = [.. this.binding.Path?.Nodes ?? []];
        owners = new object?[nodes.Length];
        subscribed = new INotifyPropertyChanged?[nodes.Length];
        sourceHandler = OnSourcePropertyChanged;
        errorsHandler = OnSourceErrorsChanged;
        targetHandler = OnTargetPropertyChanged;
        lostFocusHandler = OnTargetLostFocus;
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

    private bool ListensToSource => EffectiveMode is BindingMode.OneWay or BindingMode.TwoWay or BindingMode.OneWayToSource;

    private bool WritesSource => EffectiveMode is BindingMode.TwoWay or BindingMode.OneWayToSource;

    /// <summary>Re-reads the source and writes the target.</summary>
    public void UpdateTarget()
    {
        if (disposed || EffectiveMode == BindingMode.OneWayToSource)
        {
            return;
        }

        Resolve();
        TransferToTarget();
    }

    /// <summary>Writes the current target value to the source (TwoWay/OneWayToSource; also with <see cref="UpdateSourceTrigger.Explicit"/>).</summary>
    public void UpdateSource()
    {
        if (disposed || targetGetter is null || !WritesSource)
        {
            return;
        }

        if (!active)
        {
            Resolve();
        }

        TransferToSource();
    }

    /// <summary>Detaches the expression from its source and target. Called by <see cref="BindingOperations.ClearBinding"/>.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Deactivate();
        disposed = true;
        Target.DataContextChanged -= OnDataContextChanged;
        Target.AttachedToTree -= OnAttachedToTree;
        Target.DetachedFromTree -= OnDetachedFromTree;
        Array.Clear(owners);
        DataItem = null;
        leafValue = null;
        oneTimeSource = null;
        Validation.ClearErrors(this);
    }

    /// <summary>Called by <see cref="BindingOperations.Bind(Widget, string, Func{object, object?}?, Action{object, object?}, Type, IBinding)"/> once the expression is registered.</summary>
    internal void Attach()
    {
        Target.DataContextChanged += OnDataContextChanged;
        Target.AttachedToTree += OnAttachedToTree;
        Target.DetachedFromTree += OnDetachedFromTree;
        if (Target.IsAttachedToTree)
        {
            Activate();
            return;
        }

        // Not attached yet (compiled XAML binds before adding the widget to its parent): evaluate once if a source exists.
        Resolve();
        if (DataItem is not null)
        {
            Transfer();
        }
    }

    // ---- lifecycle ----

    private void OnAttachedToTree(object? sender, EventArgs e) => Activate();

    private void OnDetachedFromTree(object? sender, EventArgs e) => Deactivate();

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (disposed)
        {
            return;
        }

        Resolve();
        Transfer();
    }

    private void Activate()
    {
        if (disposed || active)
        {
            return;
        }

        active = true;
        if (WritesSource)
        {
            Target.PropertyChanged += targetHandler;
            if (binding.UpdateSourceTrigger == UpdateSourceTrigger.LostFocus && Target is ILostFocusSource focusSource)
            {
                lostFocusSource = focusSource;
                focusSource.LostFocus += lostFocusHandler;
            }
        }

        Resolve();
        Transfer();
    }

    private void Deactivate()
    {
        if (!active)
        {
            return;
        }

        active = false;
        Target.PropertyChanged -= targetHandler;
        if (lostFocusSource is not null)
        {
            lostFocusSource.LostFocus -= lostFocusHandler;
            lostFocusSource = null;
        }

        for (var i = 0; i < subscribed.Length; i++)
        {
            Unsubscribe(i);
        }

        SetErrorSource(null);
    }

    /// <summary>Pushes the value in the direction of the mode after the source was (re)resolved.</summary>
    private void Transfer()
    {
        if (EffectiveMode == BindingMode.OneWayToSource)
        {
            TransferToSource();
        }
        else
        {
            TransferToTarget();
        }
    }

    // ---- source resolution and path walking ----

    private void Resolve()
    {
        DataItem = ResolveSource();
        Walk(0);
    }

    private object? ResolveSource()
    {
        var path = binding.Path;
        switch (path?.Source ?? BindingSourceKind.DataContext)
        {
            case BindingSourceKind.Self:
                return Target;
            case BindingSourceKind.ElementName:
                for (Widget? w = Target; w is not null; w = w.Parent)
                {
                    if (Markup.NameScope.GetNameScope(w)?.Find(path!.ElementName!) is { } element)
                    {
                        return element;
                    }
                }

                return null;
            case BindingSourceKind.FindAncestor:
                var level = path!.AncestorLevel;
                for (var w = Target.Parent; w is not null; w = w.Parent)
                {
                    if (path.AncestorType!.IsInstanceOfType(w) && --level == 0)
                    {
                        return w;
                    }
                }

                return null;
            default:
                return Target.DataContext;
        }
    }

    /// <summary>Re-reads the path from node <paramref name="from"/> downwards, (re)subscribing the node owners while active.</summary>
    private void Walk(int from)
    {
        var subscribe = active && ListensToSource;
        object? value = DataItem;
        var resolved = DataItem is not null;
        if (from > 0)
        {
            value = owners[from];
            resolved = value is not null;
        }

        for (var i = from; i < nodes.Length; i++)
        {
            if (!resolved)
            {
                owners[i] = null;
                Unsubscribe(i);
                continue;
            }

            owners[i] = value;
            if (subscribe)
            {
                Subscribe(i, value as INotifyPropertyChanged);
            }

            try
            {
                value = nodes[i].Get(value!);
            }
            catch (Exception)
            {
                value = BindingOperations.UnsetValue;
            }

            if (ReferenceEquals(value, BindingOperations.UnsetValue))
            {
                value = null;
                resolved = false;
            }
            else if (value is null && i < nodes.Length - 1)
            {
                resolved = false;
            }
        }

        leafResolved = resolved;
        leafValue = resolved ? value : null;
        SetErrorSource(active && resolved && nodes.Length > 0 && binding.ValidatesOnNotifyDataErrors
            ? owners[^1] as INotifyDataErrorInfo
            : null);
        RefreshDataErrors();
    }

    private void Subscribe(int index, INotifyPropertyChanged? owner)
    {
        if (ReferenceEquals(subscribed[index], owner))
        {
            return;
        }

        Unsubscribe(index);
        if (owner is null)
        {
            return;
        }

        var alreadyListening = Array.Exists(subscribed, s => ReferenceEquals(s, owner));
        subscribed[index] = owner;
        if (!alreadyListening)
        {
            owner.PropertyChanged += sourceHandler;
        }
    }

    private void Unsubscribe(int index)
    {
        var owner = subscribed[index];
        if (owner is null)
        {
            return;
        }

        subscribed[index] = null;
        if (!Array.Exists(subscribed, s => ReferenceEquals(s, owner)))
        {
            owner.PropertyChanged -= sourceHandler;
        }
    }

    private void SetErrorSource(INotifyDataErrorInfo? source)
    {
        if (ReferenceEquals(errorSource, source))
        {
            return;
        }

        if (errorSource is not null)
        {
            errorSource.ErrorsChanged -= errorsHandler;
        }

        errorSource = source;
        if (source is not null)
        {
            source.ErrorsChanged += errorsHandler;
        }
    }

    // ---- notifications ----

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!active || updatingSource)
        {
            return;
        }

        if (!OnUiThread())
        {
            MarshalRefresh();
            return;
        }

        var name = e.PropertyName;
        for (var i = 0; i < nodes.Length; i++)
        {
            if (ReferenceEquals(subscribed[i], sender) && (string.IsNullOrEmpty(name) || name == nodes[i].Name))
            {
                Walk(i);
                if (EffectiveMode != BindingMode.OneWayToSource)
                {
                    TransferToTarget();
                }

                return;
            }
        }
    }

    private void OnSourceErrorsChanged(object? sender, DataErrorsChangedEventArgs e)
    {
        if (!active || !ReferenceEquals(sender, errorSource))
        {
            return;
        }

        if (!OnUiThread())
        {
            MarshalRefresh();
            return;
        }

        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nodes[^1].Name)
        {
            RefreshDataErrors();
        }
    }

    private void OnTargetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (updatingTarget || e.PropertyName != TargetProperty)
        {
            return;
        }

        var trigger = binding.UpdateSourceTrigger;
        if (trigger == UpdateSourceTrigger.Explicit || (trigger == UpdateSourceTrigger.LostFocus && lostFocusSource is not null))
        {
            return;
        }

        TransferToSource();
    }

    private void OnTargetLostFocus(object? sender, EventArgs e) => TransferToSource();

    private bool OnUiThread() => (Target.Application ?? Application.Current)?.CheckAccess() ?? true;

    /// <summary>Queues a full refresh on the UI thread, collapsing notifications that arrive before it runs.</summary>
    private void MarshalRefresh()
    {
        var app = Target.Application ?? Application.Current;
        if (app is null || Interlocked.Exchange(ref marshalPending, 1) == 1)
        {
            return;
        }

        app.Post(() =>
        {
            Volatile.Write(ref marshalPending, 0);
            if (!active || disposed)
            {
                return;
            }

            Walk(0);
            if (EffectiveMode != BindingMode.OneWayToSource)
            {
                TransferToTarget();
            }
        });
    }

    // ---- transfers ----

    private void TransferToTarget()
    {
        if (disposed || EffectiveMode == BindingMode.OneWayToSource)
        {
            return;
        }

        if (EffectiveMode == BindingMode.OneTime)
        {
            if (oneTimeDone && ReferenceEquals(oneTimeSource, DataItem))
            {
                return;
            }

            oneTimeDone = true;
            oneTimeSource = DataItem;
        }

        var value = ConvertForTarget(binding, leafResolved, leafValue, TargetType);
        if (ReferenceEquals(value, BindingOperations.DoNothing))
        {
            return;
        }

        updatingTarget = true;
        try
        {
            targetSetter(Target, value);
        }
        finally
        {
            updatingTarget = false;
        }

        // The target now shows the source value again: a stale conversion/setter error no longer applies.
        if (exceptionError is not null)
        {
            SetExceptionError(null);
        }
    }

    private void TransferToSource()
    {
        if (disposed || targetGetter is null || !WritesSource || nodes.Length == 0 || updatingSource)
        {
            return;
        }

        var leaf = nodes[^1];
        var owner = owners[^1];
        if (owner is null || leaf.Set is null)
        {
            return;
        }

        object? value;
        try
        {
            value = targetGetter(Target);
            if (binding.Converter is { } converter)
            {
                value = converter.ConvertBack(value, leaf.ValueType, binding.ConverterParameter, CultureInfo.CurrentCulture);
                if (ReferenceEquals(value, BindingOperations.DoNothing) || ReferenceEquals(value, BindingOperations.UnsetValue))
                {
                    return;
                }
            }

            value = ConvertValue(value, leaf.ValueType);
            updatingSource = true;
            try
            {
                leaf.Set(owner, value);
            }
            finally
            {
                updatingSource = false;
            }
        }
        catch (Exception ex)
        {
            SetExceptionError(binding.ValidatesOnExceptions ? new ValidationError(ex.Message, ex, this) : null);
            return;
        }

        exceptionError = null;
        RefreshDataErrors();
    }

    // ---- validation ----

    private void SetExceptionError(ValidationError? error)
    {
        exceptionError = error;
        PublishErrors();
    }

    /// <summary>Mirrors the leaf owner's <see cref="INotifyDataErrorInfo"/> errors for the leaf property.</summary>
    private void RefreshDataErrors()
    {
        var errors = new List<ValidationError>();
        if (binding.ValidatesOnNotifyDataErrors && leafResolved && nodes.Length > 0 && owners[^1] is INotifyDataErrorInfo source)
        {
            try
            {
                foreach (var error in source.GetErrors(nodes[^1].Name) ?? Array.Empty<object>())
                {
                    if (error is not null)
                    {
                        errors.Add(new ValidationError(error, null, this));
                    }
                }
            }
            catch (Exception)
            {
                // A faulty INotifyDataErrorInfo must not break the binding.
            }
        }

        dataErrors = errors;
        PublishErrors();
    }

    /// <summary>Publishes the exception error (first) and the data errors; <see cref="Validation.SetErrors"/> ignores unchanged sets.</summary>
    private void PublishErrors()
    {
        if (!disposed)
        {
            Validation.SetErrors(this, exceptionError is null ? dataErrors : dataErrors.Prepend(exceptionError));
        }
    }

    // ---- conversion pipeline (shared with BindingOperations.Evaluate) ----

    internal static object? EvaluateCore(IBinding binding, object? source, Type targetType)
    {
        if (binding is not CompiledBinding compiled)
        {
            throw new NotSupportedException($"Binding type '{binding.GetType()}' is not supported.");
        }

        var value = source;
        var resolved = source is not null;
        var path = compiled.Path?.Nodes ?? [];
        for (var i = 0; i < path.Count && resolved; i++)
        {
            try
            {
                value = path[i].Get(value!);
            }
            catch (Exception)
            {
                value = BindingOperations.UnsetValue;
            }

            if (ReferenceEquals(value, BindingOperations.UnsetValue) || (value is null && i < path.Count - 1))
            {
                resolved = false;
                value = null;
            }
        }

        return ConvertForTarget(compiled, resolved, value, targetType);
    }

    /// <summary>
    /// Source → target pipeline: unresolved → fallback; <c>null</c> → <c>TargetNullValue</c> (bypassing the converter);
    /// converter; <c>StringFormat</c> (string targets); conversion. Returns <see cref="BindingOperations.DoNothing"/>
    /// when the target must be left unchanged. Never throws (failures use the fallback).
    /// </summary>
    private static object? ConvertForTarget(CompiledBinding binding, bool resolved, object? value, Type targetType)
    {
        if (!resolved)
        {
            return Fallback(binding, targetType);
        }

        try
        {
            if (value is null && !ReferenceEquals(binding.TargetNullValue, BindingOperations.UnsetValue))
            {
                return ConvertValue(binding.TargetNullValue, targetType);
            }

            if (binding.Converter is { } converter)
            {
                value = converter.Convert(value, targetType, binding.ConverterParameter, CultureInfo.CurrentCulture);
                if (ReferenceEquals(value, BindingOperations.UnsetValue))
                {
                    return Fallback(binding, targetType);
                }

                if (ReferenceEquals(value, BindingOperations.DoNothing))
                {
                    return value;
                }
            }

            if (targetType == typeof(string) && binding.StringFormat is { } format && value is not null)
            {
                var composite = format.Contains('{', StringComparison.Ordinal) ? format : "{0:" + format + "}";
                return string.Format(CultureInfo.CurrentCulture, composite, value);
            }

            return ConvertValue(value, targetType);
        }
        catch (Exception)
        {
            return Fallback(binding, targetType);
        }
    }

    private static object? Fallback(CompiledBinding binding, Type targetType)
    {
        if (!ReferenceEquals(binding.FallbackValue, BindingOperations.UnsetValue))
        {
            try
            {
                return ConvertValue(binding.FallbackValue, targetType);
            }
            catch (Exception)
            {
                // An unconvertible fallback falls through to the type's default.
            }
        }

        return DefaultOf(targetType);
    }

    private static object? DefaultOf(Type type) =>
        type.IsValueType && Nullable.GetUnderlyingType(type) is null ? Activator.CreateInstance(type) : null;

    /// <summary>
    /// Converts <paramref name="value"/> to <paramref name="targetType"/> using <see cref="CultureInfo.CurrentCulture"/>
    /// (both directions). Throws <see cref="FormatException"/>/<see cref="InvalidCastException"/>/<see cref="OverflowException"/>
    /// when the value cannot be converted.
    /// </summary>
    internal static object? ConvertValue(object? value, Type targetType)
    {
        if (ReferenceEquals(value, BindingOperations.UnsetValue) || ReferenceEquals(value, BindingOperations.DoNothing))
        {
            throw new InvalidCastException("The binding has no value.");
        }

        if (value is null)
        {
            return DefaultOf(targetType);
        }

        if (targetType.IsInstanceOfType(value))
        {
            return value;
        }

        var culture = CultureInfo.CurrentCulture;
        if (targetType == typeof(string))
        {
            return value is IFormattable formattable ? formattable.ToString(null, culture) : value.ToString();
        }

        var nullable = Nullable.GetUnderlyingType(targetType);
        var type = nullable ?? targetType;
        if (value is string text)
        {
            if (nullable is not null && string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (type.IsEnum)
            {
                return Enum.Parse(type, text.Trim(), ignoreCase: true);
            }

            if (type == typeof(bool))
            {
                return bool.Parse(text.Trim());
            }

            if (type == typeof(char))
            {
                return text.Length == 1 ? text[0] : throw new FormatException($"'{text}' is not a single character.");
            }

            if (type == typeof(Guid))
            {
                return Guid.Parse(text);
            }

            if (type == typeof(TimeSpan))
            {
                return TimeSpan.Parse(text, culture);
            }

            if (type == typeof(DateTimeOffset))
            {
                return DateTimeOffset.Parse(text, culture);
            }
        }

        if (type.IsEnum)
        {
            return value is IConvertible
                ? Enum.ToObject(type, Convert.ChangeType(value, Enum.GetUnderlyingType(type), culture))
                : throw new InvalidCastException($"Cannot convert '{value.GetType()}' to '{targetType}'.");
        }

        if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(type))
        {
            return Convert.ChangeType(value, type, culture);
        }

        throw new InvalidCastException($"Cannot convert '{value.GetType()}' to '{targetType}'.");
    }

}

/// <summary>
/// Implemented by widgets that support <see cref="UpdateSourceTrigger.LostFocus"/> (raise <see cref="LostFocus"/> when
/// keyboard focus leaves the widget). Other widgets treat <c>LostFocus</c> as <see cref="UpdateSourceTrigger.PropertyChanged"/>.
/// </summary>
internal interface ILostFocusSource
{
    /// <summary>Raised when the widget loses keyboard focus.</summary>
    event EventHandler? LostFocus;
}
