# Binding design notes (spike findings)

Findings from the binding spike (`binding/contracts` branch). The implementation lives in
`src\Nexer.Xaml.Compiler\Transformers\BindingTransformers.cs`; the end-to-end tests are in
`tests\Nexer.Ui.Xaml.Tests\BindingCompilationTests.cs` (SRE loader) plus `Views\BindingWindow.xaml` (Cecil build step).
All four feasibility points were proven on both back ends.

## What works

1. **Replacing `{Binding}` before markup-extension handling.** `{Binding X}` parses to a `XamlAstObjectNode` whose
   type reference has `IsMarkupExtension = true`; `TypeReferenceResolver` tries `BindingExtension` then `Binding`.
   `BindingSyntaxTransformer` runs right after `TypeReferenceResolver` (inserted before `MarkupExtensionTransformer`).
   It strips `Path`/`ElementName`/`RelativeSource`/`DataType` and the positional argument, retypes the node to
   `CompiledBinding` (a plain object, `isMarkupExtension: false`), re-points the remaining property references
   (`Mode`, `Converter`, `StringFormat`, …) at `CompiledBinding`, and adds `Path = <NexerBindingPathNode>`. XamlX then
   handles the rest normally (`Mode="TwoWay"` → enum conversion; `Converter="{StaticResource k}"` → markup extension).
2. **Custom setter.** `BindingAssignmentTransformer` runs just before `ConvertPropertyValuesToAssignmentsTransformer`. It
   turns `XamlAstXamlPropertyValueNode(Text, [binding])` directly into
   `XamlPropertyAssignmentNode(Text, [NexerBindingSetter], [binding])`, so XamlX's value-type setter matching is
   bypassed completely (no ambiguity for `object`-typed properties, and ordinary values never see the custom setter).
   `NexerBindingSetter : IXamlILOptimizedEmitablePropertySetter`; `PropertyAssignmentEmitter` calls
   `EmitWithArguments` with the target already on the stack, and it emits
   `BindingOperations.Bind(target, "Text", getter, setter, typeof(string), binding)` + `pop`.
3. **Accessor methods.** `NexerBindingAccessors` defines a private nested type `XamlBindingAccessors_<guid>` per emit
   context (`context.DeclaringType.DefineSubType` + `AddAfterEmitCallbacks(CreateType)` + `context.SetItem`, the
   same pattern as XamlX's `XamlClosure_` for deferred content) with `static object Get_n(object)` and
   `static void Set_n(object, object)`. Delegates are `ldnull; ldftn; newobj Func<object,object>/Action<object,object>`.
   Identical for Cecil and SRE (the SRE nested type is created before the outer `XamlDocument` type without issue).
   Accessors are de-duplicated per CLR accessor within a document.
4. **x:DataType.** Removed and recorded per element by `BindingSyntaxTransformer` (pre-order visit, so ancestors are
   always recorded before descendants). `BindingAssignmentTransformer` walks `context.ParentNodes()` (nearest first) for
   the closest recorded type, so it is inherited and can be overridden on any element. Text (`local:Vm`) and
   `{x:Type local:Vm}` are both accepted.

## Gotchas

- `MarkupExtensionTransformer` *reports an error* for any node with `IsMarkupExtension` whose type has no `ProvideValue`,
  so the syntax pass must run **before** it (not merely before `ConvertPropertyValuesToAssignments` as the plan said).
  `{RelativeSource …}` has the same problem; the syntax pass consumes it (it is compile-time only).
- Path resolution needs the resolved target property (to read `[BindsTwoWayByDefault]` and detect `IBinding`-typed
  properties), so it happens in the second pass (after `PropertyReferenceResolver`), not in the syntax pass.
- `XamlAstClrProperty` has no `PropertyType`; look the CLR property up on the target object's type instead.
- `XamlPropertyAssignmentNode.VisitChildren` visits `Values`, so later passes still transform the binding object
  (its `Mode` etc. become assignments, `ConstructableObjectTransformer` makes it `new CompiledBinding()`).
- Getters must not throw on a DataContext of the wrong type (bindings are re-evaluated whenever the inherited
  DataContext changes, e.g. an inner panel before its own DataContext is set). Accessors therefore emit
  `isinst T` and return `BindingOperations.UnsetValue` (setters are a no-op); the runtime treats that as unresolved.
- When `Bind` runs, the target is not yet in the tree (XamlX initializes a child completely before adding it), so the
  initial evaluation sees no DataContext; re-parenting raises `DataContextChanged` (see `Widget.AttachChild`).
- SRE loader: the dynamic assembly gets `IgnoresAccessChecksTo` only for the root/document assemblies, so view models
  in other assemblies must be public (or the loader must add the x:DataType assemblies as access targets).
- Diagnostic codes are four digits; `NXAML0100` already exists (Obsolete), so bindings use `NXAML0101`–`NXAML0149`.

## Not done in the spike (compiler agent)

Indexers (`Items[0]`, `["key"]`), attached-property and cast path segments, the `NXAML0105` type-mismatch warning,
`x:DataType` reset inside `[DeferredContent]`, item-type inference for `DisplayMemberBinding`/`ChildrenBinding` from
`ItemsSource` (they currently require `DataType=` on the binding), ElementName scoping across deferred content, and
the loader access-target addition above.
