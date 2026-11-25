// Ported from Jexer TWindow.java (MIT, © Autumn Lamonte).
using System.ComponentModel;
using Nexer.Core;
using Nexer.Input;
using Nexer.Ui.Menus;

namespace Nexer.Ui;

/// <summary>
/// A top-level window with Jexer chrome: border, centred title, close box <c>[■]</c>, zoom box <c>[↑]</c> and a
/// resize grip. Add to <see cref="Ui.Application.Windows"/> (or call <see cref="Show"/>) to display it.
/// </summary>
/// <remarks>
/// <para>Before a window is added, <see cref="Widget.X"/>/<see cref="Widget.Y"/> are relative to the desktop
/// (as in Jexer's constructor, which adds the desktop top). Once added they are screen coordinates.</para>
/// <para>Derive from this class (<c>partial class MyWindow : Window</c>) for XAML code-behind.</para>
/// </remarks>
public class Window : Widget
{
    private Application? application;
    private bool maximized;
    private int restoreX, restoreY, restoreWidth, restoreHeight;
    private bool inWindowMove, inWindowResize, inKeyboardResize;
    private int moveMouseX, moveMouseY, oldWindowX, oldWindowY, resizeWidth, resizeHeight;
    private UiMouseEvent? mouse;
    private bool modal;
    private bool centered;
    private StatusBar? statusBar;
    private TaskCompletionSource<object?>? modalCompletion;

    /// <summary>Creates a window (default size 40x10, resizable, with close and zoom boxes).</summary>
    public Window()
    {
        Width = 40;
        Height = 10;
    }

    /// <summary>The application this window has been added to, if any.</summary>
    public override Application? Application => application;

    /// <summary>The window title, drawn centred in the top border.</summary>
    public virtual string? Title { get; set; }

    /// <summary>Whether the user can resize the window (Jexer <c>RESIZABLE</c>, default true).</summary>
    public bool Resizable { get; set; } = true;

    /// <summary>Whether the window is modal (implies <see cref="Centered"/>).</summary>
    public bool Modal
    {
        get => modal;
        set
        {
            modal = value;
            if (value) centered = true;
        }
    }

    /// <summary>Whether the window is centred on the desktop when shown.</summary>
    public bool Centered
    {
        get => centered;
        set => centered = value;
    }

    /// <summary>Whether the close box is shown and Ctrl+W closes the window (default true; Jexer <c>NOCLOSEBOX</c>).</summary>
    public bool CloseBox { get; set; } = true;

    /// <summary>Whether the zoom box is shown and F5 maximizes (default true; Jexer <c>NOZOOMBOX</c>).</summary>
    public bool ZoomBox { get; set; } = true;

    /// <summary>When true, closing hides the window instead of removing it (Jexer <c>HIDEONCLOSE</c>).</summary>
    public bool HideOnClose { get; set; }

    /// <summary>When true, the position is used as-is (no smart placement; Jexer <c>ABSOLUTEXY</c>).</summary>
    public bool AbsolutePosition { get; set; }

    /// <summary>Minimum width when resizing (default 10).</summary>
    public int MinimumWidth { get; set; } = 10;

    /// <summary>Minimum height when resizing (default 2).</summary>
    public int MinimumHeight { get; set; } = 2;

    /// <summary>Maximum width when resizing; values not above the minimum mean unlimited.</summary>
    public int MaximumWidth { get; set; }

    /// <summary>Maximum height when resizing; values not above the minimum mean unlimited.</summary>
    public int MaximumHeight { get; set; }

    /// <summary>Border style while active (default double).</summary>
    public BorderStyle ActiveBorderStyle { get; set; } = BorderStyle.Double;

    /// <summary>Border style while active and modal (default double).</summary>
    public BorderStyle ModalBorderStyle { get; set; } = BorderStyle.Double;

    /// <summary>Border style while inactive (default single).</summary>
    public BorderStyle InactiveBorderStyle { get; set; } = BorderStyle.Single;

