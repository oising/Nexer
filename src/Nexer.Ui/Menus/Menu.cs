// Ported from Jexer menu/TMenu.java, menu/TMenuItem.java, menu/TSubMenu.java and menu/TMenuSeparator.java
// (MIT, © Autumn Lamonte).
using System.Collections.ObjectModel;
using System.Text;
using Nexer.Input;

namespace Nexer.Ui.Menus;

/// <summary>
/// A drop-down menu (Jexer <c>TMenu</c>). Add to <see cref="Application.Menus"/> for the menu bar, or open with
/// <see cref="Application.OpenContextMenu"/>. Children are <see cref="MenuItem"/>, <see cref="MenuSeparator"/> and
/// <see cref="SubMenu"/>; <see cref="Window.Title"/> takes a mnemonic (<c>"&amp;File"</c>).
/// </summary>
public class Menu : Window
{
    private MnemonicString mnemonic = new("");

    /// <summary>Creates a menu.</summary>
    public Menu()
    {
        CloseBox = false;
        ZoomBox = false;
        Resizable = false;
        ActiveBorderStyle = BorderStyle.Single;
        ModalBorderStyle = BorderStyle.Single;
        InactiveBorderStyle = BorderStyle.Single;
        MovingBorderStyle = BorderStyle.Single;
        AbsolutePosition = true;
        PositionIsDesktopRelative = false;
        Height = 2;
        Width = 4;
    }

    /// <summary>The title with an optional mnemonic marker (<c>"&amp;File"</c>); reads back without the marker.</summary>
    public override string? Title
    {
        get => mnemonic.RawLabel;
        set
        {
            mnemonic = new MnemonicString(value ?? "");
            UpdateLayout();
            OnPropertyChanged();
        }
    }

    /// <summary>The parsed title mnemonic.</summary>
    public MnemonicString Mnemonic => mnemonic;

    /// <summary>Whether items reserve two columns for an icon (Jexer <c>jexer.menuIcons</c>, default true).</summary>
    public bool UseIcons
    {
        get => useIconsValue;
        set => SetProperty(ref useIconsValue, value);
    }

    private bool useIconsValue = true;

    /// <summary>True while the menu is open.</summary>
    public bool IsOpen
    {
        get => IsActive;
        internal set
        {
            if (value) UpdateLayout();
            IsActive = value;
            OnPropertyChanged();
        }
    }

    /// <summary>True while the menu is shown as a context menu.</summary>
    public bool IsContext { get; private set; }

    internal bool IsSubMenu { get; set; }

    internal int TitleX { get; set; }

    /// <inheritdoc/>
    protected internal override int ChildOffset => 0;

    internal void SetContext(bool context, int x, int y)
    {
        IsContext = context;
        var app = Application;
        if (context)
        {
            UpdateLayout();
            X = x;
            Y = y;
            if (app != null)
            {
                while (X > 0 && X + Width > app.ScreenWidth) X--;
                while (Y > 0 && Y + Height > app.DesktopBottom) Y--;
            }
        }
        else if (!IsSubMenu)
        {
            X = 0;
            Y = 1;
            app?.RecomputeMenuX();
        }
    }

    /// <summary>Recomputes the menu size and item positions (Jexer <c>addItem</c> sizing).</summary>
    internal void UpdateLayout()
    {
        var items = ChildList;
        int width = TextMetrics.Width(mnemonic.RawLabel) + 4;
        foreach (var w in items)
        {
            if (w is MenuItem item) width = Math.Max(width, item.PreferredWidth + 2);
        }

        int height = items.Count + 2;
        SetDimensions(X, Y, width, height);
        for (int i = 0; i < items.Count; i++) items[i].SetDimensions(1, i + 1, width - 2, 1);
    }

