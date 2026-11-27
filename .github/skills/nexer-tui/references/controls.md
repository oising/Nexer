# Nexer controls cheat sheet

All types are in `xmlns="https://nexer.dev/ui"` (C# namespaces `Nexer.Ui`, `Nexer.Ui.Controls`, `Nexer.Ui.Layout`,
`Nexer.Ui.Menus`). Every widget has `X`, `Y`, `Width`, `Height`, `Bounds="x,y,w,h"`, `Name`, `Tag`, `Enabled`,
`Visible`, `TabStop`, `DataContext`, `Resources`, `Layout`. `(2W)` = two-way binding by default.

## Windows

| Element | Key properties / members | Notes |
|---|---|---|
| `Window` | `Title`, `Width`, `Height`, `Centered`, `Modal`, `Resizable`, `CloseBox`, `ZoomBox`, `HideOnClose`, `StatusBar`, `OwnTheme`, `Layout`, `Minimum/MaximumWidth/Height`, `DialogResult` | Events `Closing` (cancelable), `Closed`, `Activated`, `Deactivated`. `Show/Hide/Close/Close(result)/Maximize/Restore/Center`, `ShowModalAsync()`/`ShowModalAsync<T>()`. Children start inside the 1-cell border. |
| `ScrollableWindow` | `HasVerticalScroller`, `HasHorizontalScroller`, `VerticalValue` (2W), `HorizontalValue` (2W), `VerticalScroller`, `HorizontalScroller`, `ToHome()`, `ToEnd()` | Virtual canvas: override `ReflowData()` (set scroller `Maximum`/`BigChange`), `Draw` (offset by values) and `OnKeypress`. See `samples/Nexer.Demo/Windows/DemoScrollableWindow.xaml.cs`. |
| `Application` (XAML root) | `Menus`, `Desktop`, `Resources`, `Theme`, `DataContext`, `HideMenuBar`, `HideStatusBar`, `ConfirmExit` (true), `SmartPlacement`, `FocusFollowsMouse`, `Translucence` (false) | Code: `Windows`, `ActiveWindow`, `ActiveMenu`, `Run(host)`, `RunAsync(host)`, `Exit()`, `Post`, `Invoke`, `InvokeAsync`, `AddTimer`, `ExecuteCommand`, `PostCommand`, `OpenContextMenu(menu, x, y)`, `TileWindows`, `CascadeWindows`, `CloseAllWindows`, `ScreenWidth/Height`; override `OnCommand(Command)`, `OnKeypress(KeyEvent)`. |
| `Desktop` | `StatusBar` | Background behind windows; its status bar shows when no window is open. |

## Input controls

```xml
<Label Text="&amp;Name:" X="1" Y="1" />                          <!-- Alt+N raises Click; never focused -->
<Field x:Name="Name" X="10" Y="1" Width="20" Text="{Binding Name}" Submitted="OnEnter" />
<PasswordField X="10" Y="2" Width="20" />                         <!-- shows * when unfocused -->
<CheckBox X="10" Y="3" Text="&amp;Subscribed" Checked="{Binding Subscribed}" />
<RadioGroup Text="Size" X="1" Y="5" Width="20" Selected="{Binding SizeId}" RequiresSelection="True">
  <RadioButton Text="&amp;Small" />                               <!-- ids 1..n; Selected=0 means none -->
  <RadioButton Text="&amp;Large" />
</RadioGroup>
<ComboBox X="30" Y="5" Width="14" MaxValuesHeight="6" LimitToListValue="True" Changed="OnDay">
  <x:String>Monday</x:String><x:String>Tuesday</x:String>
</ComboBox>
<Spinner X="45" Y="5" SpinUp="OnUp" SpinDown="OnDown" />
<ProgressBar X="1" Y="9" Width="30" Minimum="0" Maximum="100" Value="{Binding Percent}" />
<Button Text="&amp;Save" X="1" Y="11" Command="{Binding SaveCommand}" />   <!-- Width=label+3, Height=2 -->
```