    /// <summary>Border style while being moved or resized (default single).</summary>
    public BorderStyle MovingBorderStyle { get; set; } = BorderStyle.Single;

    /// <summary>Per-window colour theme override; null uses the application theme.</summary>
    public ColorTheme? OwnTheme { get; set; }

    /// <summary>True while the window is hidden.</summary>
    public bool IsHidden { get; internal set; }

    /// <summary>True while the window is shown.</summary>
    public bool IsShown => !IsHidden;

    /// <summary>True while the window is maximized.</summary>
    public bool IsMaximized => maximized;

    /// <summary>True for the desktop.</summary>
    public virtual bool IsDesktop => false;

    /// <summary>The window's status bar, drawn on the bottom screen row while the window is on top.</summary>
    public StatusBar? StatusBar
    {
        get => statusBar;
        set
        {
            statusBar = value;
            if (value != null) value.Owner = this;
        }
    }

    /// <summary>The value passed to <see cref="Close(object?)"/>; result of <see cref="ShowModalAsync"/>.</summary>
    public object? DialogResult { get; set; }

    /// <summary>Raised before the window closes; set <see cref="CancelEventArgs.Cancel"/> to keep it open.</summary>
    public event EventHandler<CancelEventArgs>? Closing;

    /// <summary>Raised after the window has been removed from the application.</summary>
    public event EventHandler? Closed;

    /// <summary>Raised when the window becomes the active window.</summary>
    public event EventHandler? Activated;

    /// <summary>Raised when the window stops being the active window.</summary>
    public event EventHandler? Deactivated;

    /// <inheritdoc/>
    protected internal override int ChildOffset => 1;

    /// <summary>True while the window is moving or resizing (mouse or keyboard).</summary>
    public bool IsInMovement => inWindowMove || inWindowResize || inKeyboardResize;

    internal int Z { get; set; }

    internal bool PositionIsDesktopRelative { get; set; } = true;

    internal void SetApplication(Application? app) => application = app;

    /// <summary>
    /// Called when the window is added to an application, before it is placed; dialogs build their content here
    /// (for example to fit the screen size).
    /// </summary>
    protected internal virtual void OnAddedToApplication()
    {
    }

    /// <summary>Stops any mouse/keyboard move or resize in progress.</summary>
    public void StopMovements()
    {
        inWindowMove = false;
        inWindowResize = false;
        inKeyboardResize = false;
    }

    // ---- lifecycle ----

    /// <summary>Shows the window: adds it to <see cref="Ui.Application.Current"/> if needed, otherwise un-hides it.</summary>
    public void Show()
    {
        if (application == null)
        {
            var app = Application.Current ?? throw new InvalidOperationException("No application; create an Application first.");
            app.Windows.Add(this);
            return;
        }

        application.ShowWindow(this);
    }

    /// <summary>Hides the window (it stays in the window list).</summary>
    public void Hide() => application?.HideWindow(this);

    /// <summary>Closes the window.</summary>
    public void Close() => application?.CloseWindow(this);

    /// <summary>Sets <see cref="DialogResult"/> and closes the window.</summary>
    public void Close(object? result)
    {
        DialogResult = result;
        Close();
    }

    /// <summary>
    /// Shows the window modally. Input goes only to modal windows until it closes; the returned task completes
    /// with <see cref="DialogResult"/> when it closes. No nested loop is run.
    /// </summary>
    public Task<object?> ShowModalAsync()
    {
        Modal = true;
        modalCompletion ??= new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = modalCompletion.Task;
        if (application == null) Show();
        else application.ShowWindow(this);
        return task;
    }

    /// <summary>Shows the window modally and returns <see cref="DialogResult"/> converted to <typeparamref name="T"/>.</summary>
    public async Task<T?> ShowModalAsync<T>()
    {
        var result = await ShowModalAsync().ConfigureAwait(true);
        return result is T t ? t : default;
    }

