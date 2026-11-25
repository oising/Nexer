namespace System.Runtime.CompilerServices;

/// <summary>
/// Recognised by the runtime (by name) on dynamic assemblies: lets compiled XAML access non-public members of the
/// named assembly, e.g. private event handlers in code-behind.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal sealed class IgnoresAccessChecksToAttribute(string assemblyName) : Attribute
{
    public string AssemblyName { get; } = assemblyName;
}
