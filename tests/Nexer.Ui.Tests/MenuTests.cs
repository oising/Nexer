using Nexer.Input;
using Nexer.Ui.Menus;
using Nexer.Ui.Testing;

namespace Nexer.Ui.Tests;

public class MenuTests
{
    private sealed class Fixture
    {
        public Fixture()
        {
            App = new Application { ConfirmExit = false };
            File = new Menu { Title = "&File" };
            Open = new MenuItem { Text = "&Open", Shortcut = KeyGesture.Parse("Ctrl+O") };
            Open.Click += (_, _) => Opened++;
            File.Children.Add(Open);
            File.Children.Add(new MenuSeparator());
            Wrap = new MenuItem { Text = "&Wrap", Checkable = true };
            File.Children.Add(Wrap);
            Recent = new SubMenu { Text = "&Recent" };
            Recent.Children.Add(new MenuItem { Text = "&One" });
            Recent.Children.Add(new MenuItem { Text = "&Two" });
            File.Children.Add(Recent);
            File.Children.Add(new MenuItem { Text = "E&xit", Shortcut = KeyGesture.Parse("Alt+X"), Command = Command.Exit });
            Window = new Menu { Title = "&Window" };
            Window.Children.Add(new MenuItem { Text = "&Tile", Command = Command.Tile });
            App.Menus.Add(File);
            App.Menus.Add(Window);
            Driver = new UiDriver(App, 40, 14);
        }

        public Application App { get; }

        public Menu File { get; }

        public Menu Window { get; }

        public MenuItem Open { get; }

        public MenuItem Wrap { get; }

        public SubMenu Recent { get; }

        public int Opened { get; set; }

        public UiDriver Driver { get; }
    }

    [Fact]
    public void MenuBarShowsTitlesWithMnemonics()
    {
        var f = new Fixture();
        var d = f.Driver;
        Assert.Equal("  File  Window", d.Row(0).TrimEnd());
        Assert.Equal(d.Theme("tmenu"), d.Style(3, 0));
        Assert.Equal(d.Theme("tmenu.mnemonic"), d.Style(2, 0));
    }

    [Fact]
    public void AltMnemonicOpensTheMenu()
    {
        var f = new Fixture();
        var d = f.Driver;
        d.Press("Alt+F");
        Assert.Same(f.File, f.App.ActiveMenu);
        Assert.Equal(
            """
             ┌────────────────┐
             │   Open  Ctrl+O │
             ├────────────────┤
             │   Wrap         │
             │   Recent      ▶│
             │   Exit   Alt+X │
             └────────────────┘
            """.ReplaceLineEndings("\n"),
            d.Text(0, 1, 19, 7));
        Assert.Equal(d.Theme("tmenu.highlighted"), d.Style(6, 2));
        Assert.Equal(d.Theme("tmenu.mnemonic.highlighted"), d.Style(5, 2));
        Assert.Equal(d.Theme("tmenu"), d.Style(6, 4));
        d.Press("Esc");
        Assert.Null(f.App.ActiveMenu);
    }

    [Fact]
    public void F10OpensTheFirstMenuAndArrowsNavigate()
    {
        var f = new Fixture();
        var d = f.Driver;
        d.Press("F10");
        Assert.Same(f.File, f.App.ActiveMenu);
        d.Press("Right");
        Assert.Same(f.Window, f.App.ActiveMenu);
        d.Press("Right");
        Assert.Same(f.File, f.App.ActiveMenu);
        d.Press("Left");
        Assert.Same(f.Window, f.App.ActiveMenu);
    }

    [Fact]
    public void EnterChoosesTheItemAndClosesTheMenu()
    {
        var f = new Fixture();
        var d = f.Driver;
        d.Press("Alt+F");
        d.Press("Enter");
        Assert.Null(f.App.ActiveMenu);
        Assert.Equal(1, f.Opened);
    }

    [Fact]
    public void ItemMnemonicChoosesTheItem()
    {
        var f = new Fixture();
        var d = f.Driver;
        d.Press("Alt+F");
        d.Type("w");
        Assert.True(f.Wrap.Checked);
        d.Press("Alt+F");
        Assert.Equal("√", d.Cell(2, 4));
        d.Type("w");
        Assert.False(f.Wrap.Checked);
    }

    [Fact]
    public void AcceleratorFiresWithoutOpeningTheMenu()
    {
        var f = new Fixture();
        f.Driver.Press("Ctrl+O");
        Assert.Equal(1, f.Opened);
        Assert.Null(f.App.ActiveMenu);
    }

    [Fact]
    public void MenuCommandIsExecuted()
    {
        var f = new Fixture();
        f.Driver.Press("Alt+X");
        Assert.False(f.App.IsRunning);
    }

    [Fact]
    public void ClickingTitleOpensMenuAndClickingItemChoosesIt()
    {
        var f = new Fixture();
        var d = f.Driver;
        d.Click(3, 0);
        Assert.Same(f.File, f.App.ActiveMenu);
        d.Click(5, 2);
        Assert.Null(f.App.ActiveMenu);
        Assert.Equal(1, f.Opened);
    }

    [Fact]
    public void ClickingOutsideClosesTheMenu()
    {
        var f = new Fixture();
        var d = f.Driver;
        d.Click(3, 0);
        d.Click(35, 10);
        Assert.Null(f.App.ActiveMenu);
        Assert.Equal(0, f.Opened);
    }

    [Fact]
    public void SubMenuOpensToTheRight()
    {
        var f = new Fixture();
        var d = f.Driver;
        d.Press("Alt+F");
        d.Type("r");
        Assert.True(f.Recent.InnerMenu.IsOpen);
        Assert.Equal(f.File.X + f.File.Width - 2, f.Recent.InnerMenu.X);
        Assert.Contains("One", d.Row(f.Recent.InnerMenu.Y + 1));
        d.Press("Left");
        Assert.False(f.Recent.InnerMenu.IsOpen);
        Assert.Same(f.File, f.App.ActiveMenu);
    }

    [Fact]
    public void ContextMenuOpensAtPositionAndIsClamped()
    {
        var f = new Fixture();
        var d = f.Driver;
        var context = new Menu();
        context.Children.Add(new MenuItem { Text = "&Copy" });
        f.App.OpenContextMenu(context, 38, 12);
        d.Pump();
        Assert.Same(context, f.App.ActiveMenu);
        Assert.True(context.X + context.Width <= 40);
        Assert.True(context.Y + context.Height <= f.App.DesktopBottom);
        Assert.Contains("Copy", d.Row(context.Y + 1) + "|" + context + "|" + d.Text());
    }

    [Fact]
    public void MenusIgnoreShortcutsWhileAModalWindowIsOpen()
    {
        var f = new Fixture();
        var dialog = new Window { Title = "Modal", Width = 20, Height = 5, Modal = true };
        f.App.Windows.Add(dialog);
        f.Driver.Pump();
        f.Driver.Press("Ctrl+O");
        f.Driver.Press("Alt+F");
        Assert.Equal(0, f.Opened);
        Assert.Null(f.App.ActiveMenu);
    }

    [Fact]
    public void HideMenuBarMovesTheDesktopUp()
    {
        var f = new Fixture();
        Assert.Equal(1, f.App.DesktopTop);
        f.App.HideMenuBar = true;
        f.Driver.Pump();
        Assert.Equal(0, f.App.DesktopTop);
        Assert.Equal("░", f.Driver.Cell(0, 0));
    }
}