    /// <inheritdoc/>
    protected override void OnChildAdded(Widget child)
    {
        if (child is not MenuItem) throw new InvalidOperationException("Menus may only contain MenuItem, MenuSeparator and SubMenu.");
        UpdateLayout();
        Activate(0);
    }

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        var background = GetColor("tmenu");
        context.Fill(0, 0, Width, Height, new Rune(' '), background);
        var style = CurrentBorderStyle;
        context.PutChar(1, 0, style.TopLeft, background);
        context.PutChar(Width - 2, 0, style.TopRight, background);
        context.PutChar(1, Height - 1, style.BottomLeft, background);
        context.PutChar(Width - 2, Height - 1, style.BottomRight, background);
        context.HLine(2, 0, Width - 4, style.Horizontal, background);
        context.HLine(2, Height - 1, Width - 4, style.Horizontal, background);
        if (Application?.Translucence != true) context.DrawBoxShadow(0, 0, Width, Height);
    }

    /// <inheritdoc/>
    protected internal override void OnMouseDown(UiMouseEvent mouse) => RouteToItem(mouse, static (w, m) => w.OnMouseDown(m));

    /// <inheritdoc/>
    protected internal override void OnMouseUp(UiMouseEvent mouse) => RouteToItem(mouse, static (w, m) => w.OnMouseUp(m));

    /// <inheritdoc/>
    protected internal override void OnMouseDoubleClick(UiMouseEvent mouse)
    {
    }

    /// <inheritdoc/>
    protected internal override void OnMouseMotion(UiMouseEvent mouse)
    {
        foreach (var w in ChildList.ToArray())
        {
            if (mouse.IsLeft && w.MouseWouldHit(mouse))
            {
                Activate(w);
                if (w is SubMenu sub) sub.Dispatch();
                return;
            }
        }
    }

    private void RouteToItem(UiMouseEvent mouse, Action<Widget, UiMouseEvent> handler)
    {
        foreach (var w in ChildList.ToArray())
        {
            if (w.MouseWouldHit(mouse))
            {
                Activate(w);
                handler(w, mouse.RelativeTo(w));
                return;
            }
        }
    }

    /// <inheritdoc/>
    protected internal override void OnKeypress(KeyEvent key)
    {
        if (ActiveChild is SubMenu { InnerMenu.IsOpen: true } openSub)
        {
            openSub.InnerMenu.OnKeypress(key);
            return;
        }

        var app = Application;
        bool plain = key.Modifiers == KeyModifiers.None;
        if (plain)
        {
            switch (key.Key)
            {
                case Key.Escape:
                    app?.CloseMenu();
                    return;
                case Key.Down:
                    SwitchWidget(true);
                    return;
                case Key.Up:
                    SwitchWidget(false);
                    return;
                case Key.Right:
                    if (!IsContext) app?.SwitchMenu(true);
                    return;
                case Key.Left:
                    if (IsSubMenu) app?.CloseSubMenu();
                    else if (!IsContext) app?.SwitchMenu(false);
                    return;
            }
        }

        if (key.Key == Key.Character && (key.Modifiers & (KeyModifiers.Alt | KeyModifiers.Ctrl)) == 0)
        {
            foreach (var w in ChildList.ToArray())
            {
                if (w is MenuItem item && item.IsEffectivelyEnabled && item.Mnemonic.Matches(key.Char))
                {
                    Activate(item);
                    item.HandleKey(new KeyEvent(Key.Enter));
                    return;
                }
            }
        }

        ActiveChild?.HandleKey(key);
    }
}

/// <summary>
/// A menu item (Jexer <c>TMenuItem</c>). <see cref="Text"/> takes a mnemonic; <see cref="Shortcut"/> is a global
/// accelerator (e.g. <c>"Ctrl+Q"</c>). Choosing the item raises <see cref="Click"/> and posts <see cref="Command"/>.
/// </summary>
public class MenuItem : Widget
{
    private MnemonicString mnemonic = new("");

    /// <summary>Creates a menu item.</summary>
    public MenuItem()
    {
        Height = 1;
    }

