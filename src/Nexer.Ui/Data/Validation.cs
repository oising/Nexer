// CONTRACT: Nexer.Ui.Data validation surface (see docs\binding.md). Additive changes only, via the coordinator.
using System.Runtime.CompilerServices;

namespace Nexer.Ui.Data;

/// <summary>A validation error attached to a widget by a binding.</summary>
/// <param name="ErrorContent">The error to show (an <c>INotifyDataErrorInfo</c> error object, or the exception message).</param>
/// <param name="Exception">The exception that caused the error (converter, conversion or source setter), if any.</param>
/// <param name="BindingExpression">The binding that produced the error.</param>
public sealed record ValidationError(object? ErrorContent, Exception? Exception, BindingExpression? BindingExpression)
{
    /// <inheritdoc/>
    public override string ToString() => ErrorContent?.ToString() ?? Exception?.Message ?? "Invalid value";
}

/// <summary>Data for <see cref="Validation.AddErrorsChangedHandler"/>.</summary>
public sealed class ValidationErrorsChangedEventArgs(Widget widget, IReadOnlyList<ValidationError> errors) : EventArgs
{
    /// <summary>The widget whose errors changed.</summary>
    public Widget Widget { get; } = widget;

    /// <summary>The widget's current errors (empty when valid).</summary>
    public IReadOnlyList<ValidationError> Errors { get; } = errors;
}

/// <summary>
/// Validation state attached to widgets (WPF's <c>Validation</c>). Binding expressions report errors here; widgets
/// (error colours) and windows (status bar) read them.
/// </summary>
public static class Validation
{
    private static readonly ConditionalWeakTable<Widget, State> s_states = new();

    /// <summary>The current errors of <paramref name="widget"/> (empty when valid).</summary>
    public static IReadOnlyList<ValidationError> GetErrors(Widget widget)
    {
        ArgumentNullException.ThrowIfNull(widget);
        return s_states.TryGetValue(widget, out var state) ? state.Errors : [];
    }

    /// <summary>Whether <paramref name="widget"/> has validation errors.</summary>
    public static bool GetHasErrors(Widget widget) => GetErrors(widget).Count > 0;

    /// <summary>Subscribes to error changes of <paramref name="widget"/>.</summary>
    public static void AddErrorsChangedHandler(Widget widget, EventHandler<ValidationErrorsChangedEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(widget);
        ArgumentNullException.ThrowIfNull(handler);
        s_states.GetOrCreateValue(widget).Handlers += handler;
    }

    /// <summary>Unsubscribes from error changes of <paramref name="widget"/>.</summary>
    public static void RemoveErrorsChangedHandler(Widget widget, EventHandler<ValidationErrorsChangedEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(widget);
        ArgumentNullException.ThrowIfNull(handler);
        if (s_states.TryGetValue(widget, out var state))
        {
            state.Handlers -= handler;
        }
    }

    /// <summary>
    /// Replaces the errors produced by <paramref name="expression"/> on its target (an empty sequence clears them),
    /// raising the errors-changed handlers and invalidating the application when the set changes.
    /// </summary>
    public static void SetErrors(BindingExpression expression, IEnumerable<ValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(errors);
        var widget = expression.Target;
        var added = errors.ToList();
        if (added.Count == 0 && !s_states.TryGetValue(widget, out _))
        {
            return;
        }

        var state = s_states.GetOrCreateValue(widget);
        var updated = state.Errors.Where(e => !ReferenceEquals(e.BindingExpression, expression)).Concat(added).ToList();
        if (updated.SequenceEqual(state.Errors))
        {
            return;
        }

        state.Errors = updated;
        state.Handlers?.Invoke(widget, new ValidationErrorsChangedEventArgs(widget, updated));
        widget.Application?.Invalidate();
    }

    /// <summary>Adds an error for <paramref name="error"/>'s binding expression (keeping its other errors).</summary>
    public static void MarkInvalid(ValidationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var expression = error.BindingExpression ?? throw new ArgumentException("The error has no binding expression.", nameof(error));
        var existing = GetErrors(expression.Target).Where(e => ReferenceEquals(e.BindingExpression, expression));
        SetErrors(expression, existing.Append(error));
    }

    /// <summary>Removes every error produced by <paramref name="expression"/>.</summary>
    public static void ClearErrors(BindingExpression expression) => SetErrors(expression, []);

    private sealed class State
    {
        public IReadOnlyList<ValidationError> Errors { get; set; } = [];

        public EventHandler<ValidationErrorsChangedEventArgs>? Handlers { get; set; }
    }
}
