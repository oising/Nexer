using Nexer.Core;
using Nexer.Input;
using Nexer.Ui.Controls;
using Nexer.Ui.Layout;
using Nexer.Ui.Markup;
using Nexer.Ui.Menus;
using Nexer.Ui.Testing;
using Nexer.Ui.Xaml.Tests.Views;

namespace Nexer.Ui.Xaml.Tests;

/// <summary>Build-time (Cecil) XAML compiled into this assembly against the real Nexer.Ui controls.</summary>
public class CompiledRealControlsTests
{
    private static (CompiledWindow Window, UiDriver Driver) Show(Application? app = null)
    {
        app ??= new Application { ConfirmExit = false };
        var window = new CompiledWindow();
        app.Windows.Add(window);
        return (window, new UiDriver(app, 80, 25));
    }

    [Fact]
    public void DocumentsAreRegistered()
    {
        Assert.True(XamlComponent.IsRegistered(typeof(CompiledWindow)));
        Assert.True(XamlComponent.IsRegistered(typeof(CompiledApplication)));
    }

    [Fact]
    public void GeneratedFieldsAreTyped()
    {
        var window = new CompiledWindow();

        Assert.Equal("Compiled Window", window.Title);
        Assert.Equal(new Rect(2, 1, 74, 21), window.Bounds);
        Assert.IsType<PasswordField>(window.Secret);
        Assert.Equal("OK", window.OkButton.Text);
        Assert.Equal('O', window.OkButton.Mnemonic.Shortcut.Value);
        Assert.Equal(["one", "two"], window.Combo.Items);
        Assert.Equal(["Apple", "Banana"], window.Fruit.Items);
        Assert.Equal(25, window.Progress.Value);
        Assert.Equal("Root", window.Tree.Root!.Text);
        Assert.Equal(2, window.Tree.Root.Items.Count);
        Assert.Equal("Compiled text body.", window.Body.Content);
        Assert.Equal(2, window.Radios.Buttons.Count());
        Assert.Same(window.OkButton, NameScope.FindName(window, "OkButton"));
        Assert.IsType<SplitPane>(Assert.Single(window.Box.Children));
    }

    [Fact]
    public void LayoutThemeAndStatusBarAreApplied()
    {
        var window = new CompiledWindow();

        Assert.IsType<AnchorLayout>(window.Layout);
        Assert.Equal(AnchorEdges.Right | AnchorEdges.Bottom, AnchorLayout.GetAnchor(window.CancelButton));
        Assert.Equal(new CellStyle(TerminalColor.Indexed(3), TerminalColor.Indexed(4), CellStyleFlags.Bold), window.OwnTheme!.GetColor("tlabel"));
        Assert.Equal("Compiled status", window.StatusBar!.Text);
        Assert.Equal(KeyGesture.Parse("F2"), Assert.Single(window.StatusBar.Keys).Key);

        int x = window.CancelButton.X, y = window.CancelButton.Y;
        window.Width += 4;
        window.Height += 2;
        Assert.Equal((x + 4, y + 2), (window.CancelButton.X, window.CancelButton.Y));
    }

    [Fact]
    public void RendersThroughHeadlessHost()
    {
        var (window, d) = Show();

        var text = d.Text();
        Assert.Contains("Compiled Window", d.Row(window.Y));
        Assert.Contains("Name:", text);
        Assert.Contains(" OK ", text);
        Assert.Contains("Cancel", text);
        Assert.Contains("Check me", text);
        Assert.Contains("Beta", text);
        Assert.Contains("Banana", text);
        Assert.Contains("Leaf 2", text);
        Assert.Contains("Compiled text body.", text);
        Assert.Contains("Bottom", text);
        Assert.Contains("F2 Save", d.Row(24));
        Assert.Contains("Compiled status", d.Row(24));
        int labelX = d.Row(window.Y + 1).IndexOf("ame:", StringComparison.Ordinal);
        Assert.Equal(TerminalColor.Indexed(3), d.Style(labelX, window.Y + 1).Foreground);
    }

    [Fact]
    public void PrivateHandlersRunFromInput()
    {
        var (window, d) = Show();

        d.Press("Alt+O");
        d.Press("Alt+C");
        d.Press("Alt+H");
        d.Press("Alt+B");
        Assert.Equal(["ok", "cancel", "check:True", "radio:2"], window.Log);

        window.Log.Clear();
        window.Activate(window.NameField);
        d.Key(Key.End);
        d.Type("!");
        Assert.Equal(["name:Nexer!"], window.Log);

        window.Log.Clear();
        window.Activate(window.Fruit);
        d.Key(Key.Down);
        d.Key(Key.Enter);
        Assert.Equal(["fruit:Apple"], window.Log);
    }

    [Fact]
    public void MouseClickRunsPrivateHandler()
    {
        var (window, d) = Show();
        int x = window.OkButton.AbsoluteX + 1, y = window.OkButton.AbsoluteY;

        d.Click(x, y);

        Assert.Equal(["ok"], window.Log);
    }

    [Fact]
    public void ApplicationMenusAreCompiled()
    {
        var app = new CompiledApplication();
        Assert.False(app.ConfirmExit);
        Assert.Equal(["File", "Window"], app.Menus.Select(m => m.Title));
        Assert.Equal("From application", app.Resources["appTitle"]);
        var open = Assert.IsType<MenuItem>(NameScope.FindName(app, "OpenItem"));
        Assert.Equal(KeyGesture.Parse("Ctrl+O"), open.Shortcut);
        var wrap = Assert.IsType<MenuItem>(app.Menus[1].Children[1]);
        Assert.True(wrap.Checkable && wrap.Checked);
        // Command="Tile" on an ICommand property compiles to Command.Parse (the well-known instance).
        Assert.Same(Command.Tile, Assert.IsType<MenuItem>(app.Menus[1].Children[0]).Command);

        var d = new UiDriver(app, 60, 20);
        Assert.Equal("  File  Window", d.Row(0).TrimEnd());

        d.Press("Ctrl+O");
        Assert.Equal(["open"], app.Log);

        d.Press("Alt+F");
        Assert.Contains("Ctrl+O", d.Text());
        d.Press("R");
        d.Press("O");
        Assert.Equal(["open", "recent"], app.Log);

        d.Press("F10");
        d.Key(Key.Right);
        Assert.Contains("Tile", d.Text());
        d.Key(Key.Escape);
    }

    [Fact]
    public void CompiledWindowInCompiledApplication()
    {
        var app = new CompiledApplication();
        var (window, d) = Show(app);

        Assert.Contains("File", d.Row(0));
        Assert.Contains("Compiled Window", d.Row(window.Y));
        d.Press("Alt+X");
        Assert.False(app.IsRunning);
    }
}
