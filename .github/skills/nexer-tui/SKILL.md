---
name: nexer-tui
description: Build text-mode (TUI) applications for VT terminals with Nexer, a .NET 10 Turbo Vision/Jexer-style windowing framework whose UI is authored in compiled XAML. Use when creating or modifying a Nexer app - windows, dialogs, menus, status bars, controls (Field, List, TreeView, ComboBox, SplitPane...), MVVM data binding with {Binding}/x:DataType, custom widgets that draw with DrawContext, or headless UI tests with UiDriver. Triggers - "Nexer", "Nexer.Ui", "TUI in .NET", "terminal UI app", "text-mode windows", "Turbo Vision style", "https://nexer.dev/ui".
---

# Building TUI apps with Nexer

Nexer is a retained-mode windowing toolkit for VT terminals (overlapping windows, menu bar, status bar, modal
dialogs, keyboard + mouse). UIs are `.xaml` + partial code-behind pairs **compiled at build time** (XAML and binding
errors fail `dotnet build` with `file(line,col): error NXAML....`). Apps run in a real console via
`ConsoleTerminalHost` and are tested **headlessly** via `UiDriver` - you cannot interact with the terminal, so verify
with builds, headless tests and a `--smoke` switch, never by launching the app and waiting.

## Workflow

1. **Scaffold** from [assets/template](assets/template) (copy the folder, rename `MyApp`): app csproj, `Program.cs`
   with `--smoke`, an `Application` XAML root with menus, one window + view model, and an xUnit test project.
   - NuGet (outside this repo): `Nexer.Ui` + `Nexer.Xaml` (build-time, dev dependency); tests add `Nexer.Ui.Testing`.
     Version `1.0.0-preview.1`.
   - Inside the Nexer repo: use project references + `<Import Project="...\build\Nexer.Xaml.targets" />` instead -
     copy the shape of `samples\Nexer.Demo\Nexer.Demo.csproj`.
2. **Author UI** as `Views\FooWindow.xaml` + `Views\FooWindow.xaml.cs` (`public partial class FooWindow : Window`,
   constructor calls `InitializeComponent()`). Look up controls in [references/controls.md](references/controls.md).
3. **Bind** with MVVM: `x:DataType` on the root + `{Binding}`; set `DataContext` in code. See
   [references/xaml-and-binding.md](references/xaml-and-binding.md).
4. **Build** (`dotnet build`) and fix every `NXAML` diagnostic at the reported XAML line.
5. **Test headlessly** with `UiDriver` (keys, typing, clicks, timers, screen text/style assertions) and run
   `dotnet run -- --smoke`. See [references/testing-and-custom-widgets.md](references/testing-and-custom-widgets.md).

## Minimal shape

```xml
<!-- Views/HelloWindow.xaml -->
<Window xmlns="https://nexer.dev/ui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:MyApp.Views" x:Class="MyApp.Views.HelloWindow"
        x:DataType="local:HelloViewModel" Title="Hello" Width="40" Height="10" Centered="True">
  <Label Text="Name:" X="1" Y="1" />
  <Field x:Name="NameField" X="8" Y="1" Width="20" Text="{Binding Name}" />
  <Label X="1" Y="3" Width="36" Text="{Binding Greeting}" />
  <Button Text="&amp;OK" X="15" Y="5" Click="OnOk" />
</Window>
```

```csharp
public partial class HelloWindow : Window
{
    public HelloWindow() : this(new HelloViewModel()) { }   // keep a parameterless ctor
    public HelloWindow(HelloViewModel vm) { InitializeComponent(); DataContext = vm; }
    private void OnOk(object? sender, EventArgs e) => Close();   // handlers may be private
}

// Program.cs
await using var host = await ConsoleTerminalHost.StartAsync(new ConsoleTerminalHostOptions());
var app = new MyApplication();              // Application subclass (XAML root) or plain new Application()
app.Windows.Add(new HelloWindow());
await app.RunAsync(host);                   // returns when the app exits or input ends
```

## Rules that prevent most mistakes

**XAML**
- Root namespaces: `xmlns="https://nexer.dev/ui"`, `xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"`,
  `xmlns:local="clr-namespace:My.Ns"`. `x:Class` must name a `partial` class deriving from the root element type.
- Escape `&` as `&amp;`. `"&amp;File"` = mnemonic F (Alt+F); `&amp;&amp;` = literal `&`.
- `x:Name` creates an `internal` typed field (no field for the root or names inside deferred content). Expose to
  tests with `<InternalsVisibleTo Include="MyApp.Tests" />`.
- List/combo items as content: `<List><x:String>One</x:String></List>`. Do not mix `Items` content and
  `ItemsSource` (throws `InvalidOperationException`).
