# XAML and data binding in Nexer

## Document skeletons

```xml
<!-- Window with code-behind and a view model -->
<Window xmlns="https://nexer.dev/ui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:MyApp.ViewModels"
        x:Class="MyApp.Views.OrdersWindow"
        x:DataType="vm:OrdersViewModel"
        Title="Orders" Width="60" Height="18" Centered="True">
  <Window.Resources>
    <vm:MoneyConverter x:Key="money" />              <!-- any type with a public parameterless ctor -->
  </Window.Resources>
  ...
</Window>

<!-- Application root: menus, desktop, app-wide resources -->
<Application xmlns="https://nexer.dev/ui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="MyApp.App">
  <Application.Resources><x:String x:Key="appName">My App</x:String></Application.Resources>
  <Application.Menus>...</Application.Menus>
  <Application.Desktop><Desktop><Desktop.StatusBar><StatusBar Text="Ready" /></Desktop.StatusBar></Desktop></Application.Desktop>
</Application>
```

```csharp
public partial class App : Application
{
    public static readonly Command Open = new("Open");      // matches Command="Open" in XAML
    public App() { InitializeComponent(); }
    protected override bool OnCommand(Command command)
    {
        if (command == Open) { _ = OpenAsync(); return true; }
        return base.OnCommand(command);                     // keep built-ins (Exit, Tile, WindowClose...)
    }
}
```

Code-behind: `public partial class X : <RootElementType>`, constructor calls the generated `InitializeComponent()`
before touching named fields. Keep a public parameterless constructor (needed by the build step's
`XamlComponent.Build`, by smoke runs and by tests).

## Directives and markup extensions

| Syntax | Meaning |
|---|---|
| `x:Class="Ns.Type"` | Root only; partial class in the same assembly. |
| `x:Name="Foo"` | Typed `internal` field + `NameScope.FindName(root, "Foo")`. `x:FieldModifier="public"` to change. |
| `x:Key="k"` | Key inside `Resources`. |
| `x:DataType="p:Type"` | Static DataContext type for `{Binding}` on this element and descendants. |
| `{StaticResource k}` | Resource lookup: enclosing elements' `Resources`, then root, then application. Use it from descendants, not on the element that declares the resources. |
| `{x:Static Command.Exit}` | Static field/property/constant. |
| `{x:Null}`, `{x:Type p:T}` | Null / `Type`. |
| `{Binding ...}` | Compiled binding (below). |
| `Click="OnClick"` | Event handler method on the root type (may be private; signature `(object? sender, EventArgs e)` or the event's `EventArgs` type). |
| `AnchorLayout.Anchor="Left, Bottom"` | Attached property (`AnchorLayout.SetAnchor(widget, value)`). |

Primitive literals: `<x:String>`, `<x:Int32>`, `<x:Boolean>` etc. are available for resources and item content.

## Binding syntax

```xml
<Field   Text="{Binding Customer.Name}" />                                   <!-- TwoWay (default for Field.Text) -->
<Field   Text="{Binding Age, UpdateSourceTrigger=LostFocus}" />              <!-- commit on focus loss -->
<Label   Text="{Binding Total, StringFormat=N2, FallbackValue=-}" />
<Label   Text="{Binding Count, StringFormat='{}{0} items'}" />               <!-- leading {} escapes the brace -->
<Label   Text="{Binding Price, Converter={StaticResource money}, ConverterParameter=EUR}" />
<Label   Text="{Binding Selected.Name, TargetNullValue=(none), FallbackValue=(none)}" />
<Label   Text="{Binding Text, ElementName=SearchField}" />
<Label   Text="{Binding Title, RelativeSource={RelativeSource FindAncestor, AncestorType=Window}}" />
<CheckBox Checked="{Binding IsAdmin, Mode=OneWay}" />
<Button  Command="{Binding DeleteCommand}" CommandParameter="{Binding Selected}" />
<List    ItemsSource="{Binding Orders}" DisplayMemberBinding="{Binding Description}"
         SelectedItem="{Binding SelectedOrder}" />
<Label   Text="{Binding Name, DataType=vm:Person}" />                        <!-- override the source type -->
```

- Path grammar: `A.B.C`, indexers `Items[0]`, `Map[key]`, `Map['two words']`, attached `(p:Type.Prop)`, casts
  `((p:Derived)Item).Member`. No method calls.
- Modes: `OneWay`, `TwoWay`, `OneTime`, `OneWayToSource`, `Default` (= TwoWay for `[BindsTwoWayByDefault]`
  properties, else OneWay).
- `UpdateSourceTrigger`: `PropertyChanged` (default), `LostFocus` (`Field` only), `Explicit`
  (`BindingOperations.GetBindingExpression(widget, "Text")!.UpdateSource()`).
- Unresolvable path (null intermediate, wrong owner type, throwing getter) → `FallbackValue` or the target default;
  not an error. Null leaf → `TargetNullValue`.
- Conversions are built in for strings ↔ numbers/enums/bool/char/Guid/TimeSpan/DateTime(Offset) (current
  culture). A failed target→source conversion becomes a **validation error** and the source keeps its value.
- Converters implement `Nexer.Ui.Data.IValueConverter` (not WPF's): `Convert/ConvertBack(object? value, Type
  targetType, object? parameter, CultureInfo culture)`; return `BindingOperations.UnsetValue` (fallback / skip
  source) or `BindingOperations.DoNothing`.

## View-model patterns

```csharp
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; OnPropertyChanged(name); return true;
    }
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? p) => canExecute?.Invoke(p) ?? true;
    public void Execute(object? p) => execute(p);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