    /// <summary>Called before closing; return false (or cancel <see cref="Closing"/>) to keep the window open.</summary>
    protected internal virtual bool OnClosing()
    {
        var args = new CancelEventArgs();
        Closing?.Invoke(this, args);
        return !args.Cancel;
    }

    /// <summary>Called after the window has been removed from the application.</summary>
    protected internal virtual void OnClosed()
    {
        Closed?.Invoke(this, EventArgs.Empty);
        var completion = modalCompletion;
        modalCompletion = null;
        completion?.TrySetResult(DialogResult);
    }

    /// <summary>Called when the window becomes active (Jexer <c>onFocus</c>).</summary>
    protected internal virtual void OnActivated() => Activated?.Invoke(this, EventArgs.Empty);

    /// <summary>Called when the window becomes inactive (Jexer <c>onUnfocus</c>).</summary>
    protected internal virtual void OnDeactivated() => Deactivated?.Invoke(this, EventArgs.Empty);

    /// <summary>Called when the window is shown after being hidden.</summary>
    protected internal virtual void OnShown()
    {
    }

    /// <summary>Called when the window is hidden.</summary>
    protected internal virtual void OnHidden()
    {
    }

    // ---- geometry ----

    /// <summary>Centres the window on the desktop if <see cref="Centered"/> (Jexer <c>center</c>).</summary>
    public void Center()
    {
        if (!centered || application == null) return;
        int screenWidth = application.ScreenWidth;
        X = Width < screenWidth ? (screenWidth - Width) / 2 : 0;
        int y = (application.DesktopBottom - application.DesktopTop - Height) / 2;
        Y = Math.Max(0, y) + application.DesktopTop;
    }

    /// <summary>Maximizes the window to the desktop area.</summary>
    public void Maximize()
    {
        if (maximized || application == null) return;
        restoreWidth = Width;
        restoreHeight = Height;
        restoreX = X;
        restoreY = Y;
        SetDimensions(0, application.DesktopTop, application.ScreenWidth, application.DesktopBottom - application.DesktopTop);
        maximized = true;
    }

    /// <summary>Restores a maximized window.</summary>
    public void Restore()
    {
        if (!maximized) return;
        SetDimensions(restoreX, restoreY, restoreWidth, restoreHeight);
        maximized = false;
    }

    /// <summary>Called when the screen is resized (Jexer <c>onResize(SCREEN)</c>). Maximized windows follow the desktop.</summary>
    protected internal virtual void OnScreenResized()
    {
        if (maximized && application != null)
        {
            SetDimensions(0, application.DesktopTop, application.ScreenWidth, application.DesktopBottom - application.DesktopTop);
        }
    }

    /// <inheritdoc/>
    protected internal override void OnSizeChanged()
    {
        var children = ChildList;
        if (children.Count == 1 && children[0] is IFillsWindow)
        {
            var child = children[0];
            child.SetDimensions(child.X, child.Y, Math.Max(0, Width - 2 * ChildOffset), Math.Max(0, Height - 2 * ChildOffset));
            return;
        }

        base.OnSizeChanged();
    }

    /// <inheritdoc/>
    protected override void OnChildAdded(Widget child)
    {
        ArgumentNullException.ThrowIfNull(child);
        // A Panel/SplitPane added without a size fills the client area (Jexer code sizes them explicitly).
        if (ChildList.Count == 1 && child is IFillsWindow && child.Width == 0 && child.Height == 0)
        {
            child.SetDimensions(child.X, child.Y, Math.Max(0, Width - 2 * ChildOffset), Math.Max(0, Height - 2 * ChildOffset));
        }

        base.OnChildAdded(child);
    }

    // ---- appearance ----

    /// <summary>Current border colour (Jexer <c>getBorder</c>, without its side effects).</summary>
    protected virtual CellStyle BorderColor
    {
        get
        {
            if (!modal && IsInMovement)
            {
                return GetColor(IsActive ? "twindow.border.windowmove" : "twindow.border.inactive");
            }

            if (modal && (inWindowMove || inWindowResize)) return GetColor("twindow.border.modal.windowmove");
            if (modal) return GetColor(IsActive ? "twindow.border.modal" : "twindow.border.modal.inactive");
            return GetColor(IsActive ? "twindow.border" : "twindow.border.inactive");
        }
    }

