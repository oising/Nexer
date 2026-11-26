using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Nexer.Ui.Data;

namespace Nexer.Ui.Tests.Data;

/// <summary>A plain widget with bindable properties.</summary>
internal class BindTarget : Widget
{
    private string? text;
    private int number;

    public string? Text
    {
        get => text;
        set => SetProperty(ref text, value);
    }

    public int Number
    {
        get => number;
        set => SetProperty(ref number, value);
    }

    public int TextSets { get; set; }

    public void Attach() => SetAttached(this, true);

    public void Detach() => SetAttached(this, false);

    /// <summary>Simulates adding the widget's window to an application (<c>Widget.SetAttachedToTree</c> is internal).</summary>
    public static void SetAttached(Widget widget, bool attached) =>
        typeof(Widget).GetMethod("SetAttachedToTree", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(widget, [attached]);

    public BindingExpression BindText(CompiledBinding binding) =>
        BindingOperations.Bind(this, nameof(Text), static t => ((BindTarget)t).Text,
            static (t, v) => { var w = (BindTarget)t; w.TextSets++; w.Text = (string?)v; }, typeof(string), binding);

    public BindingExpression BindNumber(CompiledBinding binding) =>
        BindingOperations.Bind(this, nameof(Number), static t => ((BindTarget)t).Number,
            static (t, v) => ((BindTarget)t).Number = (int)v!, typeof(int), binding);
}

internal abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public int HandlerCount => PropertyChanged?.GetInvocationList().Length ?? 0;

    public void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Raise(name);
    }
}

internal sealed class Address : Observable
{
    private string? city;

    public string? City
    {
        get => city;
        set => Set(ref city, value);
    }
}

internal sealed class Person : Observable, INotifyDataErrorInfo
{
    private readonly Dictionary<string, List<string>> errors = [];
    private string? name;
    private int age;
    private Address? address;
    private int? score;

    public string? Name
    {
        get => name;
        set => Set(ref name, value);
    }

    public int Age
    {
        get => age;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            Set(ref age, value);
        }
    }

    public int? Score
    {
        get => score;
        set => Set(ref score, value);
    }

    public Address? Address
    {
        get => address;
        set => Set(ref address, value);
    }

    public int NoisySets { get; private set; }

    /// <summary>A property that always raises (even for an unchanged value), to provoke ping-pong.</summary>
    public string? Noisy
    {
        get => name;
        set
        {
            NoisySets++;
            name = value;
            Raise(nameof(Noisy));
        }
    }

    public bool HasErrors => errors.Count > 0;

    public int ErrorHandlerCount => ErrorsChanged?.GetInvocationList().Length ?? 0;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName) =>
        propertyName is not null && errors.TryGetValue(propertyName, out var list) ? list : Array.Empty<string>();

    public void SetError(string property, params string[] messages)
    {
        if (messages.Length == 0) errors.Remove(property);
        else errors[property] = [.. messages];
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(property));
    }
}

internal sealed class UpperConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            "unset" => BindingOperations.UnsetValue,
            "skip" => BindingOperations.DoNothing,
            string s => s.ToUpperInvariant() + parameter,
            _ => value,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s ? s.ToLowerInvariant() : value;
}

/// <summary>Builds compiled paths the way the XAML compiler does (type-guarded static accessors).</summary>
internal static class Paths
{
    public static PathNode Node<TOwner, TValue>(string name, Func<TOwner, TValue> get, Action<TOwner, TValue>? set = null) =>
        new(name, typeof(TValue),
            o => o is TOwner owner ? get(owner) : BindingOperations.UnsetValue,
            set is null ? null : (o, v) => { if (o is TOwner owner) set(owner, (TValue)v!); },
            typeof(INotifyPropertyChanged).IsAssignableFrom(typeof(TOwner)));

    public static PathNode Name => Node<Person, string?>("Name", p => p.Name, (p, v) => p.Name = v);

    public static PathNode Age => Node<Person, int>("Age", p => p.Age, (p, v) => p.Age = v);

    public static PathNode Score => Node<Person, int?>("Score", p => p.Score, (p, v) => p.Score = v);

    public static PathNode Noisy => Node<Person, string?>("Noisy", p => p.Noisy, (p, v) => p.Noisy = v);

    public static PathNode AddressNode => Node<Person, Address?>("Address", p => p.Address, (p, v) => p.Address = v);

    public static PathNode City => Node<Address, string?>("City", a => a.City, (a, v) => a.City = v);

    public static CompiledBinding Binding(BindingMode mode, params PathNode[] nodes) =>
        new(new CompiledBindingPath(nodes)) { Mode = mode };
}
