# Nexer XAML

Nexer UIs are written in XAML and compiled by [XamlX](https://github.com/kekekeks/XamlX). The same XamlX pipeline
and Nexer configuration run in two places:

| Path | When | Component |
|------|------|-----------|
| **Build time (Cecil)** | `dotnet build` of a project that imports `build\Nexer.Xaml.targets` | `Nexer.Xaml.Build` (CLI) → `Nexer.Xaml.Compiler` |
| **Runtime (SRE)** | `XamlLoader.Load(...)` with XAML text (tools, tests, hot reload, plugins) | `Nexer.Xaml.Loader` → `Nexer.Xaml.Compiler` |

A Roslyn source generator (`Nexer.Xaml.Generators`) adds typed `x:Name` fields and `InitializeComponent()` to
code-behind classes. All three are optional with respect to each other: code-behind can call
`XamlComponent.InitializeComponent(this)` directly without the generator.

## Using XAML in an app project

From NuGet, reference `Nexer.Ui` and `Nexer.Xaml`. `Nexer.Xaml` is a development dependency that contains the build
step, the source generator and `Nexer.Xaml.targets`, which NuGet imports automatically:

```xml
<ItemGroup>
  <PackageReference Include="Nexer.Ui" Version="1.0.0-preview.1" />
  <PackageReference Include="Nexer.Xaml" Version="1.0.0-preview.1" />
</ItemGroup>
```

In this repository (project references):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Nexer.Ui\Nexer.Ui.csproj" />
    <!-- The build step (runs via 'dotnet exec'; not referenced by the app). -->
    <ProjectReference Include="..\..\src\Nexer.Xaml.Build\Nexer.Xaml.Build.csproj"
                      ReferenceOutputAssembly="false" OutputItemType="NexerXamlBuildTool" />
    <!-- Optional: typed x:Name fields and InitializeComponent(). -->
    <ProjectReference Include="..\..\src\Nexer.Xaml.Generators\Nexer.Xaml.Generators.csproj"
                      ReferenceOutputAssembly="false" OutputItemType="Analyzer" />
  </ItemGroup>

  <Import Project="..\..\build\Nexer.Xaml.targets" />
</Project>
```

`tests\Nexer.Xaml.TestApp` is a working example. Instead of the `Nexer.Xaml.Build` project reference you can set
`NexerXamlBuildToolPath` to a built `Nexer.Xaml.Build.dll`; when the targets are imported from the `Nexer.Xaml`
package it defaults to the copy in the package's `tools` folder.

Add `Nexer.Xaml.Loader` as a reference only if the app loads XAML text at runtime.

### MSBuild items and properties

| Name | Meaning |
|------|---------|
| `NexerXaml` item | XAML files to compile. Defaults to `**\*.xaml` (minus the SDK's default excludes). Optional `LogicalName` metadata; defaults to `Link`, otherwise the project-relative path. Logical names use `/`. |
| `EnableDefaultNexerXamlItems` | `false` disables the default glob. |
| `NexerXamlEnabled` | `false` skips the build step. |
| `NexerXamlBuildToolPath` | Path to `Nexer.Xaml.Build.dll` (alternative to the `NexerXamlBuildTool` project reference; defaults to the `Nexer.Xaml` package's copy). |
| `NexerXamlDotnetHost` | Host used for `dotnet exec` (defaults to `$(DOTNET_HOST_PATH)`, else `dotnet`). |

`NexerXaml` items are also added as `AdditionalFiles` (for the generator) and `CustomAdditionalCompileInputs`, so
editing XAML recompiles the assembly.

### What the build step does

Target `NexerXamlCompile` runs after `CoreCompile` and before `CopyFilesToOutputDirectory`:

1. Writes `obj\...\nexerxaml\<name>.refs` from `@(ReferencePath)` and `<name>.items` (`fullPath|logicalName` per line).
2. Runs `dotnet exec Nexer.Xaml.Build.dll <assembly> <refs> <items> <output>`.
3. Copies the rewritten assembly and PDB over the intermediate assembly and touches a stamp file. Inputs/outputs
   (`@(IntermediateAssembly)`, the XAML, refs/items files, the tool) make the step incremental.

Inside the rewritten assembly:

- An `x:Class` document is compiled into its code-behind type as `!NexerXaml.Populate`, `!NexerXaml.Build` and
  untyped trampolines (members are `internal`, names are not valid C#). Because the code lives in the class, private
  event handlers work.
- A document without `x:Class` is compiled into a helper type `CompiledNexerXaml.Xaml_<n>`.
- `CompiledNexerXaml.XamlRegistration.Register()` registers every document with `Nexer.Ui.Markup.XamlComponent`; a
  call to it is prepended to the module initializer (`<Module>..cctor`). Its presence also marks the assembly as
  processed, so running the step twice is a no-op.
- `CompiledNexerXaml.XamlContext` is the XamlX runtime context type.
- Portable PDBs are rewritten (sequence points of user code are preserved). Strong-name signing is not re-applied.

### Code-behind

```xml
<!-- Views\MainWindow.xaml -->
<Window xmlns="https://nexer.dev/ui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="MyApp.Views.MainWindow" Title="Main">
  <Button x:Name="OkButton" Text="&amp;OK" Click="OnOk" />
</Window>
```

```csharp
namespace MyApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent(); // generated; or: XamlComponent.InitializeComponent(this);
        OkButton.Text = "&Yes"; // generated typed field
    }

    private void OnOk(object? sender, EventArgs e) { }
}
```

The generated `InitializeComponent(IServiceProvider? services = null)` calls
`XamlComponent.InitializeComponent<MainWindow>(this, services)` and then assigns each `x:Name` field from the
root's `NameScope`. Fields are `internal` unless `x:FieldModifier` says otherwise. No field is generated for an
`x:Name` on the root element itself, or for named elements inside `[DeferredContent]` (they are created later,
possibly many times).

## Runtime API

### `Nexer.Ui.Markup.XamlComponent` (in Nexer.Ui)

| Member | Purpose |
|--------|---------|
| `InitializeComponent<T>(T component, IServiceProvider? services = null)` / `InitializeComponent(Type, object, IServiceProvider?)` | Populates a code-behind instance from the compiled XAML whose `x:Class` is `T`. Throws `InvalidOperationException` if none is registered. |
| `TryInitializeComponent(Type, object, IServiceProvider?)` | Same, returns `false` instead of throwing (after trying `Fallback`). |
| `Fallback` | Optional `Func<Type, object, IServiceProvider?, bool>` used when nothing is registered (e.g. runtime loading during development). |
| `IsRegistered(Type)` | Whether compiled XAML exists for an `x:Class`. |
| `Build(Assembly, logicalName, services)` | Creates the root of a compiled document (`"Views/About.xaml"`). For an `x:Class` document this is `new T()` (the constructor is expected to call `InitializeComponent`). |
| `Populate(Assembly, logicalName, root, services)` | Populates an existing root. |
| `GetDocuments(Assembly)` | All registrations (`XamlDocumentRegistration`) of an assembly. |
| `Register(...)` | Called by generated code only. |

### `Nexer.Ui.Markup.NameScope`

`x:Name` (and a plain `Name` attribute on types with a settable string `Name`) sets the element's `Name` property
and registers the element in the **root object's** name scope:

- `NameScope.FindName(root, name)`, `NameScope.FindName<T>(root, name)`
- `NameScope.GetNameScope(root)`, `GetOrCreateNameScope(root)`, `SetNameScope(root, scope)`; a scope has
  `Register`, `Unregister`, `Find`, `Find<T>`, `Names`.

Scopes are attached to roots with a `ConditionalWeakTable`, so no property is needed on `Widget`.

### `Nexer.Xaml.XamlLoader` (in Nexer.Xaml.Loader)

```csharp
var window = XamlLoader.Load<Window>(xamlText);               // build a new root
XamlLoader.Load(xamlText, existingWindow);                     // populate an existing instance
var diagnostics = XamlLoader.Validate(xamlText);               // compile only, return diagnostics
```

- `Load(string xaml, object? rootInstance = null, Uri? baseUri = null, IServiceProvider? services = null)`,
  `Load<T>(...)`. Errors throw `NexerXamlCompilationException` (its `Diagnostics` lists them; `Message` is the
  formatted diagnostics). `baseUri` is used in diagnostics and exposed through `IUriContext`.
- With a `rootInstance` the root type is the instance's type (an `x:Class` must match it); without one, an `x:Class`
  root is constructed and populated (`new T()` + populate — the constructor should therefore *not* also call
  `InitializeComponent` when you load such XAML at runtime).
- Compiled documents are cached per (text, root type, base URI); `CacheCount`, `ClearCache()`.
- Assemblies referenced by `Nexer.Ui`, the calling assembly, the root instance's assembly and the entry assembly are
  force-loaded so xmlns definitions resolve; the type system is rebuilt when new assemblies are loaded.
- Each compiled document lives in its own non-collectible dynamic assembly with `IgnoresAccessChecksTo` for the
  root's assembly, so private event handlers on the root instance work, and for every assembly whose types a
  compiled binding touches (`NexerXamlDocument.AccessedAssemblies`), so internal view models work.

## Supported syntax

- Namespaces: `https://nexer.dev/ui` (maps `Nexer.Ui`, `.Controls`, `.Layout`, `.Menus`, `.Markup`, `.Data` via
  `XmlnsDefinitionAttribute`; any assembly can add its own), `clr-namespace:Ns;assembly=Asm`, `using:Ns`, and the
  XAML language namespace `http://schemas.microsoft.com/winfx/2006/xaml`.
