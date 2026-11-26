using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Nexer.Ui.Data;
using Nexer.Ui.Markup;
using UiWidget = Nexer.Ui.Widget;

[assembly: XmlnsDefinition("https://nexer.dev/ui/test-fixtures", "Nexer.Xaml.Fixtures.Binding")]

namespace Nexer.Xaml.Fixtures.Binding;

/// <summary>A real <see cref="UiWidget"/> exercising every binding-target feature the compiler cares about.</summary>
public class BindingHost : UiWidget
{
    /// <summary>A property whose bindings default to two-way.</summary>
    [BindsTwoWayByDefault]
    public string? Editable { get; set; }

    public string? Text { get; set; }

    public int Number { get; set; }

    public object? Value { get; set; }

    public DateTime When { get; set; }

    /// <summary>Read-only: cannot be a binding target.</summary>
    public string Computed => "computed";

    /// <summary>No public getter: can only be the target of one-way bindings.</summary>
    public string? Sink { private get; set; }

    public IEnumerable? ItemsSource { get; set; }

    /// <summary>A per-item binding (evaluated against each item of <see cref="ItemsSource"/>).</summary>
    public IBinding? DisplayMemberBinding { get; set; }

    /// <summary>A per-item binding returning an item's children.</summary>
    public IBinding? ChildrenBinding { get; set; }

    /// <summary>Deferred content: the x:DataType scope restarts inside.</summary>
    [DeferredContent]
    public object? Template { get; set; }
}

/// <summary>A derived host (FindAncestor by a derived type).</summary>
public sealed class DerivedHost : BindingHost
{
}

/// <summary>Not a widget: bindings on it are rejected.</summary>
public sealed class PlainTarget
{
    public string? Text { get; set; }
}

/// <summary>An attached property usable in binding paths as <c>(f:Tag.Label)</c>.</summary>
public static class Tag
{
    private static readonly ConditionalWeakTable<UiWidget, string> s_labels = [];

    public static string? GetLabel(UiWidget widget) => s_labels.TryGetValue(widget, out var value) ? value : null;

    public static void SetLabel(UiWidget widget, string? value)
    {
        if (value is null)
        {
            s_labels.Remove(widget);
        }
        else
        {
            s_labels.AddOrUpdate(widget, value);
        }
    }
}

/// <summary>The main binding view model.</summary>
public class Person : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; set; } = "";

    public int Age { get; set; }

    public string ReadOnlyName => Name;

    public Person? Friend { get; set; }

    public Pet? Pet { get; set; }

    public Location Home { get; set; }

    public List<string> Tags { get; set; } = [];

    public IReadOnlyList<Person> Friends { get; set; } = [];

    public Dictionary<string, string> Map { get; set; } = [];

    public int[] Scores { get; set; } = [];

    public Person[] Children { get; set; } = [];

    public object? Anything { get; set; }

    public static string Shared { get; set; } = "";

    internal string Hidden { get; set; } = "";

    public void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A plain (non-notifying) base type for cast paths.</summary>
public class Pet
{
    public string Name { get; set; } = "";
}

/// <summary>A derived type for cast paths.</summary>
public sealed class Dog : Pet
{
    public string Breed { get; set; } = "";
}

/// <summary>A value-type owner in a path.</summary>
public struct Location
{
    public string City { get; set; }
}

/// <summary>A view model that is not public (the SRE loader must be granted access).</summary>
internal sealed class SecretModel
{
    public string Secret { get; set; } = "";
}

/// <summary>Prefixes the value with the parameter.</summary>
public sealed class PrefixConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => $"{parameter}{value}";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && parameter is string p && s.StartsWith(p, StringComparison.Ordinal) ? s[p.Length..] : value;
}
