// Ported from Jexer TApplication.java and bits/WidgetUtils.java (MIT, © Autumn Lamonte).
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using Nexer.Core;
using Nexer.Hosting;
using Nexer.Input;
using Nexer.Ui.Markup;
using Nexer.Ui.Menus;

namespace Nexer.Ui;

/// <summary>
/// The application: owns the windows, desktop, menu bar and theme, and runs the single-threaded UI loop
/// (Jexer <c>TApplication</c>). Build the UI, add windows to <see cref="Windows"/>, then call
/// <see cref="Run(ITerminalHost)"/> or <see cref="RunAsync"/>.
/// </summary>
/// <remarks>
/// All widget access must happen on the UI thread. While the loop runs, a <see cref="SynchronizationContext"/> is
/// installed so <c>await</c> continuations (e.g. after <see cref="Window.ShowModalAsync()"/>) resume on the UI
/// thread. Use <see cref="Post"/>/<see cref="Invoke"/>/<see cref="InvokeAsync"/> from other threads.
/// </remarks>
public class Application : IResourceProvider
{
    private readonly List<Window> windows = [];
    private readonly List<Window> pendingPlacement = [];
    private readonly List<Menu> subMenus = [];
    private readonly List<UiTimer> timers = [];
    private readonly ConcurrentQueue<Action> posted = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly UiSynchronizationContext context;
    private ResourceDictionary? resources;
    private object? dataContext;
    private Desktop? desktop;
    private CellSurface? surface;
    private ITerminalHost? host;
    private int uiThreadId = -1;
    private volatile bool exitRequested;
    private bool inputCompleted;
    private Task<bool>? inputWait;
    private bool invalidated = true;
    private bool hideMenuBar;
    private bool hideStatusBar;
    private int mouseX = -1, mouseY = -1;
    private long lastMouseClickTime;
    private bool hasLastClick;

    /// <summary>Creates an application and makes it <see cref="Current"/>.</summary>
    public Application()
    {
        context = new UiSynchronizationContext(this);
        Windows = new WindowCollection(this);
        Menus = new MenuCollection(this);
        Desktop = new Desktop();
        Current = this;
    }

    /// <summary>The most recently created application (used by <see cref="Window.Show"/>).</summary>
    public static Application? Current { get; private set; }

    /// <summary>The windows, front (index 0, active) to back.</summary>
    public WindowCollection Windows { get; }

    /// <summary>The menu bar menus, left to right.</summary>
    public MenuCollection Menus { get; }

    /// <summary>The desktop drawn behind all windows (Jexer creates one by default); may be null.</summary>
    public Desktop? Desktop
    {
        get => desktop;
        set
        {
            desktop?.SetApplication(null);
            desktop = value;
            if (value != null)
            {
                value.SetApplication(this);
                value.IsActive = windows.Count == 0;
                ResizeDesktop();
            }
        }
    }

    /// <summary>The colour theme.</summary>
    public ColorTheme Theme { get; set; } = new();

    /// <summary>
    /// The root data context for <c>{Binding}</c>: inherited by every window (and its widgets) that has no data context
    /// of its own. Setting it raises <see cref="Widget.DataContextChanged"/> through the inheriting windows.
    /// </summary>
    public object? DataContext
    {
        get => dataContext;
        set
        {
            if (ReferenceEquals(dataContext, value)) return;
            IEnumerable<Window> roots = windows;
            if (desktop != null) roots = roots.Append(desktop);
            roots = roots.Concat(Menus);
            if (ActiveMenu != null) roots = roots.Append(ActiveMenu);
            var affected = roots.Distinct().Where(w => !w.HasLocalDataContext && w.LogicalParent == null && w.Application == this).ToList();
            dataContext = value;
            foreach (var window in affected)
            {
                window.OnDataContextChanged();
            }
        }
    }

    /// <summary>Application-wide resources (looked up after widget resources).</summary>
    public ResourceDictionary Resources => resources ??= [];

    /// <summary>Hides the menu bar (the desktop then starts at row 0).</summary>
    public bool HideMenuBar
    {
        get => hideMenuBar;
        set
        {
            hideMenuBar = value;
            ResizeDesktop();
        }
    }

    /// <summary>Hides the status bar (the desktop then extends to the last row).</summary>
    public bool HideStatusBar
    {
        get => hideStatusBar;
        set
        {
            hideStatusBar = value;
            ResizeDesktop();
        }
    }

    /// <summary>Whether new windows are placed to minimise overlap (Jexer smart placement, default true).</summary>
    public bool SmartPlacement { get; set; } = true;

    /// <summary>Whether moving the mouse over a window activates it.</summary>
    public bool FocusFollowsMouse { get; set; }

    /// <summary>
    /// Whether windows and menus are alpha-blended (<see cref="Window.Alpha"/>) over what is behind them, with a
    /// blended drop shadow (Jexer translucence). Blended cells use RGB colours, so this is off by default (Jexer: on).
    /// </summary>
    public bool Translucence
    {
        get => translucence;
        set
        {
            translucence = value;
            invalidated = true;
        }
    }