- Elements, attribute and property-element syntax, content properties (`[Content]` on a property, or on the class
  with `Name`), collections (`IList`/`Add`, `IAddChild`, dictionaries with `x:Key`), attached properties
  (`Dock.Edge="Bottom"` → `Dock.SetEdge(target, value)`), events (`Click="Handler"` → a method on the root type,
  public or private), `[DeferredContent]` properties (become `Func<IServiceProvider?, object>` factories),
  `[UsableDuringInitialization]` types (attached to the parent before their own properties are set — top-down
  construction), `ISupportInitialize` (`BeginInit`/`EndInit`).
- Directives: `x:Class`, `x:Name`, `x:Key`, `x:ClassModifier`, `x:FieldModifier`, `x:Subclass` and `x:Uid` (the
  last four are consumed by the generator or ignored by the compiler).
- Markup extensions: `{StaticResource key}` / `{StaticResource ResourceKey=key}`, `{x:Null}`,
  `{x:Type Button}`, `{x:Static Command.Quit}` (static fields, properties and constants), `{Binding}` /
  `{RelativeSource}` (compiled, see below), and any type with a `ProvideValue(IServiceProvider)` method.
- `x:DataType="prefix:Type"` on any element: the DataContext type for `{Binding}`s on that element and its
  descendants (see [binding.md](binding.md)).