    /// <summary>Current background colour (Jexer <c>getBackground</c>).</summary>
    protected virtual CellStyle BackgroundColor
    {
        get
        {
            if (!modal && IsInMovement) return GetColor("twindow.background.windowmove");
            if (modal && (inWindowMove || inWindowResize)) return GetColor("twindow.background.modal");
            if (modal) return GetColor(IsActive ? "twindow.background.modal" : "twindow.background.modal.inactive");
            return GetColor(IsActive ? "twindow.background" : "twindow.background.inactive");
        }
    }

    /// <summary>Colour of the close/zoom/resize controls.</summary>
    protected CellStyle BorderControlsColor => GetColor(modal ? "twindow.border.modal.windowmove" : "twindow.border.windowmove");

    /// <summary>Current border style.</summary>
    public BorderStyle CurrentBorderStyle
    {
        get
        {
            if (!modal && IsInMovement) return MovingBorderStyle;
            if (modal && (inWindowMove || inWindowResize)) return MovingBorderStyle;
            if (modal) return IsActive ? ModalBorderStyle : InactiveBorderStyle;
            return IsActive ? ActiveBorderStyle : InactiveBorderStyle;
        }
    }

    /// <summary>Whether a shadow is drawn (true unless the application uses translucence).</summary>
    protected virtual bool DrawsShadow => application?.Translucence != true;

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        var border = BorderColor;
        var background = BackgroundColor;
        var style = CurrentBorderStyle;
        context.DrawBox(0, 0, Width, Height, border, background, style, DrawsShadow);

        if (!string.IsNullOrEmpty(Title))
        {
            int titleLength = TextMetrics.Width(Title);
            int titleLeft = (Width - titleLength - 2) / 2;
            context.PutChar(titleLeft, 0, ' ', border);
            context.PutString(titleLeft + 1, 0, Title, border);
            context.PutChar(titleLeft + titleLength + 1, 0, ' ', border);
        }

        if (!IsActive) return;
        char lBracket = '[', rBracket = ']';
        if (ReferenceEquals(style, BorderStyle.Round))
        {
            lBracket = '(';
            rBracket = ')';
        }

        bool pressed = mouse != null && mouse.IsLeft && mouse.Action != MouseAction.Up;
        if (CloseBox)
        {
            context.PutChar(2, 0, lBracket, border);
            context.PutChar(4, 0, rBracket, border);
            context.PutChar(3, 0, pressed && MouseOnClose(mouse) ? GraphicsChars.CP437[0x0F] : GraphicsChars.CP437[0xFE], BorderControlsColor);
        }

        if (!modal && ZoomBox)
        {
            context.PutChar(Width - 5, 0, lBracket, border);
            context.PutChar(Width - 3, 0, rBracket, border);
            char glyph = pressed && MouseOnMaximize(mouse) ? GraphicsChars.CP437[0x0F]
                : maximized ? GraphicsChars.CP437[0x12] : GraphicsChars.UpArrow;
            context.PutChar(Width - 4, 0, glyph, BorderControlsColor);
        }

