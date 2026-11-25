using Nexer.Core;
using Nexer.Ui.Controls;
using Nexer.Ui.Layout;
using Nexer.Ui.Markup;
using Nexer.Ui.Menus;
using Nexer.Ui.Testing;
using Nexer.Xaml;

namespace Nexer.Ui.Xaml.Tests;

/// <summary>Runtime (SRE) XAML loading against the real Nexer.Ui controls, rendered through <see cref="HeadlessHost"/>.</summary>
public class LoaderRealControlsTests
{
    private const string Ns = "xmlns='https://nexer.dev/ui' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'";

    private const string KitchenSink = $$"""
        <Window {{Ns}} Title='Kitchen Sink' Bounds='0,0,70,20'>
          <Window.Resources>
            <x:String x:Key='okText'>&amp;OK</x:String>
          </Window.Resources>
          <Window.StatusBar>
            <StatusBar Text='Ready'>
              <StatusKey Key='F1' Text='Help' Command='Help' />
              <StatusKey Key='Alt+X' Text='Exit' Command='Exit' />
            </StatusBar>
          </Window.StatusBar>
          <Label x:Name='Caption' Text='&amp;Name:' X='1' Y='0' />
          <Field x:Name='NameField' Text='Nexer' X='8' Y='0' Width='15' />
          <PasswordField x:Name='Secret' Text='hunter2' X='8' Y='1' Width='15' />
          <Button x:Name='Ok' Text='{StaticResource okText}' X='1' Y='3' />
          <CheckBox x:Name='Check' Text='&amp;Check me' Checked='True' X='1' Y='6' />
          <RadioGroup x:Name='Radios' Text='Choose' X='30' Y='0' Width='20' RequiresSelection='True'>
            <RadioButton Text='&amp;Alpha' />
            <RadioButton Text='&amp;Beta' IsSelected='True' />
          </RadioGroup>
          <ComboBox x:Name='Combo' X='1' Y='8' Width='15' Text='two'>
            <x:String>one</x:String>
            <x:String>two</x:String>
          </ComboBox>
          <Spinner x:Name='Spin' X='17' Y='8' />
          <ProgressBar x:Name='Progress' X='1' Y='10' Width='20' Value='40' />
          <List x:Name='Items' X='30' Y='6' Width='15' Height='4'>
            <x:String>Apple</x:String>
            <x:String>Banana</x:String>
          </List>
          <TreeView x:Name='Tree' X='46' Y='6' Width='20' Height='5'>
            <TreeItem Text='Root' IsExpanded='True'>
              <TreeItem Text='Leaf 1' />
              <TreeItem Text='Leaf 2' />
            </TreeItem>
          </TreeView>
          <Text x:Name='Body' X='1' Y='12' Width='40' Height='3' Content='Hello from XAML text.' />
        </Window>
        """;

    [Fact]
    public void LoadsKitchenSinkWithRealControls()
    {
        var window = XamlLoader.Load<Window>(KitchenSink);

        Assert.Equal("Kitchen Sink", window.Title);
        Assert.Equal(new Rect(0, 0, 70, 20), window.Bounds);
        Assert.Equal("OK", NameScope.FindName<Button>(window, "Ok")!.Text);
        Assert.Equal("Nexer", NameScope.FindName<Field>(window, "NameField")!.Text);
        Assert.IsType<PasswordField>(NameScope.FindName(window, "Secret"));
        Assert.True(NameScope.FindName<CheckBox>(window, "Check")!.Checked);
        var radios = NameScope.FindName<RadioGroup>(window, "Radios")!;
        Assert.Equal(2, radios.Selected);
        Assert.Equal("Beta", radios.SelectedButton!.Text);
        var combo = NameScope.FindName<ComboBox>(window, "Combo")!;
        Assert.Equal(["one", "two"], combo.Items);
        Assert.Equal("two", combo.Text);
        Assert.Equal(40, NameScope.FindName<ProgressBar>(window, "Progress")!.Value);
        Assert.Equal(["Apple", "Banana"], NameScope.FindName<List>(window, "Items")!.Items);
        var tree = NameScope.FindName<TreeView>(window, "Tree")!;
        Assert.Equal("Root", tree.Root!.Text);
        Assert.Equal(["Root", "Leaf 1", "Leaf 2"], tree.VisibleItems.Select(i => i.Text));
        Assert.Equal("Hello from XAML text.", NameScope.FindName<Text>(window, "Body")!.Content);
        Assert.Equal(["Help", "Exit"], window.StatusBar!.Keys.Select(k => k.Text));
        Assert.Equal(KeyGesture.Parse("Alt+X"), window.StatusBar.Keys[1].Key);
        Assert.Same(Command.Exit, window.StatusBar.Keys[1].Command);
        Assert.All(window.Children, c => Assert.Same(window, c.Parent));
    }

    [Fact]
    public void RendersKitchenSinkHeadlessly()
    {
        var app = new Application { ConfirmExit = false };
        var window = XamlLoader.Load<Window>(KitchenSink);
        app.Windows.Add(window);
        var driver = new UiDriver(app, 80, 25);

        var text = driver.Text();
        Assert.Contains("Kitchen Sink", driver.Row(1));
        Assert.Contains("Name:", text);
        Assert.Contains("OK", text);
        Assert.Contains("Check me", text);
        Assert.Contains("Alpha", text);
        Assert.Contains("Banana", text);
        Assert.Contains("Leaf 2", text);
        Assert.Contains("Hello from XAML text.", text);
        Assert.Contains("F1 Help", driver.Row(24));
        Assert.Contains("Ready", driver.Row(24));
    }

