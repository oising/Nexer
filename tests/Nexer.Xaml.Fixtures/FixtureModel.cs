using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Nexer.Core;
using Nexer.Ui.Markup;

[assembly: XmlnsDefinition(XamlNamespaces.Ui, "Nexer.Xaml.Fixtures")]

namespace Nexer.Xaml.Fixtures;

/// <summary>Fixture base widget (mirrors Nexer.Ui.Widget's XAML-facing surface).</summary>
[UsableDuringInitialization]
public class Widget : ISupportInitialize, IResourceProvider
{
    private Rect _bounds;

    public Widget() => Children = new WidgetCollection(this);

    public string? Name { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public Rect Bounds
    {
        get => _bounds;
        set
        {
            _bounds = value;
            ParentKnownWhenBoundsSet = Parent is not null;
        }
    }

    public Thickness Margin { get; set; }

    public CellStyle Style { get; set; }

    public CellStyle? FocusStyle { get; set; }

    public TerminalColor Foreground { get; set; }

    public Visibility Visibility { get; set; }

    public object? Tag { get; set; }

    public Widget? Parent { get; internal set; }

    [Content]
    public WidgetCollection Children { get; }

    public ResourceDictionary Resources { get; } = [];

    public List<string> InitLog { get; } = [];

    /// <summary>Whether <see cref="Parent"/> was already set when <see cref="Bounds"/> was assigned (top-down init).</summary>
    public bool ParentKnownWhenBoundsSet { get; private set; }

    public void BeginInit() => InitLog.Add("BeginInit");

    public void EndInit() => InitLog.Add("EndInit");

    public bool TryGetResource(string key, out object? value) => Resources.TryGetValue(key, out value);
}

public enum Visibility
{
    Visible,
    Hidden,
    Collapsed,
}

[Flags]
public enum Anchors
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
}

public sealed class WidgetCollection(Widget owner) : Collection<Widget>
{
    protected override void InsertItem(int index, Widget item)
    {
        item.Parent = owner;
        base.InsertItem(index, item);
    }
}

public sealed class ResourceDictionary : Dictionary<string, object?>;

public class Window : Widget
{
    public string? Title { get; set; }

    public Anchors Anchors { get; set; }

    public KeyGesture? CloseGesture { get; set; }

    /// <summary>Deferred content: compiled to a <c>Func&lt;IServiceProvider, object&gt;</c> factory.</summary>
    [DeferredContent]
    public object? Template { get; set; }
}

public class Button : Widget
{
    public string? Text { get; set; }

    public Command? Command { get; set; }

    public KeyGesture? Shortcut { get; set; }

    public event EventHandler? Click;

    public void PerformClick() => Click?.Invoke(this, EventArgs.Empty);
}

public class Label : Widget
{
    [Content]
    public string? Text { get; set; }
}

public class Application : IResourceProvider
{
    public ResourceDictionary Resources { get; } = [];

    [Content]
    public Window? MainWindow { get; set; }

    public bool TryGetResource(string key, out object? value) => Resources.TryGetValue(key, out value);
}

/// <summary>Attached properties as static Get/Set pairs.</summary>
public static class Dock
{
    private static readonly ConditionalWeakTableStore<Widget, DockEdge> s_edges = new();

    public static DockEdge GetEdge(Widget widget) => s_edges.Get(widget);

    public static void SetEdge(Widget widget, DockEdge value) => s_edges.Set(widget, value);
}

public enum DockEdge
{
    None,
    Top,
    Bottom,
    Left,
    Right,
}

internal sealed class ConditionalWeakTableStore<TKey, TValue>
    where TKey : class
{
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<TKey, Box> _table = new();

    public TValue? Get(TKey key) => _table.TryGetValue(key, out var box) ? box.Value : default;

    public void Set(TKey key, TValue value) => _table.AddOrUpdate(key, new Box(value));

    private sealed record Box(TValue Value);
}

public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    public static int ParseCalls { get; private set; }

    public static Rect Parse(string text)
    {
        ParseCalls++;
        var p = text.Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        return new Rect(p[0], p[1], p[2], p[3]);
    }
}

public readonly record struct Thickness(int Left, int Top, int Right, int Bottom)
{
    public static Thickness Parse(string text)
    {
        var p = text.Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        return p.Length switch
        {
            1 => new Thickness(p[0], p[0], p[0], p[0]),
            2 => new Thickness(p[0], p[1], p[0], p[1]),
            _ => new Thickness(p[0], p[1], p[2], p[3]),
        };
    }
}

public sealed record KeyGesture(string Key, bool Ctrl, bool Alt, bool Shift)
{
    public static KeyGesture Parse(string text)
    {
        var parts = text.Split('+');
        bool Has(string m) => parts[..^1].Contains(m, StringComparer.OrdinalIgnoreCase);
        return new KeyGesture(parts[^1], Has("Ctrl"), Has("Alt"), Has("Shift"));
    }
}

public sealed record Command(string Name)
{
    public static readonly Command Quit = new("Quit");

    public static Command Parse(string text) => new(text);
}

/// <summary>Static members for <c>{x:Static}</c> tests.</summary>
public static class Constants
{
    public const string Greeting = "Hello";

    public static int Answer => 42;
}

/// <summary>A sealed type with a private event handler (code-behind style) for root-instance tests.</summary>
public class HandlerWindow : Window
{
    public int Clicks { get; private set; }

    public int PrivateClicks { get; private set; }

    public void OnClick(object? sender, EventArgs e) => Clicks++;

    private void OnPrivateClick(object? sender, EventArgs e) => PrivateClicks++;
}
