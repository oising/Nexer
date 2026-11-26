# Data binding

Nexer supports WPF-style MVVM: every widget has an inherited `DataContext`, and XAML `{Binding}`s keep widget
properties in sync with view-model properties. Bindings are **compiled only**: paths are resolved at compile time
against an `x:DataType` (by the Cecil build step and the SRE loader alike, with the same transformers), and the
compiler emits strongly typed accessor delegates. There is no runtime reflection over paths, so typos are build errors
and bindings are trim/AOT friendly.

Contents: [Quick start](#quick-start) · [XAML syntax](#xaml-syntax) · [Runtime behaviour](#runtime-behaviour) ·
[Controls](#controls) · [Threading and lifetime](#threading-and-lifetime) · [API reference](#api-reference) ·
[Diagnostics](#diagnostics) · [Limitations](#limitations) ·
[Compiler implementation notes](#compiler-implementation-notes)

The demo (`dotnet run --project samples\Nexer.Demo`, menu **Demo → Data binding (MVVM)...**) shows a complete form:
`samples\Nexer.Demo\Windows\DemoBindingWindow.xaml(.cs)`.

## Quick start

```csharp
public sealed class PersonViewModel : INotifyPropertyChanged
{
    private string name = "";
    private int age;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get => name; set { name = value; Raise(); } }
    public int Age { get => age; set { age = value; Raise(); } }      // non-numeric input → validation error
    public ObservableCollection<Person> People { get; } = [];
    public Person? Selected { get; set; }                              // raise PropertyChanged in real code
    public ICommand SaveCommand { get; }                               // any ICommand (e.g. a RelayCommand)

    private void Raise([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new(p));
}
```

```xml
<Window xmlns="https://nexer.dev/ui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:MyApp" x:Class="MyApp.PersonWindow"
        x:DataType="local:PersonViewModel" Title="Person" Width="50" Height="16">
  <Field x:Name="NameField" Text="{Binding Name}" Width="20"/>      <!-- TwoWay by default -->
  <Field Text="{Binding Age}" Y="1" Width="6"/>
  <Label Text="{Binding Text, ElementName=NameField}" Y="2"/>
  <List Y="3" Height="6" ItemsSource="{Binding People}"
        DisplayMemberBinding="{Binding Name}" SelectedItem="{Binding Selected}"/>
  <Button Y="10" Text="&amp;Save" Command="{Binding SaveCommand}" CommandParameter="{Binding Selected}"/>
</Window>
```

```csharp
var window = new PersonWindow { DataContext = new PersonViewModel() };   // or Application.DataContext = vm
```

## XAML syntax

`{Binding}` (also `{Binding Path}` / `{Binding Path=…}`) is the `BindingExtension` markup syntax; the compiler
rewrites it into a `CompiledBinding` plus a `BindingOperations.Bind` call. Properties:

| Property | Meaning |
|---|---|
| `Path` (positional) | The path (grammar below). Empty or `.` = the source itself. |
| `Mode` | `OneWay`, `TwoWay`, `OneTime`, `OneWayToSource`, `Default` (see *Modes*). |
| `Converter`, `ConverterParameter` | An `IValueConverter` (usually `{StaticResource …}`) and its parameter. |
| `StringFormat` | Used when the target is `string`: `string.Format(culture, fmt, value)`; a format without `{` means `{0:fmt}` (`StringFormat=N2`). In XAML write a leading `{}` for a format starting with `{`: `StringFormat='{}{0} items'`. |
| `FallbackValue` | Used when the path cannot be resolved (or conversion fails). Converted to the target type. |
| `TargetNullValue` | Used when the resolved value is `null` (the converter is not called). |
| `UpdateSourceTrigger` | `Default`/`PropertyChanged` (every change), `LostFocus` (`Field` only, see below), `Explicit` (`BindingExpression.UpdateSource()`). |
| `ValidatesOnExceptions`, `ValidatesOnNotifyDataErrors` | Both default to `true` (see *Validation*). |
| `ElementName` | Source = the named element in the same document. |
| `RelativeSource` | `{RelativeSource Self}` or `{RelativeSource FindAncestor, AncestorType=local:T, AncestorLevel=n}`. Giving only `AncestorType` implies `FindAncestor` (as in Avalonia). |
| `DataType` | Overrides the `x:DataType` in scope for this binding (`DataType=local:Person` or `{x:Type local:Person}`). |

**Sources.** Without `ElementName`/`RelativeSource` the source is the target's `DataContext`, typed by the nearest
`x:DataType` on the element or an ancestor element (or `DataType=` on the binding); `x:DataType` is inherited down
the XAML tree and can be overridden on any element, and it is reset inside `[DeferredContent]`. `Self` uses the
target's type, `ElementName` the named element's type (names are document-wide, including deferred content),
`FindAncestor` the `AncestorType` (which must derive from `Widget`).

**Path grammar.**

- Dotted properties: `Address.City`. Members must be public instance properties.
- Indexers: `Items[0]`, `Map[key]`, `Map['key with spaces']`. An `int` indexer is preferred, then `string`; arrays use
  `IList<T>`. To refresh an indexer binding, raise `PropertyChanged("Item[]")` (or `null`/empty for all).
- Attached properties: `(prefix:Type.Prop)`, read through the static `GetProp(owner)` (read-only, no notifications).
- Casts: `((prefix:Type)Path)` or `((prefix:Type)Path).Member`, e.g. to reach a derived member.

**Modes.** If `Mode` is omitted and the target property is marked `[BindsTwoWayByDefault]` the binding is `TwoWay`,
otherwise `OneWay`. `OneTime` sets the target once per source object (first activation and each DataContext change).
`OneWayToSource` pushes the target value to the source. `TwoWay`/`OneWayToSource` need a settable path (NXAML0104)
and a target property with a public getter (NXAML0106).

**Per-item bindings.** Properties typed `IBinding` (`DisplayMemberBinding` on `List`/`ComboBox`/`TreeView`,
`TreeView.ChildrenBinding`) receive the `CompiledBinding` itself and evaluate it against each item. The item type is
inferred from a sibling `ItemsSource="{Binding …}"` whose type implements `IEnumerable<T>` (or is `T[]`), in either
attribute order; otherwise give `DataType=`. Per-item bindings cannot use `ElementName`/`RelativeSource`.

**Commands.** `Command` on `Button`, `CheckBox`, `Label`, `MenuItem` and `StatusKey` is an
`System.Windows.Input.ICommand?`, so it can be bound (`Command="{Binding SaveCommand}"`) together with
`CommandParameter`. A literal such as `Command="Exit"` or `Command="Open"` still compiles, into `Command.Parse(text)`
(the well-known instances like `Command.Exit`, otherwise a new named `Command`), and dispatches through
`Application.ExecuteCommand`/`OnCommand` as before. `Command` implements `ICommand` with `CanExecute` always `true`.

## Runtime behaviour

Implemented by `BindingExpression` (`src\Nexer.Ui\Data\BindingExpression.cs`).

- **Source resolution** (on every attach, `DataContextChanged` and `UpdateTarget()`): `DataContext` →
  `Target.DataContext`; `Self` → the target; `ElementName` → the first `NameScope` hit walking up from the target (a
  name registered later is found when the target is attached); `FindAncestor` → the `AncestorLevel`-th ancestor,
  starting at `Target.Parent`, up to and including the window, that is an `AncestorType`.
- **DataContext inheritance**: effective value = the widget's own value ?? `Parent.DataContext` ?? (for a window)
  `Application.DataContext`. Setting `null` reverts to the inherited value. `DataContextChanged` is raised on the
  widget and every inheriting descendant when the effective value changes (local set, ancestor set, application set,
  re-parenting).
- **Path walk**: node *i* reads the value of node *i−1*. A `null` intermediate, an owner of the wrong type (e.g. an
  inner widget seeing an outer DataContext) or **an exception thrown by a getter** (e.g. an out-of-range indexer)
  means *unresolved* → `FallbackValue` if set, else the target type's default. Unresolved paths are not validation
  errors. A `null` leaf is resolved → `TargetNullValue` if set. When an owner raises `PropertyChanged` for its node
  (`null`/empty = all) the path is re-walked from that node.
- **Source → target**: `TargetNullValue` (for `null`, skips the converter) → `Converter.Convert` (`UnsetValue` →
  fallback, `DoNothing` → target unchanged) → `StringFormat` (string targets) → conversion → setter. Any exception →
  fallback; nothing is thrown at the source.
- **Target → source** (`TwoWay`, `OneWayToSource`): on the target's `PropertyChanged` (trigger
  `Default`/`PropertyChanged`), when the `Field` loses focus (`LostFocus`; other widgets treat it as
  `PropertyChanged`) or on `UpdateSource()` (`Explicit`). Value → `ConvertBack` (`UnsetValue`/`DoNothing` → source
  untouched) → conversion to the leaf type → leaf setter. Echo is suppressed in both directions, so a `Field` is not
  re-formatted while the user types.
- **Conversion** (both directions, `CultureInfo.CurrentCulture`): `null` → `null`/`default`; assignable values as
  is; to `string` via `IFormattable.ToString(null, culture)`/`ToString()`; from `string`: blank → `null` for
  nullables, enums (`Enum.Parse`, ignore case), `bool`, `char` (one character), `Guid`, `TimeSpan`,
  `DateTimeOffset`, numerics/`DateTime` (culture-aware); numbers → enums; other `IConvertible` pairs via
  `Convert.ChangeType`. A failure source → target uses the fallback; target → source is a validation error and the
  source keeps its value.
- **Validation**: with `ValidatesOnExceptions`, exceptions from `ConvertBack`, conversion or the source setter
  become `ValidationError(ex.Message, ex, expression)`; a later successful update clears it. With
  `ValidatesOnNotifyDataErrors`, a leaf owner implementing `INotifyDataErrorInfo` has `GetErrors(leaf)` mirrored
  (every mode). Errors are read with `Validation.GetErrors/GetHasErrors` and observed with
  `Validation.AddErrorsChangedHandler`.
- `ClearBinding`/`Dispose` unsubscribe and leave the property value as it is. Binding the same property again
  replaces the previous expression.

## Controls

- Every authorable property raises `PropertyChanged` when it changes, including through user input, so any widget
  property can be a binding target and two-way bindings see user edits.
- `[BindsTwoWayByDefault]`: `Field.Text`, `CheckBox.Checked`, `RadioGroup.Selected`, `List.SelectedIndex`,
  `List.SelectedItem`, `ComboBox.Text`, `ComboBox.SelectedIndex`, `ComboBox.SelectedItem`, `TreeView.SelectedItem`,
  `SplitPane.Split` and the scroller values (`Value`, `VerticalValue`, `HorizontalValue`, also on
  `ScrollableWidget`/`ScrollableWindow`). `ProgressBar.Value` is one-way.
- **Items**: `List`, `ComboBox` and `TreeView` have `IEnumerable? ItemsSource` (incremental updates for
  `INotifyCollectionChanged`, `Reset` rebuilds) and `IBinding? DisplayMemberBinding` (else `ToString()`);
  `TreeView.ChildrenBinding` gives each item's children (evaluated lazily on expansion). `Items` and `ItemsSource`
  are mutually exclusive (`InvalidOperationException`). With `ItemsSource`, `SelectedItem` (`object?`) is the source
  item; `List.SelectedText` and `TreeView.SelectedTreeItem` give the displayed text / tree node. While attached,
  `List` and `TreeView` also refresh an item's text when the item raises `PropertyChanged` for the first segment of
  `DisplayMemberBinding` (e.g. renaming a person updates the list).
- **Commands**: while attached, a control subscribes to `CanExecuteChanged`; its effective enabled state is
  `Enabled && CanExecute(CommandParameter)` (re-queried when `CommandParameter` changes), so a disabled button is
  drawn disabled and ignores Enter, clicks and its mnemonic.
- **Validation visuals**: a widget with errors draws with `tfield.error`, `tcheckbox.error`, `tcombobox.error` or
  `tlist.error` (Nexer extensions to the Jexer theme), and the window status bar shows the focused widget's first
  error with `tstatusbar.error`.

## Threading and lifetime

- A `PropertyChanged`/`ErrorsChanged`/`CanExecuteChanged`/`CollectionChanged` raised off the UI thread (where
  `Application.CheckAccess()` is false) is marshalled with `Application.Post`; bursts collapse into one re-walk. The
  widget tree is only touched on the UI thread. Without a running application updates apply inline.
- Subscriptions to view-model events (`PropertyChanged`, `ErrorsChanged`, `CollectionChanged`, `CanExecuteChanged`,
  item `PropertyChanged`) exist **only while the widget is attached** to an application's tree
  (`Widget.IsAttachedToTree`, `AttachedToTree`/`DetachedFromTree`): closing a window releases them, so a long-lived
  view model never keeps a closed window alive. Re-attaching re-subscribes and re-evaluates.
- Bindings are created while the widget is being built (before it has a parent): a binding evaluates immediately only
  if its source resolves, so a property's XAML default is not overwritten before the DataContext arrives.

## API reference

Namespace `Nexer.Ui.Data` (XAML: `https://nexer.dev/ui`, no prefix needed).

| Type | Members |
|---|---|
| `IBinding` | `BindingMode Mode { get; }` |
| `BindingMode` | `Default, OneWay, TwoWay, OneTime, OneWayToSource` |
| `UpdateSourceTrigger` | `Default, PropertyChanged, LostFocus, Explicit` |
| `IValueConverter` | `object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)`; `ConvertBack` (same shape). Return `BindingOperations.UnsetValue` → fallback / no source update; `DoNothing` → skip. |
| `BindsTwoWayByDefaultAttribute` | `[AttributeUsage(Property)]`; read by the compiler. |
| `BindingSourceKind` | `DataContext, Self, ElementName, FindAncestor` |
| `RelativeSourceMode` | `Self, FindAncestor` |
| `PathNode` | ctor `(string name, Type valueType, Func<object, object?> get, Action<object, object?>? set, bool notifies)`; `Name`, `ValueType`, `Get`, `Set`, `Notifies`. |
| `CompiledBindingPath` | ctors `(BindingSourceKind source, string? elementName, Type? ancestorType, int ancestorLevel, PathNode[] nodes)`, `(params PathNode[] nodes)`; `Source`, `ElementName`, `AncestorType`, `AncestorLevel`, `Nodes`, `ValueType`, `IsWritable`. |
| `CompiledBinding : IBinding` | ctors `()`, `(CompiledBindingPath)`; `Path`, `Mode`, `Converter`, `ConverterParameter`, `StringFormat`, `FallbackValue`, `TargetNullValue` (both default `UnsetValue`), `UpdateSourceTrigger`, `ValidatesOnExceptions`, `ValidatesOnNotifyDataErrors`. |
| `BindingExtension` | XAML syntax only (rewritten by the compiler). ctors `()`, `(string path)`; the properties in [XAML syntax](#xaml-syntax). |
| `RelativeSource` | Compile-time only, inside `{Binding}`. ctors `()`, `(RelativeSourceMode)`; `Mode`, `AncestorType`, `AncestorLevel = 1`. |
| `BindingOperations` | `UnsetValue`, `DoNothing`; `Bind(Widget target, string propertyName, Func<object, object?>? targetGetter, Action<object, object?> targetSetter, Type targetType, IBinding binding)` (+ overload without the getter) → `BindingExpression`; `GetBindingExpression(Widget, string)`, `GetBindingExpressions(Widget)`, `ClearBinding(Widget, string)`, `ClearAllBindings(Widget)`, `Evaluate(IBinding, object? source, Type targetType)` (one-shot, used for per-item bindings). |
| `BindingExpression : IDisposable` | `Target`, `TargetProperty`, `TargetType`, `Binding`, `EffectiveMode`, `DataItem`, `HasError`, `IsDisposed`, `UpdateTarget()`, `UpdateSource()`, `Dispose()`. |
| `ValidationError` | `record (object? ErrorContent, Exception? Exception, BindingExpression? BindingExpression)` |
| `Validation` | `GetErrors(Widget)`, `GetHasErrors(Widget)`, `AddErrorsChangedHandler`/`RemoveErrorsChangedHandler(Widget, EventHandler<ValidationErrorsChangedEventArgs>)`, `SetErrors(BindingExpression, IEnumerable<ValidationError>)`, `MarkInvalid(ValidationError)`, `ClearErrors(BindingExpression)`. |

On `Widget` (namespace `Nexer.Ui`): `INotifyPropertyChanged` (`PropertyChanged`, protected `SetProperty<T>` and
`OnPropertyChanged`), `object? DataContext`, `event DataContextChanged`, `bool IsAttachedToTree`,
`event AttachedToTree`, `event DetachedFromTree`. On `Application`: `object? DataContext` (the root of
inheritance). `Command` (`sealed record Command(string Name) : ICommand`) has `static Parse(string)`.

## Diagnostics

Compile-time diagnostics (errors unless noted); `NXAML0101`–`NXAML0149` are reserved for bindings.

| Code | Meaning |
|---|---|
| NXAML0101 | `{Binding}` with a DataContext source but no `x:DataType` in scope (or a per-item binding whose item type cannot be inferred and has no `DataType=`) |
| NXAML0102 | Unknown member in a binding path (including no indexer / attached getter for the segment) |
| NXAML0103 | Member is not public / not an instance property |
| NXAML0104 | TwoWay/OneWayToSource binding to a read-only path |
| NXAML0105 | Warning: source and target types have no built-in conversion and no `Converter` |
| NXAML0106 | Invalid binding target (not a `Widget`, no public setter, two-way without a public getter, binding outside a property, multiple values, `ElementName`/`RelativeSource` on a per-item binding) |
| NXAML0107 | Unknown `ElementName` |
| NXAML0108 | Invalid `RelativeSource` (bad mode, missing or non-`Widget` `AncestorType`, combined with `ElementName`). Outside `{Binding}`, `{RelativeSource}` fails with NXAML0001. |
| NXAML0109 | Invalid path syntax, unresolvable type in a cast/attached segment, impossible cast |

## Limitations

- No `MultiBinding`, `PriorityBinding`, reflection (untyped) bindings, data templates (items render as text), styles
  or triggers.
- Paths cannot call methods or use multi-argument indexers.
- `UpdateSourceTrigger=LostFocus` is only honoured by `Field`; other widgets update on every change.
- Item text refresh on item `PropertyChanged` covers `List` and `TreeView`, and only the first segment of
  `DisplayMemberBinding` (`{Binding Address.City}` refreshes when the item raises `Address`, not when the address
  raises `City`). `ComboBox`'s edit text does not follow item changes; reset `ItemsSource` or raise a collection
  `Reset` to refresh.
- A source that coerces a value written by a two-way binding is not reflected in the target until its next change.
- A named `Command` (e.g. `Command="Exit"`) ignores `CommandParameter` and is always executable.

## Compiler implementation notes

The compiler side lives in `src\Nexer.Xaml.Compiler\Transformers\BindingTransformers.cs` (with
`NexerBindingAccessors`, `NexerBindingSetter` and `NexerBindingState`); tests are in
`tests\Nexer.Xaml.Tests\BindingCompilerTests.cs`, `tests\Nexer.Ui.Xaml.Tests\BindingCompilationTests.cs` and
`BindingEndToEndTests.cs` (SRE loader plus Cecil-compiled `Views\BindingWindow.xaml`/`Views\MvvmWindow.xaml`).

What is emitted, for `<Label Text="{Binding Child.Title, Mode=OneWay}"/>` inside `x:DataType="local:Vm"` (after the
label is constructed, before it is added to its parent):

```csharp
BindingOperations.Bind(label, "Text",
    XamlBindingAccessors_x.Get_0 /* Label.Text; null without a public getter */,
    XamlBindingAccessors_x.Set_0 /* Label.Text */,
    typeof(string),
    new CompiledBinding {
        Path = new CompiledBindingPath(BindingSourceKind.DataContext, null, null, 1, new[] {
            new PathNode("Child", typeof(Child), Get_1, Set_1 /* or null */, notifies: true /* Vm : INPC */),
            new PathNode("Title", typeof(string), Get_2, Set_2, notifies: true) }),
        Mode = BindingMode.OneWay,
        // Other properties are assigned only when present in XAML (FallbackValue/TargetNullValue as strings).
    });
```

- **Syntax pass.** `{Binding X}` parses to a markup-extension node (`BindingExtension`). `BindingSyntaxTransformer`
  runs right after `TypeReferenceResolver` and **before** `MarkupExtensionTransformer` (which reports an error for any
  markup extension without `ProvideValue`; `{RelativeSource}` is consumed here for the same reason). It strips
  `Path`/`ElementName`/`RelativeSource`/`DataType` and the positional argument, retypes the node to `CompiledBinding`
  and re-points the remaining properties at it, so XamlX handles `Mode`, `Converter="{StaticResource k}"` etc.
  normally. It also records `x:DataType` per element (pre-order, so ancestors come first).
- **Assignment pass.** `BindingAssignmentTransformer` runs after `PropertyReferenceResolver` (it needs the resolved
  target property to read `[BindsTwoWayByDefault]` and to detect `IBinding` properties) and just before
  `ConvertPropertyValuesToAssignmentsTransformer`. It resolves the path (recursive-descent parser for indexers,
  attached and cast segments), finds the nearest `x:DataType` through `context.ParentNodes()`, reports NXAML0101–0109
  and turns the property value into an assignment with the custom `NexerBindingSetter`
  (`IXamlILOptimizedEmitablePropertySetter`), bypassing XamlX's value-type setter matching (no ambiguity for
  `object` properties). Properties assignable from `CompiledBinding` but not `object` get the binding object itself.
- **Accessors.** `NexerBindingAccessors` defines a private nested `XamlBindingAccessors_<guid>` per document (the
  `XamlClosure_` pattern: `DefineSubType` + `AddAfterEmitCallbacks(CreateType)`), with `static object Get_n(object)`
  and `static void Set_n(object, object)`, de-duplicated per CLR member. Getters guard with `isinst Owner` and return
  `UnsetValue` for an owner of another type (bindings re-evaluate whenever an inherited DataContext changes); setters
  are then a no-op. `IXamlILEmitter` has no exception blocks, so throwing getters (indexers) are handled by the
  runtime. Delegates are `ldnull; ldftn; newobj`. Identical for Cecil and SRE.
- **Access.** With the Cecil build step accessors live in the compiled assembly, so internal view models of that
  assembly work. The SRE loader adds `IgnoresAccessChecksTo` for every assembly a binding touches (source, path and
  target types including generic arguments and array elements, collected during the transform as
  `NexerXamlDocument.AccessedAssemblies`). Members must still be public (NXAML0103).
- **Gotchas.** `XamlAstClrProperty` has no `PropertyType` (look the CLR property up on the owner). Cecil
  `IXamlType.Interfaces` lists only directly declared interfaces, so INPC/`IEnumerable<T>`/indexer lookups walk them
  recursively. When `Bind` runs the target is not yet in the tree (XamlX initializes a child completely before adding
  it), so the first evaluation usually happens when the parent attaches it (`DataContextChanged`).
