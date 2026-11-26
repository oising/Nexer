# Data binding (contract)

Status: **contract frozen; implementation in progress.** This document is the agreement between the runtime-engine,
controls and compiler agents. The public surface lives in `src\Nexer.Ui\Data\*.cs` (files marked `// CONTRACT:`) plus
the `Widget`/`Application` members listed below. Changes to that surface go through the coordinator (additive only).
Spike findings and compiler internals: [binding-design-notes.md](binding-design-notes.md).

## Overview

Nexer supports WPF-style MVVM with **compiled bindings only**: every `{Binding}` whose source is the DataContext needs
an `x:DataType` in scope, paths are resolved at compile time (Cecil build step and the SRE loader use the same
transformers) and the compiler emits strongly typed accessor delegates. There is no runtime reflection over paths.

```xml
<Window xmlns="https://nexer.dev/ui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:MyApp" x:DataType="local:PersonViewModel">
  <Label Text="{Binding Name}"/>
  <Field Text="{Binding Age, StringFormat='{}{0}'}"/>            <!-- TwoWay by default ([BindsTwoWayByDefault]) -->
  <Label Text="{Binding Address.City, FallbackValue=?}"/>
  <Label Text="{Binding Text, ElementName=NameField}"/>
  <Label Text="{Binding X, RelativeSource={RelativeSource Self}}"/>
  <Label Text="{Binding Title, RelativeSource={RelativeSource FindAncestor, AncestorType=Window}}"/>
  <Button Text="Save" Command="{Binding SaveCommand}"/>
  <List ItemsSource="{Binding People}" DisplayMemberBinding="{Binding Name, DataType=local:Person}"/>
</Window>
```

## Public API (namespace `Nexer.Ui.Data`, xmlns `https://nexer.dev/ui`)

| Type | Members |
|---|---|
| `IBinding` | `BindingMode Mode { get; }` |
| `BindingMode` | `Default, OneWay, TwoWay, OneTime, OneWayToSource` |
| `UpdateSourceTrigger` | `Default, PropertyChanged, LostFocus, Explicit` |
| `IValueConverter` | `object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)`; `object? ConvertBack(…)` (same shape). Return `BindingOperations.UnsetValue` → fallback / no source update; `DoNothing` → skip. |
| `BindsTwoWayByDefaultAttribute` | `[AttributeUsage(Property)]`; read by the compiler. |
| `BindingSourceKind` | `DataContext = 0, Self = 1, ElementName = 2, FindAncestor = 3` |
| `RelativeSourceMode` | `Self, FindAncestor` |
| `PathNode` | ctor `(string name, Type valueType, Func<object, object?> get, Action<object, object?>? set, bool notifies)`; `Name`, `ValueType`, `Get`, `Set`, `Notifies` (owner type implements `INotifyPropertyChanged`). `Get` returns `UnsetValue` if the owner is not of the compile-time type; `Set` is then a no-op. |
| `CompiledBindingPath` | ctor `(BindingSourceKind source, string? elementName, Type? ancestorType, int ancestorLevel, PathNode[] nodes)`, ctor `(params PathNode[] nodes)` (DataContext); `Source`, `ElementName`, `AncestorType`, `AncestorLevel`, `Nodes`, `ValueType`, `IsWritable`. Zero nodes = the source itself (`{Binding}`). |
| `CompiledBinding : IBinding` | ctors `()`, `(CompiledBindingPath)`; `Path`, `Mode`, `Converter`, `ConverterParameter`, `StringFormat`, `FallbackValue` (default `UnsetValue`), `TargetNullValue` (default `UnsetValue`), `UpdateSourceTrigger`, `ValidatesOnExceptions = true`, `ValidatesOnNotifyDataErrors = true`. |
| `BindingExtension` | XAML syntax only (no `ProvideValue`; the compiler rewrites it into `CompiledBinding`). ctors `()`, `(string path)`; `Path`, `Mode`, `Converter`, `ConverterParameter`, `StringFormat`, `FallbackValue`, `TargetNullValue`, `UpdateSourceTrigger`, `ValidatesOnExceptions`, `ValidatesOnNotifyDataErrors`, `ElementName`, `RelativeSource`, `Type? DataType`. |
| `RelativeSource` | Compile-time only (consumed inside `{Binding}`; an error anywhere else). ctors `()`, `(RelativeSourceMode)`; `Mode`, `AncestorType`, `AncestorLevel = 1`. |
| `BindingOperations` | `UnsetValue`, `DoNothing` sentinels; `Bind(Widget target, string propertyName, Func<object, object?>? targetGetter, Action<object, object?> targetSetter, Type targetType, IBinding binding) → BindingExpression` (+ 5-arg overload without getter); `GetBindingExpression(Widget, string)`, `GetBindingExpressions(Widget)`, `ClearBinding(Widget, string)`, `ClearAllBindings(Widget)`, `Evaluate(IBinding, object? source, Type targetType)` (one-shot, for per-item bindings). |
| `BindingExpression : IDisposable` | `Target`, `TargetProperty`, `TargetType`, `Binding`, `EffectiveMode`, `DataItem` (resolved source), `HasError`, `IsDisposed`, `UpdateTarget()`, `UpdateSource()`, `Dispose()` (detach). Created only by `Bind`. |
| `ValidationError` | `record (object? ErrorContent, Exception? Exception, BindingExpression? BindingExpression)` |
| `Validation` | `GetErrors(Widget)`, `GetHasErrors(Widget)`, `Add/RemoveErrorsChangedHandler(Widget, EventHandler<ValidationErrorsChangedEventArgs>)`, `SetErrors(BindingExpression, IEnumerable<ValidationError>)`, `MarkInvalid(ValidationError)`, `ClearErrors(BindingExpression)`. |