### `{StaticResource}`

`Nexer.Ui.Markup.StaticResourceExtension` looks for `IResourceProvider` (`TryGetResource(key, out value)`):

1. on the objects being constructed, innermost first (`IParentStackProvider.Parents`) — so `Window.Resources`
   declared before use are visible to descendants;
2. on the root object (`IRootObjectProvider`);
3. via `services.GetService(typeof(IResourceProvider))` (pass e.g. the `Application` in the services argument).

A missing key throws `KeyNotFoundException` naming the key and the target property.

### Value conversion

Attribute text is converted at **compile time** where possible (no parsing at runtime):

| Target type | Syntax | Emitted |
|-------------|--------|---------|
| primitives, `string`, `TimeSpan`, `Type`, … | XamlX built-ins | constants |
| enums (incl. `[Flags]`) | `Hidden`, `hidden`, `Left, Top`, `3` | constant; case-insensitive |
| `Nexer.Core.CellStyle` / `CellStyle?` | `bold white on blue`, `#102030 on default` (`StyleSyntax`) | `new CellStyle(fg, bg, flags)` |
| `Nexer.Core.TerminalColor` | `white`, `#FF8000`, `default` | `TerminalColor.Indexed(n)` / `Rgb(r,g,b)` / `Default` |
| `Rect` (namespace `Nexer*`) | `x,y,w,h` | `new Rect(int,int,int,int)` if that constructor exists, else `Rect.Parse` |
| `Thickness` (namespace `Nexer*`) | `1`, `1,2` (h,v), `l,t,r,b` | `new Thickness(l,t,r,b)` if that constructor exists, else `Thickness.Parse` |
| `KeyGesture` (namespace `Nexer*`) | `Ctrl+Q`, `Alt+Shift+F10`, `Ctrl++` | syntax validated at compile time, `KeyGesture.Parse` at runtime |
| `System.Windows.Input.ICommand` (e.g. `Button.Command`) | `Exit`, `Open` | `Nexer.Ui.Command.Parse(text)` at runtime (well-known instances such as `Command.Exit`, else a named `Command`) |
| any other type with `static T Parse(string)` | — | `T.Parse(text)` at runtime (e.g. `Command`) |

