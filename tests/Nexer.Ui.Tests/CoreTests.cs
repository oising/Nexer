using System.Text;
using Nexer.Core;
using Nexer.Input;
using Nexer.Ui.Layout;
using Nexer.Ui.Testing;

namespace Nexer.Ui.Tests;

public class CoreTests
{
    [Fact]
    public void RectAndThicknessParse()
    {
        Assert.Equal(new Rect(1, 2, 30, 4), Rect.Parse("1,2,30,4"));
        Assert.Equal(new Rect(1, 2, 30, 4), Rect.Parse(" 1, 2 ,30,4 "));
        Assert.Equal(new Thickness(1, 1, 1, 1), Thickness.Parse("1"));
        Assert.Equal(new Thickness(1, 2, 1, 2), Thickness.Parse("1,2"));
        Assert.Equal(new Thickness(1, 2, 3, 4), Thickness.Parse("1,2,3,4"));
        Assert.Throws<FormatException>(() => Rect.Parse("1,2,3"));
    }

    [Theory]
    [InlineData("Ctrl+Q", Key.Character, 'q', KeyModifiers.Ctrl)]
    [InlineData("Alt+F", Key.Character, 'f', KeyModifiers.Alt)]
    [InlineData("F10", Key.F10, '\0', KeyModifiers.None)]
    [InlineData("Shift+Tab", Key.BackTab, '\0', KeyModifiers.None)]
    [InlineData("Ctrl+Plus", Key.Character, '+', KeyModifiers.Ctrl)]
    [InlineData("Ctrl+Shift+F5", Key.F5, '\0', KeyModifiers.Ctrl | KeyModifiers.Shift)]
    public void KeyGestureParse(string text, Key key, char ch, KeyModifiers modifiers)
    {
        var g = KeyGesture.Parse(text);
        Assert.Equal(key, g.Key);
        Assert.Equal(modifiers, g.Modifiers);
        if (key == Key.Character) Assert.Equal(new Rune(ch), g.Char);
    }

    [Fact]
    public void KeyGestureMatchesCaseInsensitively()
    {
        var g = KeyGesture.Parse("Ctrl+Q");
        Assert.True(g.Matches(new KeyEvent(Key.Character, new Rune('q'), KeyModifiers.Ctrl)));
        Assert.True(g.Matches(new KeyEvent(Key.Character, new Rune('Q'), KeyModifiers.Ctrl | KeyModifiers.Shift)));
        Assert.False(g.Matches(new KeyEvent(Key.Character, new Rune('q'))));
        Assert.True(KeyGesture.Parse("Shift+Tab").Matches(new KeyEvent(Key.BackTab)));
        Assert.Equal("Ctrl+Q", g.ToString());
        Assert.Throws<FormatException>(() => KeyGesture.Parse("Hyper+Q"));
    }

    [Fact]
    public void CommandParseReturnsWellKnownInstances()
    {
        Assert.Same(Command.Exit, Command.Parse("exit"));
        Assert.Equal("Save", Command.Parse("Save").Name);
    }

    [Fact]
    public void MnemonicStringParsesShortcutAndEscapedAmpersand()
    {
        var m = new MnemonicString("E&xit");
        Assert.Equal("Exit", m.RawLabel);
        Assert.Equal(new Rune('x'), m.Shortcut);
        Assert.Equal(1, m.ShortcutIndex);
        Assert.True(m.Matches(new Rune('X')));

        var literal = new MnemonicString("Save && &Quit");
        Assert.Equal("Save & Quit", literal.RawLabel);
        Assert.Equal(new Rune('Q'), literal.Shortcut);
        Assert.Equal(7, literal.ScreenShortcutIndex);

        Assert.False(new MnemonicString("Plain").HasShortcut);
    }

    [Fact]
    public void ThemeHasJexerDefaults()
    {
        var theme = new ColorTheme();
        Assert.Equal(StyleSyntax.Parse("bold white on blue"), theme.GetColor("twindow.border"));
        Assert.Equal(StyleSyntax.Parse("bold yellow on blue"), theme.GetColor("twindow.background"));
        Assert.Contains("tmenu.highlighted", theme.Keys);
        Assert.Contains("tdesktop.background", theme.Keys);
        theme.SetColor("twindow.border", "red on black");
        Assert.Equal(StyleSyntax.Parse("red on black"), theme["twindow.border"]);
        Assert.Equal(StyleSyntax.Parse("bold white on blue"), ColorTheme.Default["twindow.border"]);
    }

    [Fact]
    public void DrawBoxUsesBorderGlyphs()
    {
        var surface = new CellSurface(6, 4);
        var ctx = new DrawContext(surface);
        ctx.DrawBox(0, 0, 5, 3, default, default, BorderStyle.Single, shadow: false);
        Assert.Equal("┌───┐\n│   │\n└───┘\n", ScreenDump.Text(surface, 0, 0, 5, 4));

        ctx.DrawBox(0, 0, 4, 3, default, default, BorderStyle.Double, shadow: false);
        Assert.Equal("╔══╗\n║  ║\n╚══╝", ScreenDump.Text(surface, 0, 0, 4, 3));
    }

    [Fact]
    public void ChildContextClipsAndOffsets()
    {
        var surface = new CellSurface(10, 3);
        var ctx = new DrawContext(surface).CreateChild(2, 1, 4, 1);
        int written = ctx.PutString(0, 0, "abcdefgh", default);
        Assert.Equal("", ScreenDump.Text(surface, 0, 0, 10, 1));
        Assert.Equal("  abcd", ScreenDump.Text(surface, 0, 1, 10, 1));
        Assert.True(written >= 4);
        ctx.PutChar(-1, 0, 'X', default);
        Assert.Equal(" ", surface.GetText(1, 1));
    }