    private bool translucence;

    /// <summary>Maximum delay between two presses at the same cell for a double click (default 250 ms).</summary>
    public TimeSpan DoubleClickTime { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>When true, the Exit command asks for confirmation (Jexer behaviour, default true).</summary>
    public bool ConfirmExit { get; set; } = true;

    /// <summary>Screen width in cells.</summary>
    public int ScreenWidth { get; private set; } = 80;

    /// <summary>Screen height in cells.</summary>
    public int ScreenHeight { get; private set; } = 25;

    /// <summary>First desktop row (1 below the menu bar, or 0 when hidden).</summary>
    public int DesktopTop => hideMenuBar ? 0 : 1;

    /// <summary>Row after the last desktop row (the status bar row, or the screen height when hidden).</summary>
    public int DesktopBottom => hideStatusBar ? ScreenHeight : ScreenHeight - 1;

    /// <summary>The host, once attached.</summary>
    public ITerminalHost? Host => host;

    /// <summary>The clock (the host's, or the system clock before attaching).</summary>
    public TimeProvider Time => host?.Time ?? TimeProvider.System;

    /// <summary>True between attaching to a host and exiting.</summary>
    public bool IsRunning => host != null && !exitRequested;

    /// <summary>The front-most shown active window, or null.</summary>
    public Window? ActiveWindow
    {
        get
        {
            foreach (var w in windows)
            {
                if (w.IsShown && w.IsActive) return w;
            }

            return null;
        }
    }

    /// <summary>The widget that currently receives keystrokes.</summary>
    public Widget? FocusedWidget => (ActiveWindow ?? (Widget?)desktop)?.DeepestActiveChild;

    /// <summary>The open menu, or null.</summary>
    public Menu? ActiveMenu { get; private set; }

    /// <summary>The surface drawn on the last frame.</summary>
    public IReadOnlyCellSurface? Screen => surface;

    /// <summary>True when any window is modal.</summary>
    public bool IsModalActive
    {
        get
        {
            foreach (var w in windows)
            {
                if (w.Modal) return true;
            }

            return false;
        }
    }

    /// <summary>Raised on the UI thread when the loop is about to exit.</summary>
    public event EventHandler? Exiting;

    /// <inheritdoc/>
    public bool TryGetResource(string key, out object? value)
    {
        if (resources != null && resources.TryGetValue(key, out value)) return true;
        value = null;
        return false;
    }

    // ---- loop ----

    /// <summary>Runs the UI loop on the calling thread until <see cref="Exit"/> is called or input ends.</summary>
    public void Run(ITerminalHost host)
    {
        Attach(host);
        try
        {
            while (!exitRequested)
            {
                ProcessPending();
                if (exitRequested || inputCompleted) break;
                WaitForWork();
            }
        }
        finally
        {
            Detach();
        }
    }

    /// <summary>Runs the UI loop on a new dedicated thread; the task completes when the loop exits.</summary>
    public Task RunAsync(ITerminalHost host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(Exit);
        var thread = new Thread(() =>
        {
            try
            {
                Run(host);
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            finally
            {
                registration.Dispose();
            }
        })
        {
            IsBackground = true,
            Name = "Nexer UI",
        };
        thread.Start();
        return tcs.Task;
    }

    /// <summary>
    /// Binds the application to <paramref name="host"/> on the calling thread without running the loop. Use with
    /// <see cref="ProcessPending"/> to drive the application step by step (tests, custom loops).
    /// </summary>
    public void Attach(ITerminalHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (this.host != null) throw new InvalidOperationException("The application is already attached to a host.");
        this.host = host;
        uiThreadId = Environment.CurrentManagedThreadId;
        exitRequested = false;
        inputCompleted = false;
        Current = this;
        SetScreenSize(host.Size.Columns, host.Size.Rows);
        foreach (var w in pendingPlacement.ToArray()) PlaceWindow(w);
        pendingPlacement.Clear();
        foreach (var t in timers.ToArray()) t.Start(host.Time);
        invalidated = true;
    }

    private void Detach()
    {
        Exiting?.Invoke(this, EventArgs.Empty);
        foreach (var t in timers.ToArray()) t.Dispose();
        host = null;
        uiThreadId = -1;
    }

    /// <summary>
    /// Processes all available input, posted work and due timers, then draws and presents a frame if anything
    /// changed. Must be called on the thread that called <see cref="Attach"/>. Returns the number of items processed.
    /// </summary>
    public int ProcessPending()
    {
        var h = host ?? throw new InvalidOperationException("Attach the application to a host first.");
        VerifyAccess();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            int count = 0;
            for (int guard = 0; guard < 100_000 && !exitRequested; guard++)
            {
                bool did = false;
                while (!exitRequested && h.Input.TryRead(out var e))
                {
                    HandleInput(e);
                    count++;
                    did = true;
                }

                while (!exitRequested && posted.TryDequeue(out var action))
                {
                    action();
                    count++;
                    did = true;
                }

                if (!did) break;
            }

            if (h.Input.Completion.IsCompleted && !h.Input.TryPeek(out _)) inputCompleted = true;
            if (count > 0 || invalidated) DrawAndPresent();
            return count;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private void WaitForWork()
    {
        if (!posted.IsEmpty) return;
        if (inputWait == null || inputWait.IsCompleted)
        {
            if (inputWait is { IsCompletedSuccessfully: true, Result: false })
            {
                inputCompleted = true;
                return;
            }

            var vt = host!.Input.WaitToReadAsync();
            if (vt.IsCompleted)
            {
                inputWait = null;
                if (!vt.Result) inputCompleted = true;
                return;
            }

            inputWait = vt.AsTask();
            inputWait.ContinueWith(static (_, s) => ((AutoResetEvent)s!).Set(), wake, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        wake.WaitOne();
        if (inputWait is { IsCompletedSuccessfully: true, Result: false }) inputCompleted = true;
    }

    /// <summary>Requests the loop to exit after the current step (thread-safe).</summary>
    public void Exit()
    {
        exitRequested = true;
        wake.Set();
    }

    /// <summary>Requests a redraw on the next loop step.</summary>
    public void Invalidate()
    {
        invalidated = true;
        wake.Set();
    }

    /// <summary>Queues <paramref name="action"/> to run on the UI thread (thread-safe).</summary>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        posted.Enqueue(action);
        wake.Set();
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread: inline if already on it, otherwise posts and waits.</summary>
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (CheckAccess())
        {
            action();
            return;
        }

        InvokeAsync(action).GetAwaiter().GetResult();
    }

    /// <summary>Queues <paramref name="action"/> on the UI thread and returns a task that completes after it ran.</summary>
    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                action();
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    /// <summary>Queues <paramref name="func"/> on the UI thread and returns its result.</summary>
    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                tcs.TrySetResult(func());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    /// <summary>True when called on the UI thread (or when no loop is attached yet).</summary>
    public bool CheckAccess() => uiThreadId < 0 || uiThreadId == Environment.CurrentManagedThreadId;

    [Conditional("DEBUG")]
    private void VerifyAccess() => Debug.Assert(CheckAccess(), "Not on the UI thread");

    /// <summary>
    /// Adds a timer whose <paramref name="action"/> runs on the UI thread after <paramref name="interval"/>
    /// (repeatedly when <paramref name="recurring"/>). Uses the host's <see cref="TimeProvider"/>. Dispose to stop.
    /// </summary>
    public UiTimer AddTimer(TimeSpan interval, Action action, bool recurring = false)
    {
        ArgumentNullException.ThrowIfNull(action);
        var timer = new UiTimer(this, interval, action, recurring);
        timers.Add(timer);
        if (host != null) timer.Start(host.Time);
        return timer;
    }

    internal void RemoveTimer(UiTimer timer) => timers.Remove(timer);

    // ---- input ----

    private void HandleInput(InputEvent e)
    {
        switch (e)
        {
            case ResizeEvent resize:
                OnScreenResize(resize.Columns, resize.Rows);
                break;
            case KeyEvent key:
                HandleKeyEvent(key);
                break;
            case MouseEvent mouse:
                HandleMouseEvent(mouse);
                break;
            case PasteEvent paste:
                if (ActiveMenu == null) (ActiveWindow ?? (Widget?)desktop)?.HandlePaste(paste.Text);
                break;
        }
    }

    private void HandleKeyEvent(KeyEvent key)
    {
        if (ActiveMenu != null)
        {
            ActiveMenu.HandleKey(key);
            return;
        }

        var window = ActiveWindow;
        bool windowWillShortcut = window != null ? window.IsShortcutKey(key) : desktop?.IsShortcutKey(key) == true;
        if (!windowWillShortcut && !IsModalActive)
        {
            var item = Menus.FindAccelerator(key);
            if (item != null && item.IsEffectivelyEnabled)
            {
                item.Dispatch();
                return;
            }

            if (OnKeypress(key)) return;
        }

        if (window != null) window.HandleKey(key);
        else desktop?.HandleKey(key);
    }

    /// <summary>
    /// Application-level keys (Jexer <c>onKeypress</c>): Alt+mnemonic opens a menu; F10 opens the first menu.
    /// Return true when handled.
    /// </summary>
    protected virtual bool OnKeypress(KeyEvent key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (ActiveMenu != null || IsModalActive || hideMenuBar) return false;
        if (key.Key == Key.Character && key.Modifiers == KeyModifiers.Alt)
        {
            foreach (var menu in Menus)
            {
                if (menu.Mnemonic.Matches(key.Char))
                {
                    OpenMenu(menu);
                    return true;
                }
            }
        }

        if (key.Key == Key.F10 && key.Modifiers == KeyModifiers.None && Menus.Count > 0)
        {
            OpenMenu(Menus[0]);
            return true;
        }

        return false;
    }

    private static UiMouseEvent Translate(MouseEvent e)
    {
        return e.Kind switch
        {
            MouseEventKind.Down => new UiMouseEvent(MouseAction.Down, e.Button, e.X, e.Y, e.X, e.Y, e.Modifiers),
            MouseEventKind.Up => new UiMouseEvent(MouseAction.Up, e.Button, e.X, e.Y, e.X, e.Y, e.Modifiers),
            MouseEventKind.Move => new UiMouseEvent(MouseAction.Motion, MouseButton.None, e.X, e.Y, e.X, e.Y, e.Modifiers),
            MouseEventKind.Drag => new UiMouseEvent(MouseAction.Motion, e.Button, e.X, e.Y, e.X, e.Y, e.Modifiers),
            MouseEventKind.WheelUp => new UiMouseEvent(MouseAction.Down, MouseButton.None, e.X, e.Y, e.X, e.Y, e.Modifiers) { IsWheelUp = true },
            _ => new UiMouseEvent(MouseAction.Down, MouseButton.None, e.X, e.Y, e.X, e.Y, e.Modifiers) { IsWheelDown = true },
        };
    }

    private void HandleMouseEvent(MouseEvent e)
    {
        var mouse = Translate(e);
        UiMouseEvent? doubleClick = null;
        long now = e.Timestamp != 0 ? e.Timestamp : Time.GetTimestamp();
        bool moved = mouseX != mouse.X || mouseY != mouse.Y;
        mouseX = mouse.X;
        mouseY = mouse.Y;
        if (mouse.Action == MouseAction.Down && !mouse.IsWheelUp && !mouse.IsWheelDown)
        {
            if (!moved && hasLastClick && Time.GetElapsedTime(lastMouseClickTime, now) < DoubleClickTime)
            {
                doubleClick = mouse with { Action = MouseAction.DoubleClick };
                hasLastClick = false;
            }
            else
            {
                lastMouseClickTime = now;
                hasLastClick = true;
            }
        }

        CheckSwitchFocus(mouse);

        if (ActiveMenu != null)
        {
            var menu = ActiveMenu;
            while (subMenus.Count > 0)
            {
                var sub = subMenus[^1];
                if (sub.MouseWouldHit(mouse)) break;
                if (mouse.Action == MouseAction.Motion && mouse.Button == MouseButton.None) break;
                CloseSubMenu();
            }

            if (subMenus.Count > 0) menu = subMenus[^1];
            menu.HandleMouse(mouse.RelativeTo(menu));
            return;
        }

        bool dispatchToDesktop = true;
        var window = ActiveWindow;
        if (window != null)
        {
            if (window.MouseWouldHit(mouse)) dispatchToDesktop = false;
            window.HandleMouse(mouse.RelativeTo(window));
            if (doubleClick != null) window.HandleMouse(doubleClick.RelativeTo(window));
            if (window.MouseWouldHit(mouse)) dispatchToDesktop = false;
        }

        if (dispatchToDesktop && desktop != null)
        {
            desktop.HandleMouse(mouse.RelativeTo(desktop));
            if (doubleClick != null) desktop.HandleMouse(doubleClick.RelativeTo(desktop));
        }
    }

    private bool MouseOnMenu(UiMouseEvent mouse)
    {
        Debug.Assert(ActiveMenu != null);
        for (int i = subMenus.Count - 1; i >= 0; i--)
        {
            if (subMenus[i].MouseWouldHit(mouse)) return true;
        }

        return ActiveMenu!.MouseWouldHit(mouse);
    }

    private bool MouseOnMenuTitle(Menu menu, int x) => x >= menu.TitleX && x < menu.TitleX + TextMetrics.Width(menu.Title) + 2;

    private void CheckSwitchFocus(UiMouseEvent mouse)
    {
        if (mouse.Action == MouseAction.Down && ActiveMenu is { IsContext: true } && !MouseOnMenu(mouse))
        {
            CloseMenu();
        }

        if (mouse.Action == MouseAction.Down && ActiveMenu != null && mouse.AbsoluteY != 0 && !MouseOnMenu(mouse))
        {
            CloseMenu();
        }

        if (mouse.Action == MouseAction.Down && mouse.IsLeft && !IsModalActive && mouse.AbsoluteY == 0 && !hideMenuBar)
        {
            CloseSubMenus();
            Menu? hit = null;
            foreach (var menu in Menus)
            {
                if (MouseOnMenuTitle(menu, mouse.AbsoluteX)) hit = menu;
                else menu.IsOpen = false;
            }

            if (hit != null)
            {
                hit.IsOpen = true;
                ActiveMenu = hit;
            }
            else
            {
                ActiveMenu = null;
            }

            return;
        }

        if (mouse.Action == MouseAction.Motion && mouse.IsLeft && ActiveMenu != null && mouse.AbsoluteY == 0 && !hideMenuBar)
        {
            var oldMenu = ActiveMenu;
            CloseSubMenus();
            foreach (var menu in Menus)
            {
                if (MouseOnMenuTitle(menu, mouse.AbsoluteX))
                {
                    menu.IsOpen = true;
                    ActiveMenu = menu;
                }
            }

            if (oldMenu != ActiveMenu) oldMenu.IsOpen = false;
            return;
        }

        if (ActiveMenu != null) return;
        if (windows.Count < 2) return;
        if (!hideStatusBar && mouse.AbsoluteY == DesktopBottom) return;
        if ((FocusFollowsMouse && mouse.Action == MouseAction.Motion) || mouse.Action == MouseAction.Down)
        {
            if (windows[0].Modal || windows[0].IsInMovement) return;
            foreach (var window in windows)
            {
                if (window.IsHidden) continue;
                if (window.MouseWouldHit(mouse))
                {
                    ActivateWindow(window);
                    return;
                }
            }
        }
    }

    // ---- commands ----

    /// <summary>Posts <paramref name="command"/> to be executed on the UI thread.</summary>
    public void PostCommand(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Post(() => ExecuteCommand(command));
    }

    /// <summary>
    /// Executes <paramref name="command"/> now: application commands first (Exit, Tile, Cascade, CloseAll, Menu),
    /// then the active window and the desktop.
    /// </summary>
    public void ExecuteCommand(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (OnCommand(command)) return;
        ActiveWindow?.HandleCommand(command);
        desktop?.HandleCommand(command);
    }

    /// <summary>Application-level commands (Jexer <c>onCommand</c>). Return true when handled.</summary>
    protected virtual bool OnCommand(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command == Command.Exit)
        {
            if (ConfirmExit) _ = ConfirmAndExitAsync();
            else Exit();
            return true;
        }

        if (command == Command.Tile)
        {
            TileWindows();
            return true;
        }

        if (command == Command.Cascade)
        {
            CascadeWindows();
            return true;
        }

        if (command == Command.CloseAll)
        {
            CloseAllWindows();
            return true;
        }

        if (command == Command.Menu && !hideMenuBar && !IsModalActive && ActiveMenu == null && Menus.Count > 0)
        {
            OpenMenu(Menus[0]);
            return true;
        }

        return false;
    }

    /// <summary>Asks "Really quit?" (Jexer's exit dialog) and exits when confirmed.</summary>
    protected virtual async Task ConfirmAndExitAsync()
    {
        if (await ExitConfirmation.ConfirmAsync(this).ConfigureAwait(true)) Exit();
    }

    // ---- menus ----

    /// <summary>Opens a top-level menu.</summary>
    public void OpenMenu(Menu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        CloseMenu();
        menu.IsOpen = true;
        ActiveMenu = menu;
        invalidated = true;
    }

    /// <summary>Opens <paramref name="menu"/> as a context menu at screen position (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public void OpenContextMenu(Menu menu, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(menu);
        CloseMenu();
        menu.SetApplication(this);
        menu.SetContext(true, x, y);
        menu.IsOpen = true;
        ActiveMenu = menu;
        invalidated = true;
    }

    /// <summary>Closes the open menu and its submenus.</summary>
    public void CloseMenu()
    {
        if (ActiveMenu == null) return;
        var menu = ActiveMenu;
        menu.IsOpen = false;
        if (menu.IsContext) menu.SetContext(false, 0, 0);
        ActiveMenu = null;
        invalidated = true;
        CloseSubMenus();

        // Context menus are attached only while open, so their bindings do not outlive them.
        if (!Menus.Contains(menu)) menu.SetApplication(null);
    }

    private void CloseSubMenus()
    {
        foreach (var m in subMenus)
        {
            m.IsOpen = false;
            if (m.IsContext) m.SetContext(false, m.X, m.Y);
        }

        subMenus.Clear();
    }

    internal void AddSubMenu(Menu menu) => subMenus.Add(menu);

    /// <summary>Closes the top-most open submenu.</summary>
    public void CloseSubMenu()
    {
        if (subMenus.Count == 0) return;
        subMenus[^1].IsOpen = false;
        subMenus.RemoveAt(subMenus.Count - 1);
    }

    /// <summary>Opens the next/previous top-level menu (wrapping).</summary>
    public void SwitchMenu(bool forward)
    {
        if (ActiveMenu == null || hideMenuBar) return;
        CloseSubMenus();
        int i = Menus.IndexOf(ActiveMenu);
        if (i < 0) return;
        i = forward ? (i < Menus.Count - 1 ? i + 1 : 0) : (i > 0 ? i - 1 : Menus.Count - 1);
        ActiveMenu.IsOpen = false;
        ActiveMenu = Menus[i];
        ActiveMenu.IsOpen = true;
    }

    internal void OnMenusChanged()
    {
        CloseMenu();
        RecomputeMenuX();
        foreach (var m in Menus) m.SetApplication(this);
    }

    internal void RecomputeMenuX()
    {
        int x = 0;
        foreach (var menu in Menus)
        {
            menu.UpdateLayout();
            menu.X = x;
            menu.Y = DesktopTop == 0 ? 0 : 1;
            menu.TitleX = x;
            x += TextMetrics.Width(menu.Title) + 2;
            if (menu.X + menu.Width > ScreenWidth) menu.X = ScreenWidth - menu.Width;
        }
    }

    // ---- window management ----

    internal void AddWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window is Desktop || window is Menu) throw new ArgumentException("Desktops and menus cannot be added to Windows.", nameof(window));
        if (windows.Contains(window)) throw new ArgumentException("The window is already in the window list.", nameof(window));
        if (window.Application != null && window.Application != this) throw new ArgumentException("The window belongs to another application.", nameof(window));
        window.SetApplication(this);
        window.OnAddedToApplication();
        foreach (var w in windows) w.StopMovements();
        if (IsModalActive)
        {
            window.Modal = true;
            window.IsHidden = false;
        }

        if (window.IsShown)
        {
            foreach (var w in windows)
            {
                if (w.IsActive)
                {
                    w.IsActive = false;
                    w.OnDeactivated();
                }
            }

            windows.Insert(0, window);
            window.IsActive = true;
        }
        else
        {
            windows.Add(window);
        }

        if (host != null) PlaceWindow(window);
        else pendingPlacement.Add(window);
        if (desktop != null) desktop.IsActive = false;
        if (window.IsActive) window.OnActivated();
        invalidated = true;
    }