Mnemonic text such as `&amp;File` is plain string content (`&` must be escaped in XML).

### Compiled bindings

`{Binding}` is compiled to direct accessor calls (no reflection); the runtime semantics are in
[binding.md](binding.md).

```xml
<Window x:DataType="local:MainViewModel" ...>
  <Field Text="{Binding Customer.Name}" />                       <!-- TwoWay: Field.Text is [BindsTwoWayByDefault] -->
  <Label Text="{Binding Orders[0].Total, StringFormat=N2, FallbackValue=-}" />
  <Label Text="{Binding Text, ElementName=Search}" />
  <Label Text="{Binding Title, RelativeSource={RelativeSource FindAncestor, AncestorType=Window}}" />
  <List ItemsSource="{Binding Orders}" DisplayMemberBinding="{Binding Description}" />  <!-- item type inferred -->
</Window>
```

- **Properties**: `Path` (or the positional argument), `Mode` (`Default`, `OneWay`, `TwoWay`, `OneTime`,
  `OneWayToSource`), `Converter` (usually `{StaticResource}`), `ConverterParameter`, `StringFormat`,
  `FallbackValue`, `TargetNullValue` (strings or markup extensions), `UpdateSourceTrigger`, `ValidatesOnExceptions`,
  `ValidatesOnNotifyDataErrors`, `ElementName`, `RelativeSource` (`{RelativeSource Self}`,
  `{RelativeSource FindAncestor, AncestorType=T, AncestorLevel=n}`; `AncestorType` alone implies `FindAncestor`),
  `DataType` (overrides `x:DataType`).
- **Source type**: DataContext → the nearest `x:DataType` on the element or an ancestor (a `[DeferredContent]`
  property starts a new scope: templates need their own `x:DataType`), or `DataType=`; `Self` → the target element's
  type; `ElementName` → the named element's type (any `x:Name`/`Name` in the document, forward references allowed);
  `FindAncestor` → `AncestorType` (must be a `Widget` type).
- **Path grammar**:

  | Form | Example | Notes |
  |------|---------|-------|
  | source itself | `{Binding}`, `{Binding .}` | no nodes |
  | property | `Name`, `Customer.Address.City` | public instance properties; value-type owners allowed |
  | indexer | `Items[0]`, `Map[key]`, `Map['two words']` | int indexer preferred, then string (`this[]`, `IList<T>`, `IReadOnlyList<T>`, `IDictionary<string,T>`, arrays); node name `Item[]` |
  | attached property | `(local:Tag.Label)`, `(AnchorLayout.Anchor)` | static `GetLabel(owner)`; read-only |
  | cast | `((local:Dog)Pet).Breed` | changes the static type; checked at runtime by the next accessor |

  A path containing spaces or quotes must be quoted as a whole in the markup extension
  (`{Binding 'Map[two words]'}`).
- **Mode**: without `Mode` (or with `Mode=Default`) a target property marked `[BindsTwoWayByDefault]` gets `TwoWay`;
  otherwise `Default` is emitted and means `OneWay` at runtime.
- **Targets**: public settable instance properties of `Widget`s. Properties typed `IBinding`/`CompiledBinding`
  (`DisplayMemberBinding`, `ChildrenBinding`) receive the compiled binding itself, evaluated per item; their item type
  is `DataType=` or inferred from an `ItemsSource="{Binding …}"` on the same element (`IEnumerable<T>` → `T`).