`Widget` (implemented on this branch unless noted):

- `: INotifyPropertyChanged` — `event PropertyChanged`, `protected bool SetProperty<T>(ref T, T, [CallerMemberName] string?)`,
  `protected void OnPropertyChanged([CallerMemberName] string?)`.
- `object? DataContext { get; set; }` — effective value = local ?? `Parent.DataContext` ?? (window) `Application.DataContext`.
  Setting `null` reverts to the inherited value. `event EventHandler? DataContextChanged` is raised on the widget and
  every inheriting descendant when the effective value changes (local set, ancestor set, `Application.DataContext`
  set, re-parenting via `AttachChild`/`DetachChild`).
- `bool IsAttachedToTree`, `event AttachedToTree`, `event DetachedFromTree`. `internal void SetAttachedToTree(bool)`
  propagates to the subtree; **controls agent** must call it from `Application.AddWindow`/`CloseWindow` and when a
  child is attached to / detached from an attached parent.

`Application.DataContext` (`object?`) — root of inheritance; notifies windows without a local value.

`Command` (`sealed record Command(string Name)`) now implements `System.Windows.Input.ICommand`:
`CanExecute` → `true`, `Execute` → `Application.Current?.ExecuteCommand(this)`, `CanExecuteChanged` never fires.

## What the compiler emits

For `<Label Text="{Binding Child.Title, Mode=OneWay}"/>` (inside `x:DataType="local:Vm"`), after the label is
constructed and before it is added to its parent:

```csharp
BindingOperations.Bind(label, "Text",
    getter: XamlBindingAccessors_x.Get_0 /* Label.Text */,       // null if the property has no public getter
    setter: XamlBindingAccessors_x.Set_0 /* Label.Text */,
    typeof(string),
    new CompiledBinding {
        Path = new CompiledBindingPath(BindingSourceKind.DataContext, null, null, 1, new[] {
            new PathNode("Child", typeof(Child), Get_1, Set_1 /* or null */, notifies: true /* Vm : INPC */),
            new PathNode("Title", typeof(string), Get_2, Set_2, notifies: true) }),
        Mode = BindingMode.OneWay,
        // Converter, ConverterParameter, StringFormat, FallbackValue, TargetNullValue, UpdateSourceTrigger,
        // ValidatesOn* are assigned only when present in XAML (FallbackValue/TargetNullValue as strings).
    });
```