    /// <summary>The label with an optional mnemonic marker (<c>"&amp;Open"</c>); reads back without the marker.</summary>
    public virtual string Text
    {
        get => mnemonic.RawLabel;
        set
        {
            mnemonic = new MnemonicString(value ?? "");
            (Parent as Menu)?.UpdateLayout();
            OnPropertyChanged();
        }
    }

    /// <summary>The parsed mnemonic.</summary>
    public MnemonicString Mnemonic => mnemonic;

    /// <summary>Global accelerator, active whenever no menu is open and no modal window is shown.</summary>
    public KeyGesture Shortcut
    {
        get => shortcut;
        set => SetProperty(ref shortcut, value);
    }

    private KeyGesture shortcut;

    /// <summary>Whether choosing the item toggles <see cref="Checked"/>.</summary>
    public bool Checkable
    {
        get => checkableValue;
        set => SetProperty(ref checkableValue, value);
    }

    private bool checkableValue;

    /// <summary>Whether a check mark is shown (only when <see cref="Checkable"/>).</summary>
    public bool Checked
    {
        get => checkedValue;
        set => SetProperty(ref checkedValue, value);
    }

    private bool checkedValue;

    /// <summary>Optional icon (one grapheme, typically an emoji) drawn when the menu uses icons.</summary>
    public string? Icon
    {
        get => iconValue;
        set => SetProperty(ref iconValue, value);
    }

    private string? iconValue;

    /// <summary>
    /// Command executed when the item is chosen (an <see cref="System.Windows.Input.ICommand"/>; a named <see cref="Ui.Command"/>
    /// such as <c>Command="Exit"</c> is dispatched through <see cref="Application.ExecuteCommand"/>). While attached,
    /// <c>CanExecute</c> feeds <see cref="Widget.IsEffectivelyEnabled"/>.
    /// </summary>
    public System.Windows.Input.ICommand? Command
    {
        get => CommandCore;
        set => CommandCore = value;
    }

    /// <summary>The parameter passed to <see cref="Command"/>'s <c>CanExecute</c>/<c>Execute</c>.</summary>
    public object? CommandParameter
    {
        get => CommandParameterCore;
        set => CommandParameterCore = value;
    }
    /// <summary>Raised (on the UI thread, after the menu closes) when the item is chosen.</summary>
    public event EventHandler? Click;

    /// <inheritdoc/>
    protected internal override bool LeftRightSwitchesWidget => false;

    private bool UsesIcons => (Parent as Menu)?.UseIcons ?? true;

    internal virtual int PreferredWidth
    {
        get
        {
            int label = TextMetrics.Width(Text);
            int width = label + (UsesIcons ? 6 : 4);
            if (Shortcut.Key != Key.None)
            {
                width = Math.Max(width, label + 4 + TextMetrics.Width(Shortcut.ToString()) + 2 + (UsesIcons ? 2 : 0));
            }

            return width;
        }
    }

    /// <summary>Chooses the item: closes the menu, toggles <see cref="Checked"/> if checkable, then raises
    /// <see cref="Click"/> and posts <see cref="Command"/> on the UI loop.</summary>
    public virtual void Dispatch()
    {
        var app = Application ?? Application.Current;
        app?.CloseMenu();
        if (Checkable) Checked = !Checked;
        if (app == null)
        {
            RaiseClick();
            return;
        }

        app.Post(() =>
        {
            RaiseClick();
            ExecuteCommandCore(app);
        });
    }

    /// <summary>Raises <see cref="Click"/>.</summary>
    protected virtual void RaiseClick() => Click?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc/>
    protected internal override void OnMouseUp(UiMouseEvent mouse)
    {
        if (mouse.Y == 0 && mouse.X >= 0 && mouse.X < Width && mouse.IsLeft) Dispatch();
    }

