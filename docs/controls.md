# Nexer.Ui controls (Agent A)

Port of Jexer's widget/window system (MIT, © Autumn Lamonte) to .NET with a XAML-first API.

## Progress

| Milestone | Status |
|---|---|
| a-core (Widget, WidgetCollection, DrawContext, Rect/Thickness/KeyGesture/Command, ColorTheme, HeadlessHost) | done |
| a-windowing (Window, Desktop, Application loop, z-order, modal) | done |
| a-menus (MenuBar/Menu/MenuItem/SubMenu/StatusBar/accelerators) | done |
| a-controls | pending |
| a-scrolling | pending |
| a-lists | pending |
| a-dialogs | pending |
| a-translucence | pending |

## Public API overview (for XAML authors)

All types are in XML namespace `https://nexer.dev/ui`.

### Nexer.Ui

- `Widget` — base of every element. `[Content] Children` (WidgetCollection), `Name`, `Tag`, `X`, `Y`, `Width`,
  `Height`, `Bounds` (`Rect`), `Enabled`, `Visible`, `TabStop`, `Layout` (LayoutManager), `Resources`
  (`ResourceDictionary`, `IResourceProvider` lookups walk parent → window → application). Implements
  `ISupportInitialize`: layout is suspended until the outermost `EndInit`.
- `Window` (non-sealed, XAML root) — `Title`, `Resizable`, `Modal`, `Centered`, `CloseBox`, `ZoomBox`,
  `HideOnClose`, `AbsolutePosition`, `Minimum/MaximumWidth/Height`, border styles, `OwnTheme`, `StatusBar`,
  `DialogResult`. Events `Closing` (cancelable), `Closed`, `Activated`, `Deactivated`. `Show()`, `Hide()`,
  `Close()`, `Close(result)`, `Task<object?> ShowModalAsync()`, `Task<T?> ShowModalAsync<T>()`, `Maximize()`,
  `Restore()`, `Center()`.
- `Desktop` — background behind all windows (`Application.Desktop`); can host widgets.
- `Application` — `Windows` (add to show), `Menus`, `Desktop`, `Theme`, `Resources`, `HideMenuBar`,
  `HideStatusBar`, `SmartPlacement`, `ConfirmExit`, `FocusFollowsMouse`, `DoubleClickTime`. Run with
  `app.Run(host)` (blocks) or `await app.RunAsync(host)` (dedicated UI thread). `Exit()`, `Post`, `Invoke`,
  `InvokeAsync`, `AddTimer`, `PostCommand`/`ExecuteCommand`, `OpenContextMenu`, `TileWindows`,
  `CascadeWindows`, `CloseAllWindows`. `Application.Current` is the last created/attached application.
- `DrawContext` — clip/offset drawing over a `CellSurface` (PutChar, PutString, HLine, VLine, Fill, DrawBox,
  DrawBoxShadow). Override `Widget.Draw(DrawContext)` for custom widgets.
- `ColorTheme` — all Jexer default keys (generated from `bits/ColorTheme.java` by
  `Themes/generate_theme_defaults.py`), `SetColor(key, "bold white on blue")`, `Load(TextReader)`.
- Parse-able value types: `Rect` (`"x,y,w,h"`), `Thickness` (`"1"`, `"h,v"`, `"l,t,r,b"`), `KeyGesture`
  (`"Ctrl+Q"`, `"Alt+F"`, `"F10"`, `"Shift+Tab"`, `"Ctrl+Plus"`), `Command` (`"Exit"`, `"Tile"`, any name),
  `BorderStyle` (`"Single"`, `"Double"`, `"SingleVDoubleH"`, `"SingleHDoubleV"`, `"Round"`, `"None"`).
- Mnemonics: `"&File"` underlines/accelerates `F`; write `&&` for a literal ampersand (`"Save && &Quit"`).

### Nexer.Ui.Layout

- `StretchLayout`, `BoxLayout` (`Orientation`), `AnchorLayout` with the attached property
  `AnchorLayout.Anchor` (`GetAnchor`/`SetAnchor`, `AnchorEdges` flags Left/Top/Right/Bottom). Set as
  `Window.Layout` (any widget's `Layout`).

### Nexer.Ui.Menus

- `Menu` (`Title` with mnemonic; content = items), `MenuItem` (`Text`, `Shortcut` KeyGesture, `Checkable`,
  `Checked`, `Icon`, `Command`, `Click` event), `MenuSeparator`, `SubMenu` (content = nested items).
  Add menus to `Application.Menus`. Alt+mnemonic or F10 opens the menu bar; accelerators work whenever no menu
  is open and no modal window is shown.
- `StatusBar` (`Text`, `[Content] Keys`) of `StatusKey` (`Key`, `Text`, `Command`, `Click`); set
  `Window.StatusBar` (the desktop's status bar is shown when there are no windows).

### Testing (Nexer.Ui.Testing)

- `HeadlessHost : ITerminalHost` with `ManualTimeProvider`, scripted input (`EnqueueKey`, `EnqueueGesture`,
  `EnqueueText`, `EnqueueClick`, `EnqueueDrag`, `EnqueuePaste`, `Resize`), captured `Frame` (`ScreenSnapshot`).
- `UiDriver` — attaches an application and pumps until idle after each step (`Press("Alt+F")`, `Type`,
  `Click`, `Drag`, `Advance(TimeSpan)`, `Text()`, `Row(y)`, `Style(x, y)`).
- `ScreenDump.Text` / `ScreenDump.Attributes`.

## Design notes

- Coordinates: Jexer's model. `X/Y` are relative to the parent's client origin; windows have a one-cell border
  (`ChildOffset` = 1), so children of a window at (0,0) draw at window column 1, row 1. Window positions are
  absolute screen positions once shown; positions authored before the window is added are desktop-relative
  (as Jexer's TWindow constructor adds the menu bar height).
- `Application.Windows[0]` is the front (active) window; drawing is back to front.
- Threading: one UI thread. `ProcessPending` drains input, posted work and timers, then draws if anything
  changed and calls `host.Present`. A `SynchronizationContext` posts continuations back to the loop, so
  `await` inside handlers resumes on the UI thread. Modal windows are just windows that capture input; there
  are no nested loops.
- Mnemonic passes for Alt+key inside windows: buttons, then labels, then radio buttons, then check boxes.

## Deviations from Jexer

- Initial focus: the first enabled tab stop added keeps the focus (Jexer activates the last added child).
- `Closing` can cancel a close; `Closed` fires afterwards and completes `ShowModalAsync`.
- F10 opens the first menu (Jexer only uses Alt+mnemonic).
- Cascade starts at the top of the desktop (Jexer starts at row 1 regardless of the menu bar).
- Smart placement: fixed the overlap-sum index bug (§9); the current position wins ties; windows not yet
  placed are ignored.
- `GetBorder`-style colour lookups have no side effects; mouse events are immutable records
  (`UiMouseEvent.RelativeTo`).
- Paste events go to the focused widget's `OnPaste` (Jexer sends them as keystrokes).
- A click that also moved the mouse still records the click time for double-click detection.
- Pressed close/zoom glyphs are drawn while the button is held (state cleared on release anywhere).

## Known gaps

- Controls, scrolling, lists, dialogs and translucence milestones are pending (see Progress).
- Exit confirmation always succeeds until the MessageBox milestone.
