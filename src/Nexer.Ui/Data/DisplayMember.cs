namespace Nexer.Ui.Data;

/// <summary>
/// Helpers for item controls that refresh an item's display text when the item raises
/// <see cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged"/> for the property their
/// <c>DisplayMemberBinding</c> reads from the item (the first node of its path).
/// </summary>
internal static class DisplayMember
{
    /// <summary>Whether items need to be watched for <paramref name="binding"/> (a compiled binding with a non-empty path).</summary>
    public static bool Watches(IBinding? binding) => binding is CompiledBinding { Path.Nodes.Count: > 0 };

    /// <summary>Whether a change of <paramref name="propertyName"/> on an item can change its display text.</summary>
    public static bool Affects(IBinding? binding, string? propertyName) =>
        binding is CompiledBinding { Path.Nodes: { Count: > 0 } nodes }
        && (string.IsNullOrEmpty(propertyName) || nodes[0].Name == propertyName);
}
