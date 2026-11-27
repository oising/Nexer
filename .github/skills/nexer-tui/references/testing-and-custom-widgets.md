# Testing and custom widgets

## Headless tests with UiDriver

Package `Nexer.Ui.Testing` (namespace `Nexer.Ui.Testing`). `UiDriver` attaches an `Application` to a
`HeadlessHost` (default 80x25, `ManualTimeProvider`) and pumps until idle after every step - no threads, no sleeps.

```csharp
using Nexer.Input;
using Nexer.Ui;
using Nexer.Ui.Testing;

public class MainWindowTests
{
    [Fact]
    public void TypingUpdatesTheViewModel()
    {
        var vm = new MainViewModel();
        var app = new App();                                   // Application subclass
        app.Windows.Add(new MainWindow { DataContext = vm });
        var d = new UiDriver(app, 80, 25);

        d.Type("Ada");                                         // focused Field receives the text
        d.Press("Tab");
        d.Press("Enter");                                      // activates the focused button
        Assert.Equal("Ada", vm.Name);
        Assert.Contains("Hello, Ada", d.Text(), StringComparison.Ordinal);
    }
}
```

| Driver member | Use |
|---|---|
| `Press("Alt+F")`, `Key(Key.Down)`, `Key(Key.Character...)` | Key gestures (same syntax as `KeyGesture`). |
| `Type("text")`, `Paste("text")` | Typed characters / bracketed paste. |
| `Click(x, y)`, `DoubleClick(x, y)`, `Drag(x1, y1, x2, y2)`, `Mouse(kind, x, y)` | Mouse at absolute screen cells (zero-based). |
| `Advance(TimeSpan)` | Moves the manual clock and fires due `AddTimer` timers. |
| `Resize(cols, rows)` | Terminal resize. |
| `Text()`, `Text(x, y, w, h)`, `Row(y)`, `Cell(x, y)` | Screen text (rows trimmed at the end). |
| `Style(x, y)`, `Theme("tlabel")` | Cell style / theme colour for colour assertions. |
| `Frame` (`ScreenSnapshot`), `Host`, `Application`, `Pump()` | Low-level access. |

Patterns:

- Find text before clicking it: scan `d.Row(y).IndexOf(text)` over `d.Frame.Rows` (see
  `tests/Nexer.Demo.Tests/DemoEndToEndTests.cs` `Find`). Print `d.Text()` in assertion messages - it is the fastest
  way to see what the screen looks like.
- Screen layout at 80x25: row 0 = menu bar, last row = status bar of the active window (or desktop).
- Dialogs: trigger them, then assert on `app.ActiveWindow` (`Assert.IsType<MessageBox>(...)`) and dismiss with keys
  (`d.Press("Enter")`, `d.Press("Y")`, `d.Press("Escape")`). Awaited continuations run during the next pump.
- `Command.Exit` shows a confirmation; set `app.ConfirmExit = false` in tests that exit.
- Named `x:Name` fields are `internal`: add `<InternalsVisibleTo Include="MyApp.Tests" />` to the app csproj.
- Unit-test view models directly; use `UiDriver` for wiring (bindings, commands, focus, key routing, layout).
- `ScreenDump.Attributes(d.Frame, x, y, w, h)` gives a lettered style grid with a legend, which is useful for colour assertions.
- Visual review/docs: `File.WriteAllText("main.svg", ScreenDump.Svg(d.Frame, new SvgOptions { Title = "Main" }))`
  renders the frame with real colours and box-drawing (deterministic output). This needs a `Nexer.Ui.Testing` newer than
  `1.0.0-preview.1`; `samples/Nexer.Demo -- --screenshots <dir>` shows how to use it.

## Smoke mode

Give `Program.cs` a `--smoke` switch that constructs every window, renders one frame headlessly and exits 0. It
catches XAML runtime failures, binding exceptions and layout overflow in CI and in agent shells where the real
console is unavailable:

```csharp
if (args.Contains("--smoke"))
{
    foreach (Func<Window> create in new Func<Window>[] { () => new MainWindow(), () => new SettingsWindow() })
    {
        var app = new App();
        app.Windows.Add(create());
        var d = new UiDriver(app, 80, 25);
        if (args.Contains("--dump")) Console.WriteLine(d.Text());
    }
    Console.WriteLine("smoke: OK");
    return 0;
}
```