| Control | Properties | Events |
|---|---|---|
| `Label` | `Text` (mnemonic; sets width), `ColorKey`, `MatchWindowBackground`, `Command`, `CommandParameter` | `Click` |
| `Button` | `Text`, `ShowShadow`, `Command`, `CommandParameter` | `Click` (Enter, Space, mouse, Alt+mnemonic) |
| `CheckBox` | `Text`, `Checked` (2W), `MatchWindowBackground`, `Command` | `Changed` |
| `RadioGroup` / `RadioButton` | `Text`, `Selected` (2W, id), `SelectedButton`, `RequiresSelection` / `Text`, `IsSelected`, `Id` | `SelectionChanged` |
| `Field`, `PasswordField` | `Text` (2W), `Fixed`, `InsertMode`, `BackgroundChar`, `ActiveColorKey`, `InactiveColorKey` | `Changed` (each edit), `Submitted` (Enter), `LostFocus` |
| `ComboBox` | `Items` (content) or `ItemsSource` + `DisplayMemberBinding`, `Text` (2W), `SelectedIndex` (2W), `SelectedItem` (2W), `LimitToListValue`, `MaxValuesHeight`, `ShowList()`/`HideList()` | `Changed` |
| `Spinner` | - | `SpinUp`, `SpinDown` |
| `ProgressBar` | `Minimum`, `Maximum`, `Value` (one-way), `LeftBorderChar`, `RightBorderChar`, `CompletedChar`, `RemainingChar` | - |
| `HScroller` / `VScroller` | `Minimum`, `Maximum`, `Value` (2W), `SmallChange`, `BigChange` | `ValueChanged` |

## Lists, trees, text

```xml
<List X="1" Y="1" Width="24" Height="6" ItemsSource="{Binding People}"
      DisplayMemberBinding="{Binding Name}" SelectedItem="{Binding Selected}" ItemActivated="OnOpen" />
<TreeView X="1" Y="8" Width="24" Height="8" ItemsSource="{Binding Roots}"
          DisplayMemberBinding="{Binding Title}" ChildrenBinding="{Binding Children}" />
<TreeView Width="20" Height="6">                                   <!-- static tree -->
  <TreeItem Text="Root" IsExpanded="True"><TreeItem Text="Child" /></TreeItem>
</TreeView>
<Text X="1" Y="1" Width="40" Height="10" Justification="Left" Content="Para 1.&#10;&#10;Para 2." />
<DirectoryList X="1" Y="1" Width="30" Height="10" Path="." Filter="\.cs$;\.md$" />
```

| Control | Properties | Events |
|---|---|---|
| `List` | `Items` (content, strings, `ReplaceAll`) or `ItemsSource` + `DisplayMemberBinding`, `SelectedIndex` (2W), `SelectedItem` (2W; source item), `SelectedText` | `SelectionChanged`, `ItemActivated` (Enter/double-click), `ItemClicked` |
| `TreeView` | `Root` (content `TreeItem`) or `ItemsSource` + `DisplayMemberBinding` + `ChildrenBinding`, `SelectedItem` (2W), `SelectedTreeItem`, `Select(item, center)` | `SelectionChanged`, `ItemActivated` |
| `TreeItem` (model, not a widget) | `Text`, `Items` (content), `IsExpanded`, `IsExpandable`, `IsSelectable`, `Tag`; override `OnExpand` for lazy loading | - |
| `Text` | `Content` (paragraphs separated by a blank line), `Justification` (None/Left/Center/Right/Full), `LineSpacing`, `ColorKey`, `AddLine(...)` | - |
| `DirectoryList` | `Path`, `Filter` (regexes separated by `;`), `Files`, `SelectedFile`, `Refresh()` | - |
| `DirectoryTreeItem` | `Path`; `DirectoryTreeItem.OpenPath(treeView, path)` | - |

`List`, `TreeView` and `Text` draw their scroll bars inside their own `Width`/`Height` (right column and bottom
row), so make them one column wider and one row taller than the content you want visible.

## Containers and layout

```xml
<SplitPane Vertical="True">                           <!-- only child of a window: fills it -->
  <List>...</List>
  <SplitPane Vertical="False"><Text Content="Top" /><Text Content="Bottom" /></SplitPane>
</SplitPane>
<Panel Title="Details" TitlePosition="Top" BorderStyle="Single" X="1" Y="1" Width="30" Height="8">
  <Label Text="Inside the panel" />                   <!-- bordered panel offsets children by 1 -->
</Panel>
```

- `Panel`: `Title`, `TitlePosition` (TopLeft/Top/TopRight/BottomLeft/Bottom/BottomRight), `BorderStyle`
  (None default, Single, Double, Round, SingleVDoubleH, SingleHDoubleV).
