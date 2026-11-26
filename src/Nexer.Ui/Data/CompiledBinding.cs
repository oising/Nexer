// CONTRACT: Nexer.Ui.Data public surface (bindings). See docs\binding.md. Additive changes only, via the coordinator.
namespace Nexer.Ui.Data;

/// <summary>
/// A binding whose path was resolved at compile time. The XAML compiler turns every <c>{Binding}</c> into a
/// <see cref="CompiledBinding"/> (setting <see cref="Path"/> to an emitted <see cref="CompiledBindingPath"/> and the
/// remaining properties from the markup); code can also build one directly.
/// </summary>
public sealed class CompiledBinding : IBinding
{
    /// <summary>Creates an empty binding (set <see cref="Path"/> before use).</summary>
    public CompiledBinding()
    {
    }

    /// <summary>Creates a binding for <paramref name="path"/>.</summary>
    public CompiledBinding(CompiledBindingPath path)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
    }

    /// <summary>The compiled path.</summary>
    public CompiledBindingPath? Path { get; set; }

    /// <inheritdoc/>
    public BindingMode Mode { get; set; }

    /// <summary>Converts values between source and target.</summary>
    public IValueConverter? Converter { get; set; }

    /// <summary>Passed to <see cref="Converter"/>.</summary>
    public object? ConverterParameter { get; set; }

    /// <summary>
    /// A composite format string (e.g. <c>{0:N2}</c> or <c>N2</c>) applied when the target is a <see cref="string"/>
    /// (after the converter, if any).
    /// </summary>
    public string? StringFormat { get; set; }

    /// <summary>
    /// Used when the path cannot be resolved (a null intermediate, no source) or the converter returns
    /// <see cref="BindingOperations.UnsetValue"/>. <see cref="BindingOperations.UnsetValue"/> (the default) means
    /// "use the target type's default value".
    /// </summary>
    public object? FallbackValue { get; set; } = BindingOperations.UnsetValue;

    /// <summary>
    /// Used when the resolved source value is <see langword="null"/>. <see cref="BindingOperations.UnsetValue"/> (the
    /// default) means "pass null through".
    /// </summary>
    public object? TargetNullValue { get; set; } = BindingOperations.UnsetValue;

    /// <summary>When target → source updates happen.</summary>
    public UpdateSourceTrigger UpdateSourceTrigger { get; set; }

    /// <summary>Whether exceptions thrown by the source setter or <see cref="IValueConverter.ConvertBack"/> become validation errors.</summary>
    public bool ValidatesOnExceptions { get; set; } = true;

    /// <summary>Whether <see cref="System.ComponentModel.INotifyDataErrorInfo"/> errors of the source become validation errors.</summary>
    public bool ValidatesOnNotifyDataErrors { get; set; } = true;

    /// <inheritdoc/>
    public override string ToString() => $"{{Binding {Path}}}";
}