- **Access**: view models may be internal to the assembly containing the XAML (build step) or to any assembly (SRE
  loader); the bound members must be public.
- **Commands**: `Command="{Binding SaveCommand}"` binds any `ICommand`; a literal `Command="Exit"` is parsed into a
  named `Command` (see *Value conversion*).

## Diagnostics

Errors are reported in the canonical MSBuild format, e.g.

```
Views\MainWindow.xaml(12,9): error NXAML0002: Invalid CellStyle value 'bold on nowhere': ...
```

The build step prints them, MSBuild's `Exec` turns them into build errors (clickable in IDEs), and the step stops
before replacing the assembly. `XamlLoader` throws `NexerXamlCompilationException` with the same diagnostics
(`XamlLoader.Validate` also returns warnings). Several errors in one file are reported together. Warnings
(`NXAML0100`, `NXAML0105`) are printed as `file(line,col): warning CODE: …` and surface as MSBuild warnings.

| Code | Meaning |
|------|---------|
| NXAML0001 | General XamlX error (unknown type/property, no matching constructor, bad markup extension, …) |
| NXAML0002 | Invalid attribute value (enum, `CellStyle`, `TerminalColor`, `Rect`, `Thickness`, `KeyGesture`) |
| NXAML0003 | `x:Class` type not found, not compatible with the root element, outside the compiled assembly, or used twice |
| NXAML0004 | Duplicate `x:Name` in a document |
| NXAML0005 | Malformed XML |
| NXAML0006 | Misplaced directive (e.g. `x:Class` on a non-root element) |
| NXAML0007 | Emit/build-step failure |
| NXAML0100 | Warning: use of an `[Obsolete]` member |
| NXAML0101 | `{Binding}` without an `x:DataType` in scope (or a per-item binding whose item type cannot be inferred) |
| NXAML0102 | Unknown member, indexer or attached property in a binding path |
| NXAML0103 | Binding path member is not public / not an instance property |
| NXAML0104 | TwoWay/OneWayToSource binding to a read-only path |
| NXAML0105 | Warning: binding source/target types have no built-in conversion and no `Converter` |
| NXAML0106 | Invalid binding target (not a `Widget`, no public setter, two-way without a public getter, not a property value, …) |
| NXAML0107 | Unknown `ElementName` |
| NXAML0108 | Invalid `RelativeSource` (missing or non-`Widget` `AncestorType`, combined with `ElementName`, …) |
| NXAML0109 | Invalid binding path syntax, unknown type in a cast/attached segment, impossible cast |
| NXAMLG001–4 | Generator warnings: `x:Class` not found, class not `partial`, `x:Name` field skipped (invalid identifier / clashes with a member), duplicate `x:Class` |

## Object-model assumptions

The configuration is built from `Nexer.Ui.Markup` contract types by **name** (it works with both the SRE and Cecil
type systems and has no dependency on concrete controls). The compiler relies on:

- `Nexer.Ui.Markup`: `XmlnsDefinitionAttribute`, `ContentAttribute`, `DeferredContentAttribute`,
  `UsableDuringInitializationAttribute`, `WhitespaceSignificantCollectionAttribute`,
  `TrimSurroundingWhitespaceAttribute`, `IRootObjectProvider`, `IProvideValueTarget`, `IUriContext`,
  `IParentStackProvider`, `IXmlNamespaceInfoProvider`/`XmlNamespaceInfo`, `IAddChild`, `IAddChild<T>`,
  `IResourceProvider`, plus the runtime helpers `NameScope` and `XamlComponent` (this branch).
- Element types need a public parameterless constructor (value types cannot be elements).
- `Name`: a public settable `string` property receives `x:Name`.
- Value types listed under *Value conversion* are matched by simple name in a namespace starting with `Nexer`; a
  `(int, int, int, int)` constructor enables compile-time construction of `Rect`/`Thickness`.
- `Nexer.Core.CellStyle(TerminalColor foreground, TerminalColor background, CellStyleFlags flags)` and
  `TerminalColor.Indexed(byte)`, `TerminalColor.Rgb(byte, byte, byte)`, `TerminalColor.Default`.