    private void PlaceWindow(Window window)
    {
        if (window.PositionIsDesktopRelative)
        {
            window.PositionIsDesktopRelative = false;
            window.Y += DesktopTop;
        }

        if (window.Centered) window.Center();
        else if (!window.AbsolutePosition && SmartPlacement) DoSmartPlacement(window);
    }

    /// <summary>
    /// Moves <paramref name="window"/> to the position (within the desktop) with the lowest average overlap with
    /// the other windows (Jexer <c>doSmartPlacement</c>, with its overlap-sum bug fixed). The current position wins ties.
    /// </summary>
    public void DoSmartPlacement(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        int xMin = 0, yMin = DesktopTop;
        int xMax = ScreenWidth - window.Width + 1;
        int yMax = DesktopBottom - window.Height + 1;
        if (xMax < xMin) xMax = xMin;
        if (yMax < yMin) yMax = yMin;
        if (xMin == xMax && yMin == yMax) return;
        int width = ScreenWidth, height = ScreenHeight;
        var overlap = new int[width, height];
        foreach (var w in windows)
        {
            if (w == window || w.PositionIsDesktopRelative || w.IsHidden) continue;
            for (int x = Math.Max(0, w.X); x < Math.Min(width, w.X + w.Width); x++)
            {
                for (int y = Math.Max(0, w.Y); y < Math.Min(height, w.Y + w.Height); y++) overlap[x, y]++;
            }
        }

        long oldTotal = 0, oldN = 0;
        foreach (int v in overlap)
        {
            oldTotal += v;
            if (v > 0) oldN++;
        }

        double Average(int px, int py)
        {
            long total = oldTotal, n = oldN;
            for (int x = Math.Max(0, px); x < Math.Min(width, px + window.Width); x++)
            {
                for (int y = Math.Max(0, py); y < Math.Min(height, py + window.Height); y++)
                {
                    total++;
                    if (overlap[x, y] == 0) n++;
                }
            }

            return n == 0 ? 0 : (double)total / n;
        }

        int bestX = window.X, bestY = window.Y;
        double best = Average(bestX, bestY);
        for (int x = xMin; x < xMax; x++)
        {
            for (int y = yMin; y < yMax; y++)
            {
                double avg = Average(x, y);
                if (avg < best)
                {
                    best = avg;
                    bestX = x;
                    bestY = y;
                }
            }
        }

        window.X = bestX;
        window.Y = bestY;
    }