    /// <inheritdoc/>
    protected internal override void OnKeypress(KeyEvent key)
    {
        if (key.Key == Key.Enter && key.Modifiers == KeyModifiers.None)
        {
            Dispatch();
            return;
        }

        base.OnKeypress(key);
    }

    /// <summary>Colour of the label for the current state.</summary>
    protected Nexer.Core.CellStyle MenuColor =>
        IsAbsoluteActive ? GetColor("tmenu.highlighted") : IsEffectivelyEnabled ? GetColor("tmenu") : GetColor("tmenu.disabled");

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        var background = GetColor("tmenu");
        var menuColor = MenuColor;
        var mnemonicColor = IsAbsoluteActive ? GetColor("tmenu.mnemonic.highlighted")
            : IsEffectivelyEnabled ? GetColor("tmenu.mnemonic") : GetColor("tmenu.disabled");
        bool useIcons = UsesIcons;
        var style = (Parent as Menu)?.CurrentBorderStyle ?? BorderStyle.Single;
        context.PutChar(0, 0, style.Vertical, background);
        context.PutChar(Width - 1, 0, style.Vertical, background);
        context.HLine(1, 0, Width - 2, ' ', menuColor);
        int labelX = 2 + (useIcons ? 2 : 0);
        context.PutString(labelX, 0, mnemonic.RawLabel, menuColor);
        if (Shortcut.Key != Key.None)
        {
            string keyLabel = Shortcut.ToString();
            context.PutString(Width - TextMetrics.Width(keyLabel) - 2, 0, keyLabel, menuColor);
        }

        if (mnemonic.ScreenShortcutIndex >= 0) context.PutChar(labelX + mnemonic.ScreenShortcutIndex, 0, mnemonic.Shortcut, mnemonicColor);
        if (Checkable && Checked) context.PutChar(1, 0, GraphicsChars.Check, menuColor);
        if (useIcons && !string.IsNullOrEmpty(Icon)) context.PutString(2, 0, Icon, menuColor);
    }
}

/// <summary>A horizontal separator line in a menu (Jexer <c>TMenuSeparator</c>).</summary>
public class MenuSeparator : MenuItem
{
    /// <summary>Creates a separator.</summary>
    public MenuSeparator()
    {
        Enabled = false;
    }

    internal override int PreferredWidth => 0;

    /// <inheritdoc/>
    public override void Dispatch()
    {
    }

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        var background = GetColor("tmenu");
        var style = (Parent as Menu)?.CurrentBorderStyle ?? BorderStyle.Single;
        char left = GraphicsChars.CP437[0xC3], right = GraphicsChars.CP437[0xB4];
        if (style.Vertical.Value == GraphicsChars.WindowSideDouble)
        {
            left = '\u255F';
            right = '\u2562';
        }

        if (ReferenceEquals(style, BorderStyle.None))
        {
            left = ' ';
            right = ' ';
        }

        context.PutChar(0, 0, left, background);
        context.PutChar(Width - 1, 0, right, background);
        context.HLine(1, 0, Width - 2, GraphicsChars.SingleBar, background);
    }
}

/// <summary>
/// A menu item that opens a nested menu (Jexer <c>TSubMenu</c>). Its <see cref="Widget.Children"/> are the nested
/// menu's items.
/// </summary>
public class SubMenu : MenuItem
{
    private readonly Menu menu = new() { IsSubMenu = true };

    /// <summary>Creates a submenu item.</summary>
    public SubMenu()
    {
        // The nested menu inherits this item's DataContext and is attached/detached with it.
        menu.LogicalParent = this;
    }

    /// <summary>The nested menu.</summary>
    public Menu InnerMenu => menu;

    private protected override IEnumerable<Widget> LogicalChildren => [menu];

    private protected override WidgetCollection? RedirectedChildren => menu.Children;

    /// <inheritdoc/>
    public override string Text
    {
        get => base.Text;
        set
        {
            base.Text = value;
            menu.Title = value;
        }
    }

    internal override int PreferredWidth => TextMetrics.Width(Text) + 6;