(The app project then references `Nexer.Ui.Testing` too, as `samples/Nexer.Demo` does.)

## Custom widgets

Subclass `Widget` (or a control), draw in local coordinates, react to input:

```csharp
using System.Text;
using Nexer.Input;
using Nexer.Ui;

public sealed class Meter : Widget
{
    private int value;

    public Meter() { Height = 1; TabStop = true; }

    public int Value { get => value; set => SetProperty(ref this.value, Math.Clamp(value, 0, 100)); }

    protected override void Draw(DrawContext context)
    {
        var style = GetColor(IsActive ? "tbutton.active" : "tbutton.inactive");
        int filled = Width * value / 100;
        context.HLine(0, 0, filled, '█', style);
        context.HLine(filled, 0, Width - filled, '░', style);
        if (IsActive) context.PutString(0, 0, $"{value}%", style);
    }

    protected override void OnKeypress(KeyEvent key)
    {
        if (key.Key == Key.Right) { Value += 5; return; }
        if (key.Key == Key.Left) { Value -= 5; return; }
        base.OnKeypress(key);                                  // keep Tab/arrows focus navigation
    }

    protected override void OnMouseDown(UiMouseEvent mouse)
    {
        Value = (mouse.X + 1) * 100 / Math.Max(1, Width);      // mouse.X/Y are local to the widget
        base.OnMouseDown(mouse);
    }
}
```

Use it from XAML via its CLR namespace: `xmlns:c="clr-namespace:MyApp.Controls"` then
`<c:Meter X="1" Y="3" Width="20" Value="{Binding Volume}" />` (properties set via `SetProperty` notify, so they are
bindable; mark user-edited properties `[Nexer.Ui.Data.BindsTwoWayByDefault]`).

- Hooks (override as `protected override`): `Draw`, `AfterDrawChildren`, `OnKeypress`, `OnMouseDown`, `OnMouseUp`,
  `OnMouseDoubleClick`, `OnMouseMotion`, `OnPaste`, `OnCommand`, `OnIdle`, `OnSizeChanged`, `OnAttachedToTree`,
  `OnDetachedFromTree`, `OnInitialized`; windows also `OnAddedToApplication`, `OnClosing` (return false to cancel),
  `OnClosed`, `OnActivated`, `OnDeactivated`, `OnScreenResized`, `IsShortcutKey`.
- `DrawContext`: `PutChar`, `PutString` (returns columns used; width-aware for CJK/emoji), `HLine`, `VLine`, `Fill`,
  `Clear`, `DrawBox`, `DrawBoxShadow`, `PutAttr`, `PutForeground*` (keep existing background). Drawing is clipped to
  the widget and its ancestors automatically.
- Colours: `GetColor("theme.key")` (widget-protected) or `Theme.GetColor(key)`; raw styles via
  `new CellStyle(TerminalColor.Rgb(r,g,b), TerminalColor.Default, CellStyleFlags.Bold)` (namespace `Nexer.Core`).
- Cursor: set `CursorVisible`, `CursorX`, `CursorY` (local) for text-entry widgets.
- The UI loop redraws after every processed input event, timer tick or posted action, so state changed in those
  needs no explicit refresh. Use `SetProperty` for bindable properties (it raises `PropertyChanged`); call
  `Application?.Invalidate()` only for state changed outside the loop.
- Container widgets that are a single focus target should not have children; composite widgets add children in
  their constructor (`Children.Add(...)`).
- Background work: `await Task.Run(...)` from a UI handler returns to the UI thread; for fire-and-forget producers
  use `app.Post(() => ...)`.

## Low-level (no widget toolkit)

For full-screen custom rendering without windows use `Nexer.Hosting` + `Nexer.Core` directly: draw into a
`CellSurface`, call `host.Present(surface, cursor)`, and read `host.Input` (`KeyEvent`, `MouseEvent`,
`ResizeEvent`, `PasteEvent`). See `docs/backend.md` and `samples/Nexer.Backend.Sample`.