```

- Computed properties: raise their notification from the setters they depend on
  (`if (Set(ref name, value)) OnPropertyChanged(nameof(Summary));`).
- `CommandParameter` changes re-query `CanExecute`; otherwise call `RaiseCanExecuteChanged()` when state changes.
  Disabled commands draw the control disabled and block Enter/click/mnemonic.
- Validation: throw from a setter (shown as an error), or implement `INotifyDataErrorInfo`. Errors tint the control
  (`tfield.error` ...) and the focused widget's first error replaces the window status bar text. Read them with
  `Validation.GetHasErrors(widget)` / `Validation.GetErrors(widget)`.
- `ObservableCollection<T>` Add/Remove/Replace/Move update lists incrementally and keep the selection. `List`/
  `TreeView` refresh an item's text when the item raises `PropertyChanged` for the first segment of
  `DisplayMemberBinding`.
- Background threads may raise `PropertyChanged`/`CollectionChanged`; they are marshalled to the UI thread.
- Window-scoped DataContext: `new OrdersWindow { DataContext = vm }`; app-wide: `app.DataContext = vm` (inherited by
  every window). Subscriptions exist only while the window is attached, so closed windows are not leaked.

## Common diagnostics → fix

| Code | Typical cause | Fix |
|---|---|---|
| NXAML0001 | Unknown element/property, wrong casing, missing `xmlns:` prefix, property that does not exist on that control | Check `references/controls.md`; property names are PascalCase CLR names. |
| NXAML0002 | Bad value for enum / `CellStyle` / `Rect` / `KeyGesture` | Use the syntax in the controls reference. |
| NXAML0003 | `x:Class` type missing, not `partial`, wrong base class, or same class used twice | Match namespace + class name; derive from the root element type. |
| NXAML0004 | Duplicate `x:Name` | Rename. |
| NXAML0005 | Malformed XML (often an unescaped `&`) | Use `&amp;`. |
| NXAML0101 | `{Binding}` without `x:DataType` in scope (or item type not inferable) | Add `x:DataType` on the root or `DataType=` on the binding. |
| NXAML0102/0103 | Unknown or non-public member in path | Make it a public instance property; fix the spelling. |
| NXAML0104 | Two-way binding to a get-only property | Add a setter or `Mode=OneWay`. |
| NXAML0105 (warning) | No conversion between source and target types | Add a `Converter` or change the property type. |
| NXAML0107/0108 | Unknown `ElementName` / bad `RelativeSource` | Name must exist in the same document; `AncestorType` must be a `Widget`. |
| NXAMLG002 | Code-behind class not `partial` | Add `partial`. |

## Runtime loading (optional)

`Nexer.Xaml.Loader` package: `XamlLoader.Load<Window>(xamlText)`, `XamlLoader.Load(xamlText, existingRoot)`,
`XamlLoader.Validate(xamlText)` (returns diagnostics). Useful for tools, plugins and quick validation; compiled XAML
is preferred for apps.
