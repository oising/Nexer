# Nexer

[![CI](https://github.com/oising/Nexer/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/oising/Nexer/actions/workflows/ci.yml)

Nexer is a .NET 10 text-mode windowing framework for VT terminals in the spirit of Turbo Vision and
[Jexer](https://gitlab.com/AutumnMeowMeow/jexer): overlapping windows, menus, dialogs, a status bar and a
widget set, with the UI authored in XAML that is compiled at build time by [XamlX](https://github.com/kekekeks/XamlX).
Underneath sit reusable, dependency-free VT layers (cell surfaces, an input decoder, a diffing renderer, a headless
terminal emulator and a console host) that can be used on their own.

## Project map

| Project | What it is |
|---------|------------|
| `src\Nexer.Core` | Cells, colours, styles, `CellSurface`, Unicode grapheme segmentation/width, `VtScanner`. |
| `src\Nexer.Input` | VT input decoder: keys, mouse, paste, focus and resize events. |
| `src\Nexer.Rendering` | `VtRenderer`: diffs cell surfaces into minimal VT output (true colour/256/16 colours). |
| `src\Nexer.Emulation` | Headless terminal emulator (`Terminal`) that interprets VT output into a screen model. |
| `src\Nexer.Hosting` | `ConsoleTerminalHost`: raw mode, alternate screen, input/resize pumps, restore on exit. |
| `src\Nexer.Ui` | The widget toolkit (port of Jexer): `Application`, `Window`, controls, menus, dialogs, layouts, XAML runtime types (`Nexer.Ui.Markup`), compiled data binding (`Nexer.Ui.Data`). |
| `src\Nexer.Ui.Testing` | `HeadlessHost` / `UiDriver`: run an application without a terminal, script input, inspect frames. |
| `src\Nexer.Xaml.Compiler` | XamlX-based compiler for the `https://nexer.dev/ui` namespace. |
| `src\Nexer.Xaml.Build` + `build\Nexer.Xaml.targets` | Build step that compiles `.xaml` into the app assembly (Cecil). |
| `src\Nexer.Xaml.Generators` | Source generator: typed `x:Name` fields and `InitializeComponent()`. |
| `src\Nexer.Xaml.Loader` | Runtime (System.Reflection.Emit) XAML loader. |
| `samples\Nexer.Demo` | XAML port of Jexer's demo application. |
| `samples\Nexer.Backend.Sample` | Low-level host/input/renderer sample without the widget toolkit. |
| `tests\*` | xUnit tests per project, plus `Nexer.Ui.Xaml.Tests` (XAML against real controls) and `Nexer.Demo.Tests` (end-to-end). |

Design documents: [architecture](docs/architecture.md), [controls](docs/controls.md), [XAML](docs/xaml.md),
[data binding](docs/binding.md), [backend](docs/backend.md), [compatibility](COMPATIBILITY.md).

## NuGet packages

Each runtime assembly ships as its own package; dependencies between them flow automatically.

| Package | Use it for |
|---------|------------|
| `Nexer.Core` | Cells, colours, styles, `CellSurface`, Unicode width, `VtScanner`. |
| `Nexer.Input` | Decoding VT input. |
| `Nexer.Rendering` | Rendering cell surfaces to VT output. |
| `Nexer.Hosting` | Running in a real console (`ConsoleTerminalHost`). |
| `Nexer.Emulation` | The headless terminal emulator. |
| `Nexer.Ui` | The widget toolkit (brings in Core, Input and Hosting). |
| `Nexer.Ui.Testing` | Driving an application headlessly in tests. |
| `Nexer.Xaml` | Build-time XAML: compiles `.xaml` into the assembly and generates code-behind members (development dependency). |
| `Nexer.Xaml.Loader` | Loading XAML text at runtime. |

## Running the demo

```powershell
dotnet run --project samples\Nexer.Demo
```

Run it in a VT terminal such as Windows Terminal. F10 or Alt+letter opens the menus, F6 switches windows, Ctrl+W
closes a window and Alt+X exits (with confirmation). The demo windows cover buttons, check boxes, radio groups,
text and password fields, combo boxes, spinners, progress bars, lists, trees, directory lists, message boxes, input
boxes, the file open box, a scrollable window, split panes, translucent windows and an MVVM form with data binding
(Demo → Data binding).

`dotnet run --project samples\Nexer.Demo -- --smoke` builds every demo window headlessly, renders one frame of each
and exits 0 (add `--dump` to print the frames).

## Building an app with XAML

Reference the toolkit and the build-time XAML package:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Nexer.Ui" Version="1.0.0-preview.1" />
    <PackageReference Include="Nexer.Xaml" Version="1.0.0-preview.1" />
  </ItemGroup>
</Project>
```

When building against this repository's sources instead, reference the build step and the generator as projects and
import the targets (see [docs/xaml.md](docs/xaml.md)):

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\Nexer.Ui\Nexer.Ui.csproj" />
  <ProjectReference Include="..\..\src\Nexer.Hosting\Nexer.Hosting.csproj" />
  <ProjectReference Include="..\..\src\Nexer.Xaml.Build\Nexer.Xaml.Build.csproj"
                    ReferenceOutputAssembly="false" OutputItemType="NexerXamlBuildTool" />
  <ProjectReference Include="..\..\src\Nexer.Xaml.Generators\Nexer.Xaml.Generators.csproj"
                    ReferenceOutputAssembly="false" OutputItemType="Analyzer" />
</ItemGroup>
<Import Project="..\..\build\Nexer.Xaml.targets" />
```

Every `*.xaml` file is compiled into the assembly. Pair it with a partial code-behind class:

```xml
<Window xmlns="https://nexer.dev/ui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="MyApp.HelloWindow" Title="Hello" Bounds="0,0,30,8" Centered="True">
  <Label X="2" Y="1" Text="Hello, Nexer!" />
  <Button x:Name="OkButton" X="10" Y="3" Text="&amp;OK" Click="OnOk" />
</Window>
```

```csharp
public partial class HelloWindow : Window
{
    public HelloWindow() => InitializeComponent();   // generated
    private void OnOk(object? sender, EventArgs e) => Close();
}

// Program.cs
var app = new Application();
app.Windows.Add(new HelloWindow());
await using var host = await ConsoleTerminalHost.StartAsync(new ConsoleTerminalHostOptions());
await app.RunAsync(host);
```

`x:Name` fields are `internal` (use `InternalsVisibleTo` for tests). An `x:Class` root of type `Application` declares
the menu bar and desktop. See [docs/xaml.md](docs/xaml.md) for markup extensions, value conversion and diagnostics
(XAML errors fail the build with file/line positions), and `samples\Nexer.Demo` for a complete application.

Applications are testable without a terminal:

```csharp
var driver = new UiDriver(new MyApplication(), 80, 25);
driver.Press("Alt+F");
Assert.Contains("Exit", driver.Text());
```

### MVVM and data binding

Widgets have an inherited `DataContext` and XAML `{Binding}`s are compiled against `x:DataType` (no reflection;
unknown members are build errors). Two-way bindings, converters, `StringFormat`, `ElementName`/`RelativeSource`,
validation (`INotifyDataErrorInfo` and conversion errors), `ItemsSource`/`ObservableCollection` and `ICommand`
are supported, and view-model changes from other threads are marshalled to the UI thread:

```xml
<Window xmlns="https://nexer.dev/ui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:MyApp" x:Class="MyApp.PersonWindow" x:DataType="local:PersonViewModel">
  <Field Text="{Binding Name}" Width="20" />
  <List Y="2" Height="5" ItemsSource="{Binding People}" DisplayMemberBinding="{Binding Name}"
        SelectedItem="{Binding Selected}" />
  <Button Y="8" Text="&amp;Remove" Command="{Binding RemoveCommand}" CommandParameter="{Binding Selected}" />
</Window>
```

```csharp
app.Windows.Add(new PersonWindow { DataContext = new PersonViewModel() });
```

See [docs/binding.md](docs/binding.md).

## Build and test

Run from this directory using the .NET 10 SDK:

```powershell
dotnet build .\Nexer.slnx
dotnet test .\Nexer.slnx
dotnet format .\Nexer.slnx --verify-no-changes --exclude external
```

Clone with `--recurse-submodules` (XamlX lives in `external/XamlX`). Pull requests are gated by
`.github/workflows/ci.yml`, which runs build, tests and the demo `--smoke` run on Windows and Ubuntu plus the format
check. To make these checks required, enable branch protection on `main` and add the `Build & test (windows-latest)`,
`Build & test (ubuntu-latest)` and `Format check` status checks.

`dotnet pack .\Nexer.slnx -c Release -o artifacts\pkg` produces the NuGet packages (CI uploads them as the `packages`
artifact). Pushing a `v*` tag (for example `v1.0.0-preview.1`) runs `.github/workflows/release.yml`, which packs with
that version and publishes to nuget.org via trusted publishing (OIDC; nuget.org policy `nexer-release` for
`release.yml`, so no API key secret is stored). The default version is set in
`Directory.Build.props`.

The libraries have no third-party runtime dependencies besides XamlX/Mono.Cecil in the XAML build tooling. Tests use
xUnit. Generated Unicode data and the official fixture ship with the Unicode license and attribution; see
`THIRD-PARTY-NOTICES.txt`. Regeneration requires Python and network access (`python .\tools\generate_unicode.py`),
not normal builds/tests. Ported Jexer code is MIT © Autumn Lamonte.

## Headless emulator usage

`Nexer.Emulation` can be used on its own to interpret application output:

```csharp
using Nexer.Emulation;

var terminal = new Terminal(80, 24);
terminal.Feed("hello \u001b[32mworld\u001b[0m\r\n"u8);
terminal.CompleteInput(); // Only at EOF, not after each read.
string text = terminal.Screen.GetText(0, 1);
```

It is not a complete xterm implementation; see [COMPATIBILITY.md](COMPATIBILITY.md) for precise support.
## Core and emulation contracts

`CellSurface`, `VtScanner`, and `Terminal` require a single owner; they are not thread-safe.
Row spans must not be retained across mutations. Surface versions are monotonic,
and row generations are never consumed/reset by readers. A new renderer must
establish its own baseline even when every row is still at generation zero.
This increment tracks whole dirty rows, not minimal intervals.

`Write` accepts one non-control scalar; `WriteCluster` accepts one presegmented
cluster. Both take width 1 or 2. Low-level surface callers supply width;
`Terminal` segments/measures automatically. At the right edge, a width-2
write becomes a styled blank. Replacing either half of a wide glyph blanks its
partner with default style. Geometry is validated against a configurable cell
limit before allocation. Cells default to a space with default terminal colors.

The scanner does not interpret parameters numerically or execute commands.
CSI parameter bytes retain private prefixes, omitted values, and colon syntax.
DCS callbacks contain the entire body after `ESC P`, including its raw header.
String payloads are bytes, not automatically decoded text.

Callback spans are borrowed until that callback returns. Callbacks must not
reenter the scanner; exceptions from a sink should be treated as a failed stream
and that scanner discarded. There are no I/O operations, timers, or background
tasks. `Complete` is explicit EOF, not an ordinary read boundary, and resets
partial sequence state so the instance can then process a fresh stream.

OSC ends at BEL or ST; DCS/SOS/PM/APC end at ST. CAN/SUB cancel sequences and are
consumed rather than emitted as input keys. DEL is ignored outside strings.
ESC followed by a non-backslash byte inside a string remains opaque payload;
only CAN/SUB or a proper terminator ends it. This conservative policy prevents
unsupported or overflowing strings from leaking embedded commands into text.
Over-limit strings are discarded in full, while over-limit/malformed sequence
headers are ignored through the next final byte. Raw eight-bit C1 bytes are not
control introductions in this UTF-8 profile.

The scanner bounds raw header bytes rather than allocating numeric parameter
objects. The emulator additionally bounds numeric values and
parameter counts. Invalid UTF-8 uses replacement characters; incomplete UTF-8 is
preserved across feeds and replaced at a control boundary or EOF.