- Value syntax: `Bounds="x,y,w,h"`, `Thickness` `"1"`/`"h,v"`/`"l,t,r,b"`, `Shortcut="Ctrl+O"`, `Style="bold
  white on blue"` / `"#FF8000 on default"`, enums case-insensitive, flags comma-separated (`"Left, Top"`).
- The read-only word-wrapped `Text` control uses `Content`, not `Text`.
- Every `.xaml` in the project is compiled; no `x:Code`, styles, triggers or data templates (list items are text).

**Layout and coordinates**
- Default layout is absolute: `X`, `Y`, `Width`, `Height` relative to the parent's client area. A window's border
  takes one cell, so a child at `X="0" Y="0"` draws at the window's first interior cell; a `Width="40"` window has 38
  usable columns and `Height-2` usable rows.
- Size windows explicitly (`Width`/`Height`) and use `Centered="True"`. The menu bar uses row 0 and the status bar
  the last row - design for 80x25 unless told otherwise.
- `Button` is `label + 3` wide and 2 tall (shadow): leave a blank row below buttons.
- A single `Panel` or `SplitPane` child fills the window automatically. For resize-aware layouts use
  `<Window.Layout><AnchorLayout /></Window.Layout>` + `AnchorLayout.Anchor="Right, Bottom"` on children, or
  `BoxLayout`/`StretchLayout`.

**Behaviour**
- Windows do not self-register: `app.Windows.Add(w)`. `app.Windows[0]` / `app.ActiveWindow` is the front window.
- Modal dialogs are async, never blocking: `await MessageBox.ShowAsync(app, "Title", "Line1\nLine2",
  MessageBoxButtons.YesNo)`, `await InputBox.ShowAsync(...)`, `await FileOpenBox.ShowAsync(...)`,
  `await window.ShowModalAsync<T>()` (+ `Close(result)`). From a sync event handler use `_ = SomeAsync();` - `await`
  resumes on the UI thread.
- One UI thread owns all widgets. From background work use `app.Post(...)` / `app.InvokeAsync(...)`; view-model
  `PropertyChanged`/`CollectionChanged` raised off-thread are marshalled automatically.
- Timers: create in `protected override void OnAddedToApplication()` via `Application!.AddTimer(interval, action,
  recurring: true)`, dispose in `OnClosed()`. Never use `Thread.Sleep`/`System.Threading.Timer` to drive UI.
- Commands: built-ins `Exit` (asks to confirm unless `ConfirmExit = false`), `Help`, `Menu`, `Tile`, `Cascade`,
  `CloseAll`, `WindowClose`, `WindowNext`, `WindowPrevious`, `WindowMove`, `WindowZoom`. Custom named commands
  (`Command="Open"`) reach `protected override bool OnCommand(Command c)` on the `Application`; compare with
  `public static readonly Command Open = new("Open");`. For MVVM bind an `ICommand`: `Command="{Binding SaveCommand}"`.
- Key routing: the active window's mnemonics (buttons, then labels, radios, check boxes) beat menu `Alt+letter`.
  Pick button mnemonics that do not collide with menu titles.
- Framework hooks (`Draw`, `OnKeypress`, `OnAddedToApplication`, `OnClosed`, ...) are `protected internal virtual`
  in Nexer.Ui; override them in apps as `protected override`.

**Binding** (compiled, no reflection)
- Requires `x:DataType` in scope, else `NXAML0101`. Bound members must be public instance properties; the view model
  type itself may be `internal`.
- Two-way by default for user-editable properties (`Field.Text`, `CheckBox.Checked`, `RadioGroup.Selected`,
  `List/ComboBox.SelectedIndex|SelectedItem`, `ComboBox.Text`, `TreeView.SelectedItem`, scroller values,
  `SplitPane.Split`); everything else is one-way.
- View models implement `INotifyPropertyChanged`; collections use `ObservableCollection<T>`. Raise notifications
  for computed properties yourself.
- Lists: `ItemsSource="{Binding People}" DisplayMemberBinding="{Binding Name}" SelectedItem="{Binding Selected}"` -
  the item type is inferred from `ItemsSource`.

## Verify before finishing

```powershell
dotnet build                             # 0 NXAML errors/warnings
dotnet test                              # UiDriver tests
dotnet run --project MyApp -- --smoke    # builds every window headlessly, exits 0
```

`ConsoleTerminalHost.StartAsync` throws `InvalidOperationException` when stdin/stdout are redirected (as in agent
shells). That is expected: use `--smoke` and tests instead of running the interactive app.

## Further reading (in the Nexer repo, github.com/oising/Nexer)

`docs/controls.md` (full control API, deviations from Jexer), `docs/xaml.md` (compiler, diagnostics, runtime
`XamlLoader`), `docs/binding.md` (binding semantics), `docs/backend.md` (host options, input decoding),
`samples/Nexer.Demo` (complete reference app: every control, dialogs, MVVM window, scrollable canvas, timers).
