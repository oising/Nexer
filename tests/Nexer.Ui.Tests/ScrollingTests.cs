using Nexer.Ui.Controls;
using Nexer.Ui.Testing;

namespace Nexer.Ui.Tests;

public class ScrollingTests
{
    // A window at desktop (0,0) lands on screen row 1 (below the menu bar); its client origin is screen (1,2).
    private static UiDriver Start(Window window)
    {
        var app = new Application { ConfirmExit = false };
        app.Windows.Add(window);
        return new UiDriver(app, 40, 14);
    }

    private static string Column(UiDriver d, int x, int y, int height) =>
        string.Concat(Enumerable.Range(y, height).Select(row => d.Cell(x, row)));

    [Fact]
    public void ScrollableWindowDrawsScrollersOnItsBorder()
    {
        var window = new ScrollableWindow
        {
            Title = "S",
            X = 0,
            Y = 0,
            Width = 20,
            Height = 8,
            HasVerticalScroller = true,
            HasHorizontalScroller = true,
        };
        var d = Start(window);
        Assert.Equal("▲■▒▒▒▼", Column(d, 19, 2, 6));
        Assert.Equal("◀■▒▒▒▒▒▒▒▒▒▒▒▶", d.Row(8, 4, 14));
        Assert.False(window.VerticalScroller!.TabStop);

        d.Click(19, 7);
        Assert.Equal(1, window.VerticalValue);
        d.Click(17, 8);
        Assert.Equal(1, window.HorizontalValue);

        window.Height = 10;
        d.Pump();
        Assert.Equal("▲■▒▒▒▒▒▼", Column(d, 19, 2, 8));
    }

    [Fact]
    public void PanelFillsWindowAndDrawsBorderAndTitle()
    {
        var panel = new Panel { BorderStyle = BorderStyle.Single, Title = " Hi " };
        var window = new Window { Title = "P", X = 0, Y = 0, Width = 20, Height = 8 };
        window.Children.Add(panel);
        var d = Start(window);
        Assert.Equal(new Rect(0, 0, 18, 6), panel.Bounds);
        Assert.Equal("┌ Hi ─", d.Row(2, 1, 6));
        Assert.Equal("└" + new string('─', 16) + "┘", d.Row(7, 1, 18));
        Assert.Equal(d.Theme("tpanel.border"), d.Style(5, 4));

        panel.TitlePosition = PanelTitlePosition.BottomRight;
        d.Pump();
        Assert.Equal("─ Hi ┘", d.Row(7, 13, 6));
    }

    [Fact]
    public void SplitPaneCentresAndDragsTheDivider()
    {
        var left = new Panel();
        var right = new Panel();
        var split = new SplitPane();
        split.Children.Add(left);
        split.Children.Add(right);
        var window = new Window { Title = "S", X = 0, Y = 0, Width = 21, Height = 8 };
        window.Children.Add(split);
        var d = Start(window);

        Assert.Equal(9, split.Split);
        Assert.Equal(new Rect(0, 0, 9, 6), left.Bounds);
        Assert.Equal(new Rect(10, 0, 9, 6), right.Bounds);
        Assert.Equal("││││││", Column(d, 10, 2, 6));

        d.Drag(10, 4, 6, 4);
        Assert.Equal(5, split.Split);
        Assert.Equal(new Rect(0, 0, 5, 6), left.Bounds);
        Assert.Equal(new Rect(6, 0, 13, 6), right.Bounds);
        Assert.Equal("││↔│││", Column(d, 6, 2, 6));
        Assert.Equal(d.Theme("tsplitpane"), d.Style(6, 2));

        window.Width = 11;
        d.Pump();
        Assert.Equal(5, split.Split);
        window.Width = 7;
        d.Pump();
        Assert.Equal(2, split.Split);
    }

    [Fact]
    public void NestedSplitPanesDrawJunctionsAndKeepPaneOrder()
    {
        var inner = new SplitPane { Vertical = false };
        inner.Children.Add(new Panel());
        inner.Children.Add(new Panel());
        var outerRight = new Panel();
        var outer = new SplitPane { Right = outerRight, Left = inner };
        var window = new Window { Title = "S", X = 0, Y = 0, Width = 21, Height = 8 };
        window.Children.Add(outer);
        var d = Start(window);

        Assert.Same(inner, outer.Children[0]);
        Assert.Same(inner, outer.Top);
        Assert.Same(outerRight, outer.Bottom);
        Assert.Equal(new Rect(0, 0, 9, 6), inner.Bounds);
        Assert.Equal(3, inner.Split);
        Assert.Equal("─────────┤", d.Row(5, 1, 10));

        outer.Right = null;
        Assert.Null(outerRight.Parent);
        Assert.Single(outer.Children);
        Assert.Throws<InvalidOperationException>(() =>
        {
            outer.Children.Add(new Panel());
            outer.Children.Add(new Panel());
        });
        Assert.Equal(2, outer.Children.Count);
    }
}