- Accessors are static methods on a nested helper type: `Get(object)` = `isinst Owner` guard (→ `UnsetValue`), call
  the getter, box; `Set(object, object?)` = guard (→ no-op), `unbox.any`/`castclass` the value, call the setter.
  Target setters receive a value already converted to `targetType` (or `null`).
- Source kinds: `DataContext` (root type = nearest `x:DataType`, or `DataType=` on the binding), `Self` (target
  type), `ElementName` (type of the `x:Name`/`Name`d element in the document), `FindAncestor` (`AncestorType`,
  `AncestorLevel`).
- **Default mode** is resolved by the compiler: if XAML gives no `Mode` and the target property has
  `[BindsTwoWayByDefault]`, it emits `Mode = TwoWay`; otherwise `Mode` stays `Default`, which the runtime treats as
  `OneWay`. Two-way (or OneWayToSource) to a path without a setter is a compile error.
- Properties whose type is assignable from `CompiledBinding` (`IBinding`: `DisplayMemberBinding`, `ChildrenBinding`)
  get the `CompiledBinding` object itself (no `Bind` call). These need `DataType=` on the binding (item type
  inference from `ItemsSource` is a later compiler enhancement) and a DataContext source.
- Binding targets must be `Widget`s with a public instance setter (no `TreeItem`, `ThemeColor`, …) — NXAML0106.

## Runtime semantics (runtime-engine agent)