## Design decisions

- **Name scopes.** Every `x:Name` registers into the document root's scope (last registration wins at runtime).
  Duplicate names in a document are a compile error, including inside deferred content. Names inside templates
  are also registered into the root scope when the template is instantiated (the generator does not create fields
  for them).
- **`x:Class` + build.** In compiled assemblies `XamlComponent.Build` of an `x:Class` document calls the public
  parameterless (or `IServiceProvider`) constructor, which is expected to call `InitializeComponent`. The runtime
  loader constructs and populates instead.
- **Runtime parsing** remains for `KeyGesture` and `Command` (their semantics belong to Nexer.Ui), with compile-time
  syntax validation for `KeyGesture`.
- **`StyleSyntax`** is linked into the compiler as source so styles are parsed at compile time while the compiler
  still reads Nexer.Ui only as metadata (it references Nexer.Core for `CellStyle`/`TerminalColor`).
- **Incrementality.** XAML files are compile inputs, so the build step always rewrites a freshly compiled assembly;
  an already-processed assembly is detected and copied unchanged.

## Limitations

- `{Binding}` is compiled only (requires `x:DataType`; no reflection bindings, `MultiBinding`, method calls or
  multi-argument indexers in paths). No styles/templates beyond `[DeferredContent]` factories, or `x:Arguments`/`x:TypeArguments`
  constructors beyond what XamlX provides; no `x:Code`.
- Event handlers in documents *without* `x:Class` must be public (they are emitted into a helper type).
- The rewritten assembly is not re-signed (strong naming unsupported); no NuGet packaging yet (in-repo project
  references only).
- The SRE loader keeps compiled documents (dynamic assemblies) alive for the process lifetime (non-collectible).
- Hot reload is not wired up; use `XamlComponent.Fallback` + `XamlLoader` to experiment.

## Real-control coverage

`tests\Nexer.Ui.Xaml.Tests` compiles XAML in the real `https://nexer.dev/ui` namespace both at runtime (`XamlLoader`)
and at build time (the test assembly itself imports `Nexer.Xaml.targets`), then renders it through `HeadlessHost`.
`tests\Nexer.Xaml.Tests` keeps testing the toolchain against the fixture object model
(`https://nexer.dev/ui/test-fixtures`).

Idioms for the real controls:

- Lists, combo boxes: `<List><x:String>One</x:String></List>`; trees: `<TreeView><TreeItem Text="Root">…</TreeItem></TreeView>`.
- Menus live on the application: an `x:Class` root of type `Application` with `<Application.Menus><Menu Title="&amp;File">…`.
- Status bars: `<Window.StatusBar><StatusBar Text="…"><StatusKey Key="F1" Text="Help" Command="Help" /></StatusBar></Window.StatusBar>`.
- Colours: `<Window.OwnTheme><ColorTheme><ThemeColor Key="tlabel" Style="bold yellow on blue" /></ColorTheme></Window.OwnTheme>`.
- Layout: `<Window.Layout><AnchorLayout /></Window.Layout>` and `AnchorLayout.Anchor="Right, Bottom"` on children.
- A `{StaticResource}` used on the element that declares the resources is resolved before the property element is
  processed; reference resources from descendants (or the application) instead.

## Demo application notes

`samples\Nexer.Demo` is the reference app for the Cecil path (`build\Nexer.Xaml.targets` + generator); every window
is a `.xaml` + `.xaml.cs` pair and the build reports zero XAML diagnostics. Lessons from it:

- Generated `x:Name` fields are `internal`; the demo exposes them to `tests\Nexer.Demo.Tests` with
  `<InternalsVisibleTo Include="Nexer.Demo.Tests" />` rather than `x:FieldModifier="public"`.
- `Command="Open"` creates `new Command("Open")`; commands compare by name, so a code-behind
  `public static readonly Command Open = new("Open")` matches it in `Application.OnCommand`. Built-in commands use
  `{x:Static Command.Exit}` (either form works).
- Properties of derived roots work as property elements (`<Desktop.StatusBar>`, `<ScrollableWindow.StatusBar>`).
- An MSBuild XML comment in the csproj must not contain `--` (it breaks project loading, not XAML).