    /// <summary>Brings <paramref name="window"/> to the front and makes it active (refused for non-modal windows while a modal one is open).</summary>
    public void ActivateWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!windows.Contains(window)) return;
        if (IsModalActive && !window.Modal) return;
        foreach (var w in windows) w.StopMovements();
        if (window.IsHidden)
        {
            ShowWindow(window);
            return;
        }

        if (ActiveWindow == window && windows[0] == window) return;
        windows.Remove(window);
        windows.Insert(0, window);
        foreach (var w in windows)
        {
            if (w != window && w.IsActive)
            {
                w.IsActive = false;
                w.OnDeactivated();
            }
        }

        window.IsActive = true;
        window.OnActivated();
        invalidated = true;
    }

    /// <summary>Hides <paramref name="window"/> and activates the next shown window.</summary>
    public void HideWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!windows.Contains(window) || window.IsHidden) return;
        foreach (var w in windows) w.StopMovements();
        bool wasActive = window.IsActive;
        window.IsActive = false;
        window.IsHidden = true;
        if (wasActive) window.OnDeactivated();
        window.OnHidden();
        foreach (var w in windows)
        {
            if (w.IsShown)
            {
                ActivateWindow(w);
                break;
            }
        }

        invalidated = true;
    }

    /// <summary>Shows a hidden window and activates it.</summary>
    public void ShowWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!windows.Contains(window))
        {
            if (window.Application == null || window.Application == this) AddWindow(window);
            return;
        }

        if (window.IsHidden)
        {
            window.IsHidden = false;
            window.OnShown();
            ActivateWindow(window);
        }
    }

    /// <summary>
    /// Closes <paramref name="window"/>: raises <see cref="Window.Closing"/> (which may cancel), removes it, activates
    /// the next shown window, then raises <see cref="Window.Closed"/>. Returns false if cancelled.
    /// </summary>
    public bool CloseWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!windows.Contains(window)) return false;
        if (!window.OnClosing()) return false;
        window.StopMovements();
        bool wasActive = window.IsActive;
        window.IsActive = false;
        windows.Remove(window);
        if (wasActive) window.OnDeactivated();
        Window? next = null;
        foreach (var w in windows)
        {
            w.StopMovements();
            if (w.IsHidden) continue;
            if (next == null)
            {
                next = w;
            }
            else if (w.IsActive)
            {
                w.IsActive = false;
                w.OnDeactivated();
            }
        }

        if (next != null && !next.IsActive)
        {
            next.IsActive = true;
            next.OnActivated();
        }

        window.SetApplication(null);
        window.PositionIsDesktopRelative = false;
        pendingPlacement.Remove(window);
        if (desktop != null && windows.Count == 0) desktop.IsActive = true;
        invalidated = true;
        window.OnClosed();
        return true;
    }

    /// <summary>Cycles the front window forward/backward (F6 / Shift+F6). Ignored while a modal window is open.</summary>
    public void SwitchWindow(bool forward)
    {
        int shown = 0;
        foreach (var w in windows)
        {
            if (w.IsShown) shown++;
        }

        if (shown < 2 || IsModalActive) return;
        do
        {
            if (forward)
            {
                var first = windows[0];
                windows.RemoveAt(0);
                windows.Add(first);
            }
            else
            {
                var last = windows[^1];
                windows.RemoveAt(windows.Count - 1);
                windows.Insert(0, last);
            }
        }
        while (!windows[0].IsShown);

        var window = windows[0];
        foreach (var w in windows)
        {
            w.StopMovements();
            if (w != window && w.IsActive)
            {
                w.IsActive = false;
                w.OnDeactivated();
            }
        }

        window.IsActive = true;
        window.OnActivated();
        invalidated = true;
    }

    /// <summary>Tiles the shown windows over the desktop (Jexer <c>WidgetUtils.tileWidgets</c>).</summary>
    public void TileWindows()
    {
        if (ActiveMenu != null) return;
        var sorted = new List<Window>();
        for (int i = windows.Count - 1; i >= 0; i--)
        {
            if (windows[i].IsShown) sorted.Add(windows[i]);
        }

        int z = sorted.Count;
        if (z == 0) return;
        int a = (int)Math.Sqrt(z);
        int b = 0, c = 0;
        while (c < a)
        {
            b = (z - c) / a;
            if (a * b + c == z) break;
            c++;
        }

        Debug.Assert(a > 0 && b > 0 && c < a);
        int height = DesktopBottom - DesktopTop;
        int newWidth = ScreenWidth / a;
        int newHeight1 = height / b;
        int newHeight2 = height / (b + c);
        for (int i = 0; i < sorted.Count; i++)
        {
            int logicalX = i / b;
            int logicalY = i % b;
            bool lastColumn = i >= (a - 1) * b;
            if (lastColumn)
            {
                logicalX = a - 1;
                logicalY = i - (a - 1) * b;
            }

            var w = sorted[i];
            int h = lastColumn ? newHeight2 : newHeight1;
            w.SetDimensions(logicalX * newWidth, logicalY * h + DesktopTop, newWidth, h);
        }

        invalidated = true;
    }

    /// <summary>Cascades all windows from the top-left of the desktop.</summary>
    public void CascadeWindows()
    {
        if (ActiveMenu != null) return;
        int x = 0, y = DesktopTop;
        for (int i = windows.Count - 1; i >= 0; i--)
        {
            windows[i].X = x;
            windows[i].Y = y;
            x++;
            y++;
            if (x > ScreenWidth) x = 0;
            if (y >= ScreenHeight) y = DesktopTop;
        }

        invalidated = true;
    }

    /// <summary>Closes every window (windows that cancel closing stay open).</summary>
    public void CloseAllWindows()
    {
        if (ActiveMenu != null) return;
        foreach (var w in windows.ToArray()) CloseWindow(w);
    }

    // ---- screen ----

    private void SetScreenSize(int columns, int rows)
    {
        ScreenWidth = Math.Max(1, columns);
        ScreenHeight = Math.Max(1, rows);
        surface = new CellSurface(ScreenWidth, ScreenHeight);
        ResizeDesktop();
        RecomputeMenuX();
    }

    private void ResizeDesktop()
    {
        desktop?.SetDimensions(0, DesktopTop, ScreenWidth, Math.Max(0, DesktopBottom - DesktopTop));
        invalidated = true;
    }

    private void OnScreenResize(int columns, int rows)
    {
        SetScreenSize(columns, rows);
        mouseX = mouseY = -1;
        foreach (var w in windows.ToArray()) w.OnScreenResized();
    }

    // ---- drawing ----

    private void DrawAndPresent()
    {
        invalidated = false;
        foreach (var w in windows.ToArray()) w.OnIdle();
        desktop?.OnIdle();
        var cursor = DrawAll();
        host?.Present(surface!, cursor);
    }

    /// <summary>Redraws the whole screen into <see cref="Screen"/> (Jexer <c>drawAll</c>) and returns the cursor.</summary>
    private CursorState DrawAll()
    {
        var s = surface!;
        s.Clear();
        var screen = new Rect(0, 0, ScreenWidth, ScreenHeight);
        desktop?.DrawTree(s, screen);
        void DrawWindow(Window window)
        {
            if (Translucence) Ui.Translucence.DrawWindow(s, window, screen);
            else window.DrawTree(s, screen);
        }

        for (int i = windows.Count - 1; i >= 0; i--)
        {
            if (windows[i].IsShown) DrawWindow(windows[i]);
        }

        var root = new DrawContext(s);
        bool menuIsActive = false;
        Window? topLevel = windows.Count > 0 ? windows[0] : null;
        if (!hideMenuBar) root.HLine(0, 0, ScreenWidth, ' ', Theme.GetColor("tmenu"));
        int x = 1;
        foreach (var menu in Menus)
        {
            CellStyle menuColor, mnemonicColor;
            if (menu.IsOpen && !menu.IsContext)
            {
                menuColor = Theme.GetColor("tmenu.highlighted");
                mnemonicColor = Theme.GetColor("tmenu.mnemonic.highlighted");
            }
            else
            {
                menuColor = Theme.GetColor("tmenu");
                mnemonicColor = Theme.GetColor("tmenu.mnemonic");
            }

            if (menu.IsOpen)
            {
                menuIsActive = true;
                topLevel = menu;
            }

            int titleWidth = TextMetrics.Width(menu.Title);
            if (!hideMenuBar)
            {
                root.HLine(x, 0, titleWidth + 2, ' ', menuColor);
                root.PutString(x + 1, 0, menu.Title, menuColor);
                if (menu.Mnemonic.ScreenShortcutIndex >= 0)
                {
                    root.PutChar(x + 1 + menu.Mnemonic.ScreenShortcutIndex, 0, menu.Mnemonic.Shortcut, mnemonicColor);
                }
            }

            if (menu.IsOpen) DrawWindow(menu);
            x += titleWidth + 2;
        }

        if (ActiveMenu is { IsContext: true } context)
        {
            menuIsActive = true;
            topLevel = context;
            DrawWindow(context);
        }

        foreach (var sub in subMenus) DrawWindow(sub);

        if (!hideStatusBar && DesktopBottom >= 0 && DesktopBottom < ScreenHeight)
        {
            StatusBar? statusBar = topLevel != null ? (topLevel.IsShown ? topLevel.StatusBar : null) : desktop?.StatusBar;
            var row = new DrawContext(s, 0, DesktopBottom, ScreenWidth, 1, new Rect(0, DesktopBottom, ScreenWidth, 1));
            if (statusBar != null) statusBar.Draw(row, (topLevel ?? (Window?)desktop)?.Theme ?? Theme);
            else StatusBar.DrawEmpty(row, Theme);
        }

        if (menuIsActive) return CursorState.Hidden;
        Window? front = null;
        foreach (var w in windows)
        {
            if (w.IsShown)
            {
                front = w;
                break;
            }
        }

        front ??= desktop;
        if (front == null) return CursorState.Hidden;
        var active = front.DeepestActiveChild;
        if (active.IsCursorShown)
        {
            int cy = active.CursorAbsoluteY;
            if (cy >= DesktopTop && cy <= DesktopBottom) return new CursorState(true, active.CursorAbsoluteX, cy);
        }

        return CursorState.Hidden;
    }

    /// <summary>Collection of windows (front to back). Adding shows the window; removing closes it.</summary>
    public sealed class WindowCollection : ICollection<Window>, IReadOnlyList<Window>
    {
        private readonly Application app;

        internal WindowCollection(Application app) => this.app = app;

        /// <inheritdoc/>
        public Window this[int index] => app.windows[index];

        /// <inheritdoc cref="ICollection{T}.Count"/>
        public int Count => app.windows.Count;

        bool ICollection<Window>.IsReadOnly => false;

        /// <summary>Adds and shows a window (Jexer <c>addWindowToApplication</c>).</summary>
        public void Add(Window item) => app.AddWindow(item);

        /// <summary>Closes all windows.</summary>
        public void Clear() => app.CloseAllWindows();

        /// <inheritdoc/>
        public bool Contains(Window item) => app.windows.Contains(item);

        /// <inheritdoc/>
        public void CopyTo(Window[] array, int arrayIndex) => app.windows.CopyTo(array, arrayIndex);

        /// <summary>Closes the window; returns false if it was not present or closing was cancelled.</summary>
        public bool Remove(Window item) => app.CloseWindow(item);

        /// <inheritdoc/>
        public IEnumerator<Window> GetEnumerator() => app.windows.ToList().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