- `SplitPane`: `Vertical` (default true = left/right), `Split` (2W), `Left`/`Right` (= `Top`/`Bottom`),
  `FocusFollowsMouse`; up to two content panes; divider is mouse-draggable.
- Layout managers (`Widget.Layout`): `AnchorLayout` with attached `AnchorLayout.Anchor` = flags `Left, Top, Right,
  Bottom` or `All` (keeps distances to anchored edges on resize); `BoxLayout Orientation="Horizontal|Vertical"`;
  `StretchLayout` (scales children proportionally).

## Menus and status bars

```xml
<Application.Menus>                                   <!-- on an x:Class Application root -->
  <Menu Title="&amp;File">
    <MenuItem Text="&amp;Open..." Shortcut="Ctrl+O" Command="Open" />
    <MenuItem Text="&amp;Wrap" Checkable="True" Checked="{Binding Wrap}" />
    <SubMenu Text="&amp;Recent"><MenuItem Text="a.txt" Click="OnRecent" /></SubMenu>
    <MenuSeparator />
    <MenuItem Text="E&amp;xit" Shortcut="Alt+X" Command="Exit" />
  </Menu>
</Application.Menus>

<Window.StatusBar>                                    <!-- per window; Desktop.StatusBar when no windows -->
  <StatusBar Text="Ready">
    <StatusKey Key="F1" Text="Help" Command="Help" />
    <StatusKey Key="F2" Text="Save" Click="OnSave" />
  </StatusBar>
</Window.StatusBar>
```

- `MenuItem`: `Text`, `Shortcut` (KeyGesture accelerator; works when no menu is open and no modal window),
  `Checkable`, `Checked`, `Icon`, `Command`, `CommandParameter`, `Enabled`, `Click`.
- Menus can also be built in code: `app.Menus.Add(new Menu { Title = "&File" })`, `menu.Children.Add(new MenuItem
  {...})`. Context menus: `app.OpenContextMenu(menu, x, y)`.
- F10 opens the first menu; Alt+mnemonic opens a specific menu.

## Dialogs (all async, modal, centred)

```csharp
MessageBoxResult r = await MessageBox.ShowAsync(app, "Delete", "Delete file?\nThis cannot be undone.",
    MessageBoxButtons.YesNo);                                   // Ok | OkCancel | YesNo | YesNoCancel
string? name = await InputBox.ShowAsync(app, "Rename", "New name:", "old.txt", MessageBoxButtons.OkCancel);
string? file = await FileOpenBox.ShowAsync(app, Directory.GetCurrentDirectory(), FileOpenBoxType.Open, @"\.txt$");

// A custom dialog: a Window subclass with Modal="True"
var dlg = new EditPersonDialog(person);
app.Windows.Add(dlg);
Person? edited = await dlg.ShowModalAsync<Person>();            // dialog calls Close(result) or Close()
```

Use `async void` only for event handlers (`private async void OnDelete(object? s, EventArgs e)`), otherwise
`_ = DoAsync();`. Dialog windows that size to the screen should build content in `OnAddedToApplication()`.

## Themes and colours

- `ColorTheme` keys follow Jexer (`twindow.border`, `twindow.background`, `tlabel`, `tbutton.active`,
  `tfield.inactive`, `tlist.selected`, `tstatusbar.text`, ...) plus Nexer's `*.error` keys for validation.
- Per window: `<Window.OwnTheme><ColorTheme><ThemeColor Key="tlabel" Style="bold yellow on blue" /></ColorTheme></Window.OwnTheme>`.
- App-wide in code: `app.Theme.SetColor("tlabel", "bold white on blue")`; `ColorTheme.Load(TextReader)`.
- Style syntax: `[bold] [italic] [underline] [blink] [reverse] <fg> on <bg>`, colours = names (`black`, `red`,
  `green`, `yellow`, `blue`, `magenta`, `cyan`, `white`), `#RRGGBB` or `default`.

## Keys and gestures

`KeyGesture` strings: `Ctrl+Q`, `Alt+F`, `F10`, `Shift+Tab`, `Ctrl+Plus`, `Alt+Shift+F10`. Default navigation: Tab /
Shift+Tab (and arrows) move focus, F6/Shift+F6 switch windows, Ctrl+W closes, F5 zooms, Ctrl+F5 moves/sizes,
Alt+X exit (if bound to `Command.Exit`).