        if (Resizable && !ReferenceEquals(style, BorderStyle.None))
        {
            context.PutChar(Width - 2, Height - 1, style.Horizontal, BorderControlsColor);
            context.PutChar(Width - 1, Height - 1, style.BottomRight, BorderControlsColor);
        }
    }

    // ---- mouse ----

    private bool MouseOnClose(UiMouseEvent? m) =>
        CloseBox && !IsDesktop && m != null && m.AbsoluteY == Y && m.AbsoluteX == X + 3;

    private bool MouseOnMaximize(UiMouseEvent? m) =>
        ZoomBox && !IsDesktop && m != null && !modal && m.AbsoluteY == Y && m.AbsoluteX == X + Width - 4;

    private bool MouseOnResize(UiMouseEvent? m) =>
        Resizable && !IsDesktop && !ReferenceEquals(CurrentBorderStyle, BorderStyle.None) && m != null &&
        m.AbsoluteY == Y + Height - 1 && (m.AbsoluteX == X + Width - 1 || m.AbsoluteX == X + Width - 2);

    /// <inheritdoc/>
    protected internal override void OnMouseDown(UiMouseEvent mouse)
    {
        this.mouse = mouse;
        StopMovements();
        if (!IsDesktop && mouse.AbsoluteY == Y && mouse.IsLeft && !mouse.IsWheelUp && !mouse.IsWheelDown &&
            X <= mouse.AbsoluteX && mouse.AbsoluteX < X + Width && !MouseOnClose(mouse) && !MouseOnMaximize(mouse))
        {
            inWindowMove = true;
            moveMouseX = mouse.AbsoluteX;
            moveMouseY = mouse.AbsoluteY;
            oldWindowX = X;
            oldWindowY = Y;
            maximized = false;
            return;
        }

        if (MouseOnResize(mouse) && mouse.IsLeft)
        {
            inWindowResize = true;
            moveMouseX = mouse.AbsoluteX;
            moveMouseY = mouse.AbsoluteY;
            resizeWidth = Width;
            resizeHeight = Height;
            maximized = false;
            return;
        }

        if (statusBar != null && statusBar.HandleMouseDown(mouse)) return;
        base.OnMouseDown(mouse);
    }

    /// <inheritdoc/>
    protected internal override void OnMouseUp(UiMouseEvent mouse)
    {
        this.mouse = mouse;
        if (inWindowMove && mouse.IsLeft)
        {
            inWindowMove = false;
            return;
        }

        if (inWindowResize && mouse.IsLeft)
        {
            inWindowResize = false;
            return;
        }

        if (mouse.IsLeft && MouseOnClose(mouse))
        {
            if (HideOnClose) application?.HideWindow(this);
            else application?.CloseWindow(this);
            return;
        }

        if (mouse.AbsoluteY == Y && mouse.IsLeft && MouseOnMaximize(mouse))
        {
            if (maximized) Restore();
            else Maximize();
            return;
        }

        if (statusBar != null && statusBar.HandleMouseUp(mouse)) return;
        base.OnMouseUp(mouse);
    }

    /// <inheritdoc/>
    protected internal override void OnMouseMotion(UiMouseEvent mouse)
    {
        this.mouse = mouse;
        var app = application;
        if (inWindowMove && app != null)
        {
            int y = oldWindowY + (mouse.AbsoluteY - moveMouseY);
            if (y < app.DesktopTop) y = app.DesktopTop;
            if (y >= app.DesktopBottom) y = app.DesktopBottom - 1;
            X = oldWindowX + (mouse.AbsoluteX - moveMouseX);
            Y = y;
            return;
        }

        if (inWindowResize && app != null)
        {
            int newWidth = resizeWidth + (mouse.AbsoluteX - moveMouseX);
            int newHeight = resizeHeight + (mouse.AbsoluteY - moveMouseY);
            if (X + newWidth > app.ScreenWidth) newWidth = app.ScreenWidth - X;
            if (newHeight + Y >= app.DesktopBottom) newHeight = app.DesktopBottom - Y;
            newWidth = Math.Max(newWidth, MinimumWidth);
            if (MaximumWidth > MinimumWidth) newWidth = Math.Min(newWidth, MaximumWidth);
            newHeight = Math.Max(newHeight, MinimumHeight);
            if (MaximumHeight > MinimumHeight) newHeight = Math.Min(newHeight, MaximumHeight);
            int y = Y;
            if (y + newHeight > app.DesktopBottom) y = app.DesktopBottom - newHeight + 1;
            if (y < app.DesktopTop) y = app.DesktopTop;
            SetDimensions(X, y, newWidth, newHeight);
            return;
        }

        statusBar?.HandleMouseMotion(mouse);
        base.OnMouseMotion(mouse);
    }

    // ---- keyboard ----

    /// <summary>Returns true if this window wants <paramref name="key"/> before menu accelerators are checked.</summary>
    protected internal virtual bool IsShortcutKey(KeyEvent key) => false;

    /// <inheritdoc/>
    protected internal override void OnKeypress(KeyEvent key)
    {
        bool plain = key.Modifiers == KeyModifiers.None;
        bool shift = key.Modifiers == KeyModifiers.Shift;
        if ((inWindowMove || inWindowResize) && plain && (key.Key == Key.Escape || key.Key == Key.Enter))
        {
            inWindowMove = false;
            inWindowResize = false;
            return;
        }

        if (inKeyboardResize)
        {
            HandleKeyboardResize(key, plain, shift);
            return;
        }

        if (statusBar != null && statusBar.HandleKey(key)) return;

        if (!IsDesktop)
        {
            if (key.Key == Key.Character && key.Char.Value == 'w' && key.Modifiers == KeyModifiers.Ctrl)
            {
                if (CloseBox)
                {
                    if (HideOnClose) application?.HideWindow(this);
                    else application?.CloseWindow(this);
                }

                return;
            }

            if (key.Key == Key.F6 && plain)
            {
                application?.SwitchWindow(true);
                return;
            }

            if (key.Key == Key.F6 && shift)
            {
                application?.SwitchWindow(false);
                return;
            }

            if (key.Key == Key.F5 && plain && ZoomBox)
            {
                if (maximized) Restore();
                else Maximize();
            }

            if (key.Key == Key.F5 && key.Modifiers == KeyModifiers.Ctrl)
            {
                inKeyboardResize = !inKeyboardResize;
            }
        }

        base.OnKeypress(key);
    }

    private void HandleKeyboardResize(KeyEvent key, bool plain, bool shift)
    {
        var app = application;
        if (plain && (key.Key == Key.Escape || key.Key == Key.Enter)) inKeyboardResize = false;
        if (app == null) return;
        if (plain)
        {
            switch (key.Key)
            {
                case Key.Left when X > 0: X--; break;
                case Key.Right when X < app.ScreenWidth - 1: X++; break;
                case Key.Down when Y < app.DesktopBottom - 1: Y++; break;
                case Key.Up when Y > 1: Y--; break;
            }
        }

        if (Resizable && !ReferenceEquals(CurrentBorderStyle, BorderStyle.None) && shift)
        {
            switch (key.Key)
            {
                case Key.Left when Width > MinimumWidth || MinimumWidth <= 0: Width--; break;
                case Key.Right when Width < MaximumWidth || MaximumWidth <= 0: Width++; break;
                case Key.Up when Height > MinimumHeight || MinimumHeight <= 0: Height--; break;
                case Key.Down when Height < MaximumHeight || MaximumHeight <= 0: Height++; break;
            }
        }
    }

    /// <inheritdoc/>
    protected internal override void OnCommand(Command command)
    {
        if (!IsDesktop)
        {
            if (command == Command.WindowClose)
            {
                if (CloseBox)
                {
                    if (HideOnClose) application?.HideWindow(this);
                    else application?.CloseWindow(this);
                }

                return;
            }

            if (command == Command.WindowNext)
            {
                application?.SwitchWindow(true);
                return;
            }

            if (command == Command.WindowPrevious)
            {
                application?.SwitchWindow(false);
                return;
            }

            if (command == Command.WindowMove)
            {
                inKeyboardResize = true;
                return;
            }

            if (command == Command.WindowZoom && ZoomBox)
            {
                if (maximized) Restore();
                else Maximize();
            }
        }

        base.OnCommand(command);
    }

    /// <inheritdoc/>
    public override string ToString() => $"{GetType().Name} '{Title}' ({X},{Y} {Width}x{Height})";
}

/// <summary>Marker for widgets that fill their window's client area when they are its only child (Panel, SplitPane).</summary>
internal interface IFillsWindow
{
}
