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
- Properties whose type is assignable from `CompiledBinding` (`IBinding`: `DisplayMemberBinding`, `ChildrenBinding`;
  not `object`) get the `CompiledBinding` object itself (no `Bind` call) and are evaluated per item (DataContext
  source only). The item type is `DataType=` on the binding if given, otherwise **inferred** when `ItemsSource` on
  the same element is itself a `{Binding}` whose leaf type implements `IEnumerable<T>` (or is `T[]`): the item type
  is `T`. Otherwise NXAML0101.
- Binding targets must be `Widget`s with a public instance setter (no `TreeItem`, `ThemeColor`, …) — NXAML0106;
  TwoWay/OneWayToSource bindings also need a public getter on the target property.
- **Path grammar** (see [xaml.md](xaml.md#compiled-bindings)): `''`/`.` (the source itself), dotted properties,
  indexers `[0]`/`[key]`/`['key']` (int indexer preferred, then string; arrays via `IList<T>`; node name `Item[]`,
  `Notifies` = owner implements INPC — sources should raise `PropertyChanged("Item[]")`), attached properties
  `(prefix:Type.Prop)` (static `GetProp(owner)`, read-only, node name `(Type.Prop)`, `Notifies = false`) and casts
  `((prefix:Type)path)` (no node of its own: the next node's accessor guards on the cast type, so the previous
  node's `ValueType` is the pre-cast member type).
- Indexer getters run the real indexer, so they can throw (`ArgumentOutOfRangeException`, `KeyNotFoundException`);
  **the runtime must treat an exception from `PathNode.Get` as "unresolved"** (fallback), like `UnsetValue`.
- **Access**: accessors call members directly. With the build step (Cecil) they are emitted into the compiled
  assembly, so internal view models of that assembly work. The SRE loader grants its dynamic assembly
  `IgnoresAccessChecksTo` for every assembly whose types a binding touches (`NexerXamlDocument.AccessedAssemblies`),
  so non-public view models work there too. Members themselves must still be public (NXAML0103).

## Runtime semantics (runtime-engine agent)

- **Source resolution**: `DataContext` → `Target.DataContext`; `Self` → `Target`; `ElementName` → walk the target's
  ancestors, `NameScope.GetNameScope(w)?.Find(name)` (resolved once attached; re-resolve on re-parenting);
  `FindAncestor` → the `AncestorLevel`-th ancestor assignable to `AncestorType` (parents, then the window).
- **Lifecycle**: `Bind` registers the expression (replacing an existing one for the same property) and evaluates
  once if a source is available. Subscriptions (`INotifyPropertyChanged` on every `Notifies` node,
  `INotifyDataErrorInfo.ErrorsChanged`, the target's `PropertyChanged` for TwoWay/OneWayToSource,
  `DataContextChanged`) exist **only while `Target.IsAttachedToTree`**; detaching unsubscribes from sources so a
  long-lived view model never keeps a closed window alive. Re-attach re-evaluates. `DataContextChanged` always
  re-evaluates (it is raised on the widget itself, no leak).
- **Path walk**: node *i* is read from the value of node *i−1*; a `null` intermediate or `UnsetValue` = unresolved →
  `FallbackValue` (if set) else the target type's default (`null`/`default(T)`, which for `string` is `null`).
  Re-subscribe from the changed node downwards when an intermediate changes (`PropertyChanged` with that node's
  name, `null` or empty = all).
- **Source → target**: value (`null` → `TargetNullValue` if set) → `Converter.Convert(value, targetType, parameter,
  CultureInfo.CurrentCulture)` → if `targetType == string` and `StringFormat` set: `string.Format(culture, fmt, v)`
  (a format without `{` is treated as `{0:fmt}`) → conversion (below) → `targetSetter`. `OneTime` evaluates on
  attach/DataContext change only, without subscriptions.
- **Target → source** (TwoWay, OneWayToSource): on the target's `PropertyChanged(TargetProperty)` when
  `UpdateSourceTrigger` is `Default`/`PropertyChanged`; `LostFocus` only on `Field` (on focus loss); `Explicit` only
  via `UpdateSource()`. Value via `targetGetter` → `ConvertBack` (or conversion to the leaf's `ValueType`) → leaf
  `Set`. A re-entrancy guard suppresses the echo in both directions.
- **Conversion rules**: `null` → `null` for reference/nullable targets, `default` otherwise; assignable → as is;
  `string` target → `IFormattable.ToString(null, culture)`/`ToString()`; from `string` to numeric/`bool`/enum/`char`
  (and their nullables) via `Parse` with the current culture (empty string → `null` for nullables); other primitive
  pairs via `Convert.ChangeType`. Failure source→target → fallback; failure target→source → validation error,
  source unchanged.
- **Validation**: when `ValidatesOnExceptions`, exceptions from `ConvertBack`, conversion or the source setter become
  `ValidationError(ex.Message, ex, expr)`; a successful update clears them. When `ValidatesOnNotifyDataErrors` and the
  leaf owner implements `INotifyDataErrorInfo`, its errors for the leaf property name are mirrored (subscribe while
  attached). Use `Validation.SetErrors(expr, …)` so errors from one expression replace only its own.
- **Threading**: source notifications raised off the UI thread are marshalled with `Application.Post` (when the
  target's application is known; otherwise applied inline). The widget tree is touched only on the UI thread.
- `ClearBinding`/`Dispose` detach and leave the property value as is. Exceptions in converters during
  source→target are swallowed (fallback) and do not crash the app.

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
| NXAML0102 | Unknown member in a binding path (including no indexer / attached getter for the segment) |
| NXAML0103 | Member is not public / not an instance property |
| NXAML0104 | TwoWay/OneWayToSource binding to a read-only path |
| NXAML0105 | Warning: source and target types have no built-in conversion (per *Conversion rules*) and no `Converter` |
| NXAML0106 | Invalid binding target (not a `Widget`, no public setter, two-way without a public getter, binding used outside a property, multiple values, `ElementName`/`RelativeSource` on a per-item binding) |
| NXAML0107 | Unknown `ElementName` |
| NXAML0108 | Invalid `RelativeSource` (bad mode, missing or non-`Widget` `AncestorType`, combined with `ElementName`). Outside `{Binding}` a `{RelativeSource}` fails with NXAML0001 (no `ProvideValue`). |
| NXAML0109 | Invalid binding path syntax, unresolvable type in a cast/attached segment, impossible cast |

## Ownership

| Area | Owner |
|---|---|
| `src\Nexer.Ui\Data\` contract surface (`// CONTRACT:` files) | coordinator |
| `BindingExpression` internals, `BindingOperations` implementation, conversion, validation store, marshalling | runtime-engine agent |
| Rest of `src\Nexer.Ui` (INPC in controls, lifecycle raising, ICommand, ItemsSource, validation visuals) and `Nexer.Ui.Testing` | controls agent |
| `src\Nexer.Xaml.*` (`Transformers\BindingTransformers.cs`: indexers, NXAML0105, `[DeferredContent]` reset, item-type inference, loader access targets for view-model assemblies) | compiler agent |
| Integration, demo MVVM window, docs | coordinator |

The current `BindingExpression` is a spike implementation (OneWay/OneTime for `Self`/`DataContext`, no
subscriptions); the runtime-engine agent replaces its body keeping the public members above.

## Out of scope (v1)

`MultiBinding`, `PriorityBinding`, reflection bindings without `x:DataType`, data templates (items render as text),
styles/triggers, `UpdateSourceTrigger=LostFocus` on non-`Field` widgets, method calls / multi-argument indexers in paths.