    /// <summary>Opens the nested menu to the right of this item.</summary>
    public override void Dispatch()
    {
        var app = Application;
        if (app == null || !IsAbsoluteActive || menu.IsOpen) return;
        menu.SetApplication(app);
        menu.UpdateLayout();
        int x = AbsoluteX + Width - 1, y = AbsoluteY;
        while (x > 0 && x + menu.Width > app.ScreenWidth) x--;
        while (y > 0 && y + menu.Height > app.DesktopBottom) y--;
        menu.X = x;
        menu.Y = y;
        app.AddSubMenu(menu);
        menu.IsOpen = true;
        if (Parent is Menu { IsContext: true }) menu.SetContext(true, menu.X, menu.Y);
    }

    /// <inheritdoc/>
    protected internal override void OnKeypress(KeyEvent key)
    {
        bool plain = key.Modifiers == KeyModifiers.None;
        if (key.Key == Key.Character && (key.Modifiers & (KeyModifiers.Alt | KeyModifiers.Ctrl)) == 0 && Mnemonic.Matches(key.Char))
        {
            Dispatch();
            return;
        }

        if (menu.IsOpen)
        {
            menu.OnKeypress(key);
            return;
        }

        if (!plain) return;
        var app = Application;
        var parentMenu = Parent as Menu;
        switch (key.Key)
        {
            case Key.Enter:
            case Key.Right:
                Dispatch();
                return;
            case Key.Down:
                Parent?.SwitchWidget(true);
                return;
            case Key.Up:
                Parent?.SwitchWidget(false);
                return;
            case Key.Left:
                if (parentMenu?.IsSubMenu == true) app?.CloseSubMenu();
                else if (parentMenu?.IsContext != true) app?.SwitchMenu(false);
                return;
            case Key.Escape:
                app?.CloseMenu();
                return;
        }
    }

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        base.Draw(context);
        context.PutChar(Width - 2, 0, GraphicsChars.CP437[0x10], MenuColor);
    }
}

/// <summary>The menu bar menus (<see cref="Application.Menus"/>).</summary>
public sealed class MenuCollection : Collection<Menu>
{
    private readonly Application application;

    internal MenuCollection(Application application) => this.application = application;

    /// <inheritdoc/>
    protected override void InsertItem(int index, Menu item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Application != null && item.Application != application) throw new ArgumentException("The menu belongs to another application.", nameof(item));
        base.InsertItem(index, item);
        application.OnMenusChanged();
    }

    /// <inheritdoc/>
    protected override void RemoveItem(int index)
    {
        this[index].SetApplication(null);
        base.RemoveItem(index);
        application.OnMenusChanged();
    }

    /// <inheritdoc/>
    protected override void SetItem(int index, Menu item)
    {
        ArgumentNullException.ThrowIfNull(item);
        this[index].SetApplication(null);
        base.SetItem(index, item);
        application.OnMenusChanged();
    }

    /// <inheritdoc/>
    protected override void ClearItems()
    {
        foreach (var m in this) m.SetApplication(null);
        base.ClearItems();
        application.OnMenusChanged();
    }

    /// <summary>Finds the menu item whose <see cref="MenuItem.Shortcut"/> matches <paramref name="key"/>.</summary>
    public MenuItem? FindAccelerator(KeyEvent key)
    {
        ArgumentNullException.ThrowIfNull(key);
        foreach (var menu in this)
        {
            if (Find(menu, key) is { } item) return item;
        }

        return null;
    }

    private static MenuItem? Find(Menu menu, KeyEvent key)
    {
        foreach (var w in menu.ChildList)
        {
            if (w is SubMenu sub)
            {
                if (Find(sub.InnerMenu, key) is { } nested) return nested;
            }
            else if (w is MenuItem item && item.Shortcut.Key != Key.None && item.Shortcut.Matches(key))
            {
                return item;
            }
        }

        return null;
    }
}