Implemented in `src\Nexer.Ui\Data\BindingExpression.cs`; tests in `tests\Nexer.Ui.Tests\Data\`.

- **Source resolution** (repeated on every attach, `DataContextChanged` and `UpdateTarget()`): `DataContext` →
  `Target.DataContext`; `Self` → `Target`; `ElementName` → walk the target and its ancestors, first
  `NameScope.GetNameScope(w)?.Find(name)` hit (a name registered after `Bind` — compiled XAML registers names at the
  end of the document — is found on attach); `FindAncestor` → the `AncestorLevel`-th ancestor (starting at
  `Target.Parent`, up to and including the window) that is an instance of `AncestorType`. Re-parenting is seen
  through the detach/attach pair (or `DataContextChanged`).
- **Lifecycle**: `Bind` registers the expression (replacing an existing one for the same property). On an attached
  target it activates immediately; otherwise it evaluates once **only if a source resolves** (so a widget bound
  before it is added to its parent is not reset to the default). Subscriptions (`INotifyPropertyChanged` on every
  node owner that implements it at runtime, `INotifyDataErrorInfo.ErrorsChanged` on the leaf owner, the target's
  `PropertyChanged` for TwoWay/OneWayToSource) exist **only while `Target.IsAttachedToTree`**: `AttachedToTree`
  subscribes and re-evaluates, `DetachedFromTree` unsubscribes, so a long-lived view model never keeps a closed
  window alive. `DataContextChanged`, `AttachedToTree` and `DetachedFromTree` are raised by the target itself and
  stay subscribed until `Dispose`; `DataContextChanged` always re-evaluates (without subscribing while detached).
  An owner shared by several nodes is subscribed once.
- **Path walk**: node *i* is read from the value of node *i−1*; a `null` intermediate, `UnsetValue` (wrong owner
  type, e.g. a DataContext of another type) or an exception from a getter = unresolved → `FallbackValue` (if set,
  converted to the target type) else the target type's default (`null`/`default(T)`, which for `string` is `null`).
  A `null` leaf is resolved (→ `TargetNullValue`). When a node owner raises `PropertyChanged` with that node's name
  (`null`/empty = all), the path is re-walked from that node down (old downstream owners are unsubscribed).
- **Source → target**: unresolved → fallback; `null` with `TargetNullValue` set → `TargetNullValue` (the converter
  is **not** called, as in WPF/Avalonia); otherwise `Converter.Convert(value, targetType, parameter,
  CultureInfo.CurrentCulture)` (`UnsetValue` → fallback, `DoNothing` → target untouched) → if `targetType == string`
  and `StringFormat` set: `string.Format(culture, fmt, v)` (a format without `{` is treated as `{0:fmt}`) →
  conversion (below) → `targetSetter`. Any exception in this pipeline → fallback (never thrown to the source).
- **Modes**: `Default` = `OneWay`. `OneTime` evaluates without subscriptions, and only when the resolved source
  object differs from the one it last evaluated (first activation, DataContext change); `UpdateTarget()` always
  evaluates. `OneWayToSource` pushes the target value to the source when it activates / the source is (re)resolved,
  then on target changes; source changes never touch the target.
- **Target → source** (TwoWay, OneWayToSource): on the target's `PropertyChanged(TargetProperty)` when
  `UpdateSourceTrigger` is `Default`/`PropertyChanged`; `Explicit` only via `UpdateSource()`; `LostFocus` when the
  target implements the internal `ILostFocusSource` (`Field`), otherwise like `PropertyChanged`. Requires a resolved
  leaf owner and a leaf `Set`. Value via `targetGetter` → `ConvertBack(value, leaf.ValueType, …)` (`DoNothing`/
  `UnsetValue` → source untouched) → conversion to the leaf's `ValueType` → leaf `Set`. Echo suppression: source
  notifications are ignored while the expression writes the source (the target is not re-read/re-formatted while the
  user types; a source that coerces the value is not reflected until its next change), and target notifications are
  ignored while it writes the target, so there is no ping-pong.
- **Conversion rules** (both directions, always `CultureInfo.CurrentCulture`): `null` → `null` for reference/nullable
  targets, `default` otherwise; assignable → as is; `string` target → `IFormattable.ToString(null, culture)`/
  `ToString()`; from `string`: empty/whitespace → `null` for nullables, enums via `Enum.Parse` (ignore case), `bool`,
  `char` (exactly one character), `Guid`, `TimeSpan`, `DateTimeOffset`, numerics/`DateTime` via
  `Convert.ChangeType` (culture-aware parse); numbers → enums via `Enum.ToObject`; other `IConvertible` pairs via
  `Convert.ChangeType`; anything else fails (`InvalidCastException`). Failure source→target → fallback; failure
  target→source → validation error, source unchanged.
- **Validation**: when `ValidatesOnExceptions`, exceptions from `ConvertBack`, conversion or the source setter become
  `ValidationError(ex.Message, ex, expr)` (otherwise they are swallowed and the source is untouched); a successful
  source update, or a later source → target transfer, clears it. When `ValidatesOnNotifyDataErrors` and the leaf owner
  implements `INotifyDataErrorInfo`, `GetErrors(leafName)` is mirrored as `ValidationError(error, null, expr)` after
  every walk and on `ErrorsChanged` for that name (or all); this applies to every mode, not only two-way. The
  expression publishes its errors with `Validation.SetErrors(expr, …)` (the exception error first), which raises the
  errors-changed handlers and invalidates the application only when its set actually changes. `Dispose` clears them.
- **Threading**: a source `PropertyChanged`/`ErrorsChanged` raised where `(Target.Application ??
  Application.Current).CheckAccess()` is false is marshalled with `Application.Post`; notifications arriving before
  the posted work runs are collapsed into one full re-walk. With no application (or no running loop) updates apply
  inline. The widget tree is touched only on the UI thread.
- `ClearBinding`/`Dispose` unsubscribe everything and leave the property value as is.

## Controls contract (controls agent)

- Every authorable/bindable property raises `PropertyChanged` when it changes, **including by user input**
  (`SetProperty`). `[BindsTwoWayByDefault]` on: `Field.Text`, `CheckBox.Checked`, `RadioGroup.Selected`,
  `List.SelectedIndex`, `List.SelectedItem`, `ComboBox.Text`, `ComboBox.SelectedIndex`, `ComboBox.SelectedItem`,
  `TreeView.SelectedItem`, scroller `Value`. (`ProgressBar.Value` stays OneWay.)
- **ICommand**: `Command` on `Button`, `MenuItem`, `CheckBox`, `Label`, `StatusKey` becomes
  `System.Windows.Input.ICommand?`, plus `object? CommandParameter`. `Command="Exit"` keeps working (the existing
  `Command` converter produces the record, which is an `ICommand`). Code that dispatches named commands checks
  `command is Command named` before calling `Application.ExecuteCommand`; otherwise `Execute(CommandParameter)`.
  While attached, subscribe to `CanExecuteChanged` → effective enabled state (`Enabled && CanExecute(parameter)`).
- **Items**: `IEnumerable? ItemsSource` on `List`, `ComboBox`, `TreeView`; `INotifyCollectionChanged` incremental
  updates (Reset fallback), subscribed while attached; `Items` and `ItemsSource` are mutually exclusive (setting one
  while the other is in use throws `InvalidOperationException`). `IBinding? DisplayMemberBinding` (evaluated per
  item with `BindingOperations.Evaluate(binding, item, typeof(string))`, else `item?.ToString()`). `TreeView`:
  `IBinding? ChildrenBinding` (evaluated to `IEnumerable?` per item, lazily on expansion). `SelectedItem` returns the
  source item when `ItemsSource` is used.
- **Validation visuals**: theme keys (`tfield.error`, `tcheckbox.error`, … — Nexer extensions, not Jexer) used when
  `Validation.GetHasErrors(this)`; the window status bar shows the focused widget's first error.
- Raise `AttachedToTree`/`DetachedFromTree` via `SetAttachedToTree` (see above).

## Diagnostics

Binding diagnostics use `NXAML0101`–`NXAML0149` (four digits like the other codes; `NXAML0100` is the existing
Obsolete warning). Errors unless noted.

| Code | Meaning |
|---|---|
| NXAML0101 | `{Binding}` with a DataContext source but no `x:DataType` in scope (or no `DataType=` for `IBinding` properties) |
| NXAML0102 | Unknown member in a binding path |
| NXAML0103 | Member is not public / not an instance property |
| NXAML0104 | TwoWay/OneWayToSource binding to a read-only path |
| NXAML0105 | Warning: source type not convertible to the target type and no `Converter` (reserved; not yet emitted) |
| NXAML0106 | Invalid binding target (not a `Widget`, no public setter, binding used outside a property, multiple values) |
| NXAML0107 | Unknown `ElementName` |
| NXAML0108 | Invalid `RelativeSource` (bad mode, missing `AncestorType`, combined with `ElementName`). Outside `{Binding}` a `{RelativeSource}` fails with NXAML0001 (no `ProvideValue`). |
| NXAML0109 | Invalid / unsupported binding path syntax (indexers, casts, attached properties: not yet) |

## Ownership

| Area | Owner |
|---|---|
| `src\Nexer.Ui\Data\` contract surface (`// CONTRACT:` files) | coordinator |
| `BindingExpression` internals, `BindingOperations` implementation, conversion, validation store, marshalling | runtime-engine agent |
| Rest of `src\Nexer.Ui` (INPC in controls, lifecycle raising, ICommand, ItemsSource, validation visuals) and `Nexer.Ui.Testing` | controls agent |
| `src\Nexer.Xaml.*` (`Transformers\BindingTransformers.cs`: indexers, NXAML0105, `[DeferredContent]` reset, item-type inference, loader access targets for view-model assemblies) | compiler agent |
| Integration, demo MVVM window, docs | coordinator |

`BindingExpression` implements the runtime semantics above (it replaced the spike's OneWay/OneTime-only body; the
public members are unchanged).

## Out of scope (v1)

`MultiBinding`, `PriorityBinding`, reflection bindings without `x:DataType`, data templates (items render as text),
styles/triggers, `UpdateSourceTrigger=LostFocus` on non-`Field` widgets, cast and attached-property path segments.
