# Nexer.Ui architecture

Nexer.Ui is a retained-mode, Turbo Vision / Jexer-style windowing framework for VT terminals
(xterm, Windows Terminal; keyboard + mouse) whose UI is authored in XAML and compiled by
[XamlX](https://github.com/kekekeks/XamlX). Jexer (MIT, © Autumn Lamonte) is the behavioural and
visual reference; Nexer.Core is the cell/Unicode foundation.

## Reference documents

These live outside this repository (paths relative to the Jexer checkout that contains `nexer\`, and to a
XamlX checkout):

| Document | Use |
|---|---|
| `..\src\jexer-windowing-analysis.md` (Jexer checkout) | Widget tree, coordinates, window chrome, event/draw order, z-order, layouts, translucence, Java defects (§9). |
| `..\src\jexer-rendering-analysis.md` (Jexer checkout) | Cell model, diff algorithm, SGR, input parsing, Windows console setup. |
| [`dotnet-vt-library-design.md`](../dotnet-vt-library-design.md) | Authoritative design for Rendering/Input/Hosting. Overrides Jexer where they differ. |
| [`README.md`](../README.md), [`COMPATIBILITY.md`](../COMPATIBILITY.md) | What Nexer.Core / Nexer.Emulation guarantee. |
| `docs/tui-framework-guide.md` in a XamlX checkout (not in the `external\XamlX` submodule) | XamlX mapping contract, object-model rules, gotchas. |
| Jexer sources: `..\src\jexer\**` (Jexer checkout) | Behavioural reference for ports. |

## Projects and ownership

The *Owner* column and the editing rule below record how the code was originally built: three parallel agents
plus a coordinator. They still describe each project's area of responsibility.

```
Core  <-  Input, Rendering  <-  Hosting  <-  Ui  <-  Ui.Testing
                                              ^
                                              +---  Xaml.Loader (runtime SRE)
Xaml.Compiler (XamlX + Cecil; reads Ui by metadata only)  <-  Xaml.Build (CLI)
Xaml.Generators (Roslyn, netstandard2.0, no project references)
```

| Project | Owner | Purpose |
|---|---|---|
| `Nexer.Core`, `Nexer.Emulation` | baseline (frozen) | Cells, surface, Unicode, VT scanner; headless emulator used as a test oracle. |
| `Nexer.Input` | Agent C | Byte-fed decoder producing `InputEvent`s. |
| `Nexer.Rendering` | Agent C | `CellSurface` → VT bytes (diff renderer). |
| `Nexer.Hosting` | Agent C | Console session (Windows/Unix), `ConsoleTerminalHost : ITerminalHost`. |
| `Nexer.Ui` (except `Markup\`, `Data\`) | Agent A | Widgets, windows, application, menus, themes, layout, drawing; DataContext, INPC, `ICommand` and `ItemsSource` support in the controls. |
| `Nexer.Ui\Data\` (namespace `Nexer.Ui.Data`) | Coordinator (contract) | Compiled data binding runtime: `CompiledBinding`, `BindingExpression`, `BindingOperations`, conversion, `Validation`. See [binding.md](binding.md). |
| `Nexer.Ui.Testing` | Agent A | `HeadlessHost : ITerminalHost`, screen dumps, scripted input. |
| `Nexer.Ui\Markup\` | Agent B | Markup extensions and XAML runtime helpers (contract file is coordinator-owned). |
| `Nexer.Xaml.Compiler`, `.Build`, `.Loader`, `.Generators`, `build\` | Agent B | XAML compilation and loading. |
| `samples\`, `docs\`, solution files | Coordinator | |

An agent may only edit its own projects and their test projects. If you need something in another
agent's area, write it up in your final report; the coordinator brokers it.

## Contracts (frozen; coordinator-owned)

Files marked `// CONTRACT:` may only be changed by the coordinator:

| File | Content |
|---|---|
| `src\Nexer.Input\InputEvents.cs` | `Key`, `KeyModifiers`, `MouseButton`, `MouseEventKind`, `InputEvent` records. |
| `src\Nexer.Hosting\ITerminalHost.cs` | `TerminalSize`, `CursorState`, `ITerminalHost`. |
| `src\Nexer.Ui\Markup\MarkupContracts.cs` | XAML marker attributes and service interfaces matched by XamlX. |
| `src\Nexer.Ui\Properties\XmlnsDefinitions.cs` | `https://nexer.dev/ui` → `Nexer.Ui`, `.Controls`, `.Layout`, `.Menus`, `.Markup`, `.Data`. |
| `src\Nexer.Ui\StyleSyntax.cs` | Jexer colour syntax → `CellStyle` (used by themes and the compile-time converter). |
| `src\Nexer.Ui\Data\*.cs` | Binding public surface (`IBinding`, `CompiledBinding`, `CompiledBindingPath`, `BindingExtension`, `BindingOperations`, `BindingExpression`, `Validation`, …). See [binding.md](binding.md). |

Additive extensions (new members) are possible by request; renames/removals are not.

### Input semantics
- Events are immutable records; re-base coordinates with `with { X = …, Y = … }`.
- Mouse coordinates are **zero-based cells**, relative to the host screen.
- `Key.Character` carries the already-shifted scalar in `Char`. Ctrl+letter = `Key.Character`,
  lower-case letter, `KeyModifiers.Ctrl`. Alt+x = `Key.Character`, `x`, `Alt`.
- `Timestamp` = the producing host's `TimeProvider.GetTimestamp()`. Double-click synthesis is a UI concern.
- Paste is never turned into keys. Unsolicited terminal replies are never surfaced as keys.

### Host semantics
- `Present(surface, cursor)` copies what it needs before returning; may coalesce; called only from the UI thread.
- `Input` completes when input ends; the application loop then exits.
- `Size` changes also arrive as `ResizeEvent`. `CellSurface` cannot resize: the UI allocates a new one.

## Coordinate model (from Jexer, preserved)

- Every widget's `X`, `Y` are relative to its parent's client origin.
- Windows and bordered panels offset their children by **+1,+1** (the border); a window's children at
  `X=0,Y=0` draw at the first interior cell.
- Drawing uses Jexer's clip/offset contract: each widget draws in its own local coordinates through a
  `DrawContext` holding an absolute offset and an absolute clip rectangle (intersection of all ancestors).
- Mouse events are re-based into the target widget's local coordinates before dispatch.

## Threading model

- One **UI thread** owns the entire widget tree and calls `ITerminalHost.Present`.
- `Application.Run` installs a single-threaded `SynchronizationContext` and pumps: input from
  `ITerminalHost.Input`, posted continuations, timers (via `TimeProvider`), then draws if invalidated.
- `ConsoleTerminalHost` reads input on a background task into a bounded `Channel<InputEvent>`
  (`InputCapacity`); the test `HeadlessHost` uses an unbounded channel fed by scripted input.
- Modal UI is `Task<T> ShowModalAsync()`; awaiting continues on the UI context. No nested loops,
  no secondary UI threads (this replaces Jexer's `yield()`).
- `Application.Invoke`/`InvokeAsync`/`Post` marshal work from other threads.
- Data binding: view-model notifications (`PropertyChanged`, `ErrorsChanged`, `CollectionChanged`,
  `CanExecuteChanged`) raised off the UI thread are marshalled with `Application.Post` before touching widgets.
  Widgets subscribe to view-model events only while `IsAttachedToTree` (their window belongs to an application),
  so closed windows are not kept alive by long-lived view models. See [binding.md](binding.md#threading-and-lifetime).

## XAML object-model rules (Agent A must follow; Agent B relies on them)

1. Public parameterless constructors; no parent/application arguments. Windows do not self-register:
   use `app.Windows.Add(w)` / `w.Show()`.
2. `Widget` is `[UsableDuringInitialization]` and implements `ISupportInitialize`; layout is suspended
   until the outermost `EndInit`.
3. Children: `[Content] public WidgetCollection Children { get; }` (get-only, non-null).
   Adding sets `Parent`/`Window` and applies Jexer `addChild` rules.
4. Settable CLR properties for everything authorable. Value types used in attributes expose
   `static T Parse(string)` (e.g. `Rect`, `Thickness`, `KeyGesture`) so XamlX can convert them.
5. Events are plain CLR events (`EventHandler` / `EventHandler<T>`). `Command` properties are
   `System.Windows.Input.ICommand?` (bindable; literal text becomes `Command.Parse`).
6. `[DeferredContent]` properties are typed `object` (templates, lazily built menus).
7. `Name` is a `string` property on `Widget`; name scopes are populated by the XAML compiler.
8. Attached properties: `public static T GetFoo(Widget)` / `SetFoo(Widget, T)` pairs.
9. Do not rely on constructor order between parent and children; finalize in `EndInit`.
10. Bindable properties raise `PropertyChanged` (`Widget : INotifyPropertyChanged`); properties users edit are
    `[BindsTwoWayByDefault]`. `DataContext` is inherited (see [binding.md](binding.md)).

## Rendering pipeline

```
Widget tree --DrawContext--> CellSurface (UI-owned back buffer)
            --ITerminalHost.Present--> host snapshot --Renderer.Prepare/Commit--> VT bytes --> terminal
```
The renderer keeps its own baseline and diffs; the UI does not track dirty cells. Width policy is
Nexer.Core's `UnicodeText`, used identically by drawing and rendering.

## Engineering rules

- `net10.0`, Nullable, `TreatWarningsAsErrors`; no warning suppressions without a comment.
- Before every commit: `dotnet build`, `dotnet test` (for your projects), and
  `dotnet format --verify-no-changes --exclude external`.
- Ported Jexer logic carries a header comment: `// Ported from Jexer <file> (MIT, © Autumn Lamonte).`
- Fix the Java defects in windowing-analysis §9 rather than porting them; assert invariants with `Debug.Assert`.
- Known pre-existing failure: `Nexer.Emulation.Tests.TerminalTests.ScalarEmulationHotPathDoesNotAllocate`
  (allocation-sensitive, baseline code). Ignore it.

## XamlX consumption

XamlX is a submodule at `external\XamlX` and its sources (`XamlX`, `XamlX.IL.Cecil`) are linked into
`Nexer.Xaml.Compiler`. `XAMLX_INTERNAL` is **not** defined: the Cecil backend's types are
unconditionally public and fail to compile against an internal core. XamlX's runtime interfaces are
therefore not used at runtime; Nexer maps its own equivalents from `Nexer.Ui.Markup`.