    [Fact]
    public void LoadsMenusIntoApplication()
    {
        var app = XamlLoader.Load<Application>($"""
            <Application {Ns} ConfirmExit='False'>
              <Application.Menus>
                <Menu Title='&amp;File'>
                  <MenuItem Text='&amp;Open' Shortcut='Ctrl+O' />
                  <MenuSeparator />
                  <SubMenu Text='&amp;Recent'>
                    <MenuItem Text='&amp;One' />
                  </SubMenu>
                  <MenuItem Text='E&amp;xit' Shortcut='Alt+X' Command='Exit' />
                </Menu>
                <Menu Title='&amp;Window'>
                  <MenuItem Text='&amp;Tile' Command='Tile' />
                </Menu>
              </Application.Menus>
            </Application>
            """);

        Assert.Equal(["File", "Window"], app.Menus.Select(m => m.Title));
        var open = Assert.IsType<MenuItem>(app.Menus[0].Children[0]);
        Assert.Equal(KeyGesture.Parse("Ctrl+O"), open.Shortcut);
        Assert.Equal('O', open.Mnemonic.Shortcut.Value);
        Assert.IsType<SubMenu>(app.Menus[0].Children[2]);

        var driver = new UiDriver(app, 40, 12);
        Assert.Equal("  File  Window", driver.Row(0).TrimEnd());
        driver.Press("Alt+F");
        Assert.Contains("Open", driver.Text());
        Assert.Contains("Ctrl+O", driver.Text());
    }

    [Fact]
    public void LayoutAndAttachedAnchor()
    {
        var window = XamlLoader.Load<Window>($"""
            <Window {Ns} Title='Anchors' Bounds='0,0,30,10'>
              <Window.Layout>
                <AnchorLayout />
              </Window.Layout>
              <Button x:Name='Corner' Text='X' X='20' Y='5' AnchorLayout.Anchor='Right, Bottom' />
            </Window>
            """);
        var button = NameScope.FindName<Button>(window, "Corner")!;
        Assert.IsType<AnchorLayout>(window.Layout);
        Assert.Equal(AnchorEdges.Right | AnchorEdges.Bottom, AnchorLayout.GetAnchor(button));

        window.Width = 40;
        window.Height = 14;

        Assert.Equal((30, 9), (button.X, button.Y));
    }

    [Fact]
    public void PanelsSplitPanesAndScrollableWindows()
    {
        var window = XamlLoader.Load<ScrollableWindow>($"""
            <ScrollableWindow {Ns} Title='Split' Bounds='0,0,40,12' HasVerticalScroller='True'>
              <SplitPane x:Name='Split' Vertical='True'>
                <Panel Title='Left' BorderStyle='Single'>
                  <Label Text='Left side' />
                </Panel>
                <Panel Title='Right' BorderStyle='Single'>
                  <Label Text='Right side' />
                </Panel>
              </SplitPane>
            </ScrollableWindow>
            """);
        var split = NameScope.FindName<SplitPane>(window, "Split")!;
        Assert.NotNull(split.Left);
        Assert.NotNull(split.Right);
        Assert.NotNull(window.VerticalScroller);

        var app = new Application { ConfirmExit = false };
        app.Windows.Add(window);
        var driver = new UiDriver(app, 60, 20);
        var text = driver.Text();
        Assert.True(text.Contains("Left side", StringComparison.Ordinal), text);
        Assert.Contains("Right side", text);
    }

    [Fact]
    public void ConvertsCellStylesInThemes()
    {
        var window = XamlLoader.Load<Window>($"""
            <Window {Ns} Title='Themed' Bounds='0,0,30,8'>
              <Window.OwnTheme>
                <ColorTheme>
                  <ThemeColor Key='twindow.background' Style='bold yellow on red' />
                  <ThemeColor Key='tlabel' Style='#102030 on default' />
                </ColorTheme>
              </Window.OwnTheme>
              <Label Text='Styled' />
            </Window>
            """);

        var theme = window.OwnTheme!;
        Assert.Equal(new CellStyle(TerminalColor.Indexed(3), TerminalColor.Indexed(1), CellStyleFlags.Bold), theme.GetColor("twindow.background"));
        Assert.Equal(new CellStyle(TerminalColor.Rgb(0x10, 0x20, 0x30), TerminalColor.Default, CellStyleFlags.None), theme.GetColor("tlabel"));
        Assert.Equal(ColorTheme.Default.GetColor("tbutton.active"), theme.GetColor("tbutton.active"));

        var app = new Application { ConfirmExit = false };
        app.Windows.Add(window);
        var driver = new UiDriver(app, 40, 12);
        int x = driver.Row(window.Y + 1).IndexOf("Styled", StringComparison.Ordinal);
        Assert.Equal(theme.GetColor("tlabel").Foreground, driver.Style(x, window.Y + 1).Foreground);
    }
    [Fact]
    public void KeyboardDrivesLoadedWindow()
    {
        var window = XamlLoader.Load<Window>(KitchenSink);
        var app = new Application { ConfirmExit = false };
        app.Windows.Add(window);
        var driver = new UiDriver(app, 80, 25);
        int clicks = 0;
        NameScope.FindName<Button>(window, "Ok")!.Click += (_, _) => clicks++;

        driver.Press("Alt+O");

        Assert.Equal(1, clicks);
    }
}