    [Fact]
    public void WideCharactersOccupyTwoCellsAndAreNotSplitAtClip()
    {
        var surface = new CellSurface(6, 1);
        var ctx = new DrawContext(surface).CreateChild(0, 0, 3, 1);
        ctx.PutString(0, 0, "中文", default);
        Assert.Equal("中", surface.GetText(0, 0));
        Assert.Equal(CellRole.WideContinuation, surface.GetRow(0)[1].Role);
        Assert.NotEqual("文", surface.GetText(2, 0));
    }

    [Fact]
    public void ShadowRestylesButKeepsGlyphs()
    {
        var surface = new CellSurface(8, 4);
        var ctx = new DrawContext(surface);
        ctx.Fill(0, 0, 8, 4, new Rune('x'), StyleSyntax.Parse("white on blue"));
        ctx.DrawBoxShadow(0, 0, 4, 2);
        Assert.Equal("x", surface.GetText(4, 1));
        Assert.Equal(DrawContext.ShadowStyle, surface.GetRow(1)[4].Style);
        Assert.Equal(DrawContext.ShadowStyle, surface.GetRow(2)[2].Style);
        Assert.NotEqual(DrawContext.ShadowStyle, surface.GetRow(0)[4].Style);
        Assert.NotEqual(DrawContext.ShadowStyle, surface.GetRow(2)[1].Style);
    }

    [Fact]
    public void ChildrenGetTabOrderAndActivation()
    {
        var window = new Window();
        var a = new TestWidget { Name = "a" };
        var b = new TestWidget { Name = "b" };
        var c = new TestWidget { Name = "c", TabStop = false };
        window.Children.Add(a);
        window.Children.Add(b);
        window.Children.Add(c);
        Assert.Same(a, window.ActiveChild);
        Assert.Equal([0, 1, 2], window.Children.Select(w => w.TabOrder));
        Assert.Same(window, a.Parent);
        Assert.Same(window, a.Window);
        window.SwitchWidget(true);
        Assert.Same(b, window.ActiveChild);
        window.SwitchWidget(true);
        Assert.Same(a, window.ActiveChild);
        b.Enabled = false;
        window.SwitchWidget(true);
        Assert.Same(a, window.ActiveChild);
        Assert.Same(c, window.FindByName("c"));
        window.Children.Remove(a);
        Assert.Null(a.Parent);
        Assert.Throws<InvalidOperationException>(() => a.Children.Add(new Window()));
    }

    [Fact]
    public void BeginInitDefersLayoutUntilEndInit()
    {
        var window = new Window();
        window.BeginInit();
        window.Width = 22;
        window.Height = 12;
        window.Layout = new BoxLayout { Orientation = Orientation.Horizontal };
        var left = new TestWidget();
        var right = new TestWidget();
        window.Children.Add(left);
        window.Children.Add(right);
        window.EndInit();
        Assert.Equal(new Rect(0, 0, 10, 10), left.Bounds);
        Assert.Equal(new Rect(10, 0, 10, 10), right.Bounds);
        window.Width = 42;
        Assert.Equal(new Rect(10 + 10, 0, 20, 10), right.Bounds);
    }

    [Fact]
    public void AnchorLayoutKeepsMargins()
    {
        var window = new Window { Width = 30, Height = 10, Layout = new AnchorLayout() };
        var stretch = new TestWidget { X = 2, Y = 1, Width = 10, Height = 3 };
        AnchorLayout.SetAnchor(stretch, AnchorEdges.Left | AnchorEdges.Right | AnchorEdges.Top);
        var corner = new TestWidget { X = 20, Y = 5, Width = 4, Height = 1 };
        AnchorLayout.SetAnchor(corner, AnchorEdges.Right | AnchorEdges.Bottom);
        window.Children.Add(stretch);
        window.Children.Add(corner);
        window.Width = 40;
        window.Height = 14;
        Assert.Equal(new Rect(2, 1, 20, 3), stretch.Bounds);
        Assert.Equal(new Rect(30, 9, 4, 1), corner.Bounds);
        Assert.Equal(AnchorEdges.Right | AnchorEdges.Bottom, AnchorLayout.GetAnchor(corner));
    }

    [Fact]
    public void ResourcesResolveThroughAncestors()
    {
        var app = new Application();
        app.Resources["accent"] = "red";
        var window = new Window();
        var child = new TestWidget();
        window.Children.Add(child);
        window.Resources["local"] = 42;
        app.Windows.Add(window);
        Assert.True(child.TryGetResource("local", out var local));
        Assert.Equal(42, local);
        Assert.True(child.TryGetResource("accent", out var accent));
        Assert.Equal("red", accent);
        Assert.False(child.TryGetResource("missing", out _));
    }
}

/// <summary>A focusable widget recording what it receives.</summary>
internal sealed class TestWidget : Widget
{
    public List<KeyEvent> Keys { get; } = [];

    public int Clicks { get; private set; }

    public string? Pasted { get; private set; }

    public TestWidget()
    {
        Width = 4;
        Height = 1;
    }

    protected override void OnKeypress(KeyEvent key)
    {
        if (key.Key is Key.Tab or Key.BackTab)
        {
            base.OnKeypress(key);
            return;
        }

        Keys.Add(key);
    }

    protected override void OnMouseUp(UiMouseEvent mouse) => Clicks++;

    protected override void OnPaste(string text) => Pasted = text;

    protected override void Draw(DrawContext context) =>
        context.PutString(0, 0, Name ?? "", IsAbsoluteActive ? GetColor("tbutton.active") : GetColor("tbutton.inactive"));
}
