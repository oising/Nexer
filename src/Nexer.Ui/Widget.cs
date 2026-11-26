// Ported from Jexer TWidget.java (MIT, © Autumn Lamonte).
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Nexer.Input;
using Nexer.Ui.Layout;
using Nexer.Ui.Markup;

namespace Nexer.Ui;

/// <summary>
/// Base class of every visual element (Jexer's <c>TWidget</c>). Coordinates are relative to the parent's
/// client origin; windows and bordered panels offset their children by one cell (the border).
/// </summary>
[UsableDuringInitialization]
public class Widget : ISupportInitialize, IResourceProvider, INotifyPropertyChanged
{
    private readonly List<Widget> children = [];
    private object? dataContext;
    private int width, height;
    private bool enabled = true;
    private LayoutManager? layout;
    private int initDepth;
    private bool sizePending;
    private ResourceDictionary? resources;

    /// <summary>Creates a widget.</summary>
    public Widget()
    {
        Children = RedirectedChildren ?? new WidgetCollection(this, children);
    }

    /// <summary>When non-null (set from a field initializer), <see cref="Children"/> forwards to another widget's collection.</summary>
    private protected virtual WidgetCollection? RedirectedChildren => null;

    /// <summary>Child widgets. Adding applies Jexer's addChild rules (activation, tab order, layout).</summary>
    [Content]
    public WidgetCollection Children { get; }

    /// <summary>The parent widget, or null for windows and detached widgets.</summary>
    public Widget? Parent { get; private set; }

    /// <summary>The nearest window ancestor (or this widget if it is a window).</summary>
    public Window? Window
    {
        get
        {
            for (Widget? w = this; w != null; w = w.Parent)
            {
                if (w is Window window) return window;
            }

            return null;
        }
    }

    /// <summary>The application owning this widget's window, if attached.</summary>
    public virtual Application? Application => Window?.Application;

    // ---- data binding (contract: see docs\binding.md) ----

    /// <summary>Raised when a bindable property changes (including changes made by user input).</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when the effective <see cref="DataContext"/> of this widget changes (local set, ancestor change, re-parenting).</summary>
    public event EventHandler? DataContextChanged;

    /// <summary>Raised after this widget's window is added to an application (see <see cref="IsAttachedToTree"/>).</summary>
    public event EventHandler? AttachedToTree;

    /// <summary>Raised after this widget's window is removed from its application, or the widget is removed from an attached tree.</summary>
    public event EventHandler? DetachedFromTree;

    /// <summary>
    /// The data context used by <c>{Binding}</c>: the local value if set, otherwise the parent's (a window inherits
    /// <see cref="Nexer.Ui.Application.DataContext"/>). Setting <see langword="null"/> reverts to the inherited value.
    /// </summary>
    public object? DataContext
    {
        get => dataContext ?? Parent?.DataContext ?? LogicalParent?.DataContext ?? InheritedRootDataContext;
        set
        {
            if (ReferenceEquals(dataContext, value)) return;
            var old = DataContext;
            dataContext = value;
            if (!ReferenceEquals(old, DataContext))
            {
                OnDataContextChanged();
            }
        }
    }

    /// <summary>Whether this widget is in a window that belongs to an application (bindings subscribe only while attached).</summary>
    public bool IsAttachedToTree { get; private set; }

    /// <summary>The data context a root widget (no parent) inherits: <see cref="Nexer.Ui.Application.DataContext"/> for windows.</summary>
    private protected virtual object? InheritedRootDataContext => null;

    /// <summary>Whether <see cref="DataContext"/> has a local (non-inherited) value.</summary>
    internal bool HasLocalDataContext => dataContext is not null;

    /// <summary>Raises <see cref="PropertyChanged"/>.</summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Sets <paramref name="field"/> and raises <see cref="PropertyChanged"/> when the value changes.</summary>
    /// <returns><see langword="true"/> if the value changed.</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>Raises <see cref="DataContextChanged"/> on this widget and every descendant that inherits its data context.</summary>
    internal void OnDataContextChanged()
    {
        DataContextChanged?.Invoke(this, EventArgs.Empty);
        foreach (var child in children)
        {
            if (child.dataContext is null) child.OnDataContextChanged();
        }

        foreach (var logical in LogicalChildren)
        {
            if (logical.dataContext is null) logical.OnDataContextChanged();
        }
    }

    /// <summary>
    /// Sets <see cref="IsAttachedToTree"/> for this subtree and raises <see cref="AttachedToTree"/>/<see cref="DetachedFromTree"/>:
    /// parents before children when attaching, children before parents when detaching. Idempotent.
    /// </summary>
    internal void SetAttachedToTree(bool attached)
    {
        if (IsAttachedToTree == attached) return;
        if (attached)
        {
            IsAttachedToTree = true;
            SubscribeCommand();
            OnAttachedToTree();
            foreach (var child in children.ToArray()) child.SetAttachedToTree(true);
            foreach (var logical in LogicalChildren) logical.SetAttachedToTree(true);
        }
        else
        {
            foreach (var logical in LogicalChildren) logical.SetAttachedToTree(false);
            foreach (var child in children.ToArray()) child.SetAttachedToTree(false);
            IsAttachedToTree = false;
            UnsubscribeCommand();
            OnDetachedFromTree();
        }
    }

    /// <summary>
    /// Widgets that are not in <see cref="Children"/> but share this widget's lifecycle and data context (e.g. the
    /// drop-down list of a <c>ComboBox</c> or the menu of a <c>SubMenu</c>).
    /// </summary>
    private protected virtual IEnumerable<Widget> LogicalChildren => [];

    /// <summary>The widget whose data context a parentless logical child (see <see cref="LogicalChildren"/>) inherits.</summary>
    internal Widget? LogicalParent { get; set; }

    /// <summary>Called after this widget becomes attached (before its children); raises <see cref="AttachedToTree"/>.</summary>
    protected virtual void OnAttachedToTree() => AttachedToTree?.Invoke(this, EventArgs.Empty);

    /// <summary>Called after this widget is detached (after its children); raises <see cref="DetachedFromTree"/>.</summary>
    protected virtual void OnDetachedFromTree() => DetachedFromTree?.Invoke(this, EventArgs.Empty);

    // ---- ICommand support (Button, CheckBox, Label, MenuItem) ----

    private System.Windows.Input.ICommand? command;
    private object? commandParameter;
    private bool commandSubscribed;
    private bool commandCanExecute = true;

    /// <summary>The command source state backing a control's <c>Command</c> property.</summary>
    private protected System.Windows.Input.ICommand? CommandCore
    {
        get => command;
        set
        {
            if (ReferenceEquals(command, value)) return;
            UnsubscribeCommand();
            command = value;
            if (IsAttachedToTree) SubscribeCommand();
            OnPropertyChanged("Command");
            RequeryCanExecute();
        }
    }

    /// <summary>The parameter backing a control's <c>CommandParameter</c> property.</summary>
    private protected object? CommandParameterCore
    {
        get => commandParameter;
        set
        {
            if (Equals(commandParameter, value)) return;
            commandParameter = value;
            OnPropertyChanged("CommandParameter");
            RequeryCanExecute();
        }
    }

    /// <summary>The command's last <c>CanExecute</c> result (true without a command).</summary>
    private protected bool CommandCanExecute => commandCanExecute;

    private void SubscribeCommand()
    {
        if (command is null || commandSubscribed) return;
        command.CanExecuteChanged += OnCanExecuteChanged;
        commandSubscribed = true;
        RequeryCanExecute();
    }

    private void UnsubscribeCommand()
    {
        if (!commandSubscribed) return;
        command?.CanExecuteChanged -= OnCanExecuteChanged;
        commandSubscribed = false;
    }

    private void OnCanExecuteChanged(object? sender, EventArgs e)
    {
        if (Application is { } app && !app.CheckAccess())
        {
            app.Post(RequeryCanExecute);
            return;
        }

        RequeryCanExecute();
    }

    private void RequeryCanExecute()
    {
        bool can = command is null || command.CanExecute(commandParameter);
        if (can == commandCanExecute) return;
        bool wasEffective = IsEffectivelyEnabled;
        commandCanExecute = can;
        OnEffectiveEnabledChanged(wasEffective);
        Invalidate();
    }

    /// <summary>
    /// Executes the command: a named <see cref="Ui.Command"/> goes through <see cref="Application.ExecuteCommand"/>
    /// (<paramref name="post"/>: <see cref="Application.PostCommand"/>); any other <see cref="System.Windows.Input.ICommand"/>
    /// is executed with the command parameter when <c>CanExecute</c> allows.
    /// </summary>
    private protected void ExecuteCommandCore(Application? app = null, bool post = false)
    {
        switch (command)
        {
            case null:
                return;
            case Command named:
                app ??= Application ?? Application.Current;
                if (post) app?.PostCommand(named);
                else app?.ExecuteCommand(named);
                return;
            default:
                if (command.CanExecute(commandParameter)) command.Execute(commandParameter);
                return;
        }
    }

    /// <summary>Optional name (populated into XAML name scopes by the compiler).</summary>
    public string? Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    private string? name;

    /// <summary>Arbitrary user data.</summary>
    public object? Tag
    {
        get => tag;
        set => SetProperty(ref tag, value);
    }

    private object? tag;

    /// <summary>Column relative to the parent's client origin (absolute for windows).</summary>
    public int X
    {
        get => x;
        set
        {
            if (SetProperty(ref x, value)) OnPropertyChanged(nameof(Bounds));
        }
    }

    private int x;

    /// <summary>Row relative to the parent's client origin (absolute for windows).</summary>
    public int Y
    {
        get => y;
        set
        {
            if (SetProperty(ref y, value)) OnPropertyChanged(nameof(Bounds));
        }
    }

    private int y;

    /// <summary>Width in cells.</summary>
    public int Width
    {
        get => width;
        set
        {
            if (width == value) return;
            width = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Bounds));
            NotifySizeChanged();
        }
    }

    /// <summary>Height in cells.</summary>
    public int Height
    {
        get => height;
        set
        {
            if (height == value) return;
            height = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Bounds));
            NotifySizeChanged();
        }
    }

    /// <summary>Position and size. XAML text form <c>"x,y,width,height"</c>.</summary>
    public Rect Bounds
    {
        get => new(X, Y, width, height);
        set => SetDimensions(value.X, value.Y, value.Width, value.Height);
    }

    /// <summary>Sets position and size at once (one resize notification).</summary>
    public void SetDimensions(int x, int y, int newWidth, int newHeight)
    {
        X = x;
        Y = y;
        bool widthChanged = newWidth != width, heightChanged = newHeight != height;
        width = newWidth;
        height = newHeight;
        if (widthChanged) OnPropertyChanged(nameof(Width));
        if (heightChanged) OnPropertyChanged(nameof(Height));
        if (widthChanged || heightChanged)
        {
            OnPropertyChanged(nameof(Bounds));
            NotifySizeChanged();
        }
    }

    /// <summary>
    /// Whether the widget accepts input. Disabling moves activation to the first enabled sibling. See also
    /// <see cref="IsEffectivelyEnabled"/>, which also accounts for the command's <c>CanExecute</c>.
    /// </summary>
    public bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value) return;
            bool wasEffective = IsEffectivelyEnabled;
            enabled = value;
            OnPropertyChanged();
            OnEffectiveEnabledChanged(wasEffective);
        }
    }

    /// <summary>
    /// <see cref="Enabled"/> combined with the <c>CanExecute</c> state of the widget's command (if any): whether the
    /// widget currently accepts input.
    /// </summary>
    public bool IsEffectivelyEnabled => enabled && commandCanExecute;

    private void OnEffectiveEnabledChanged(bool wasEffective)
    {
        bool now = IsEffectivelyEnabled;
        if (now == wasEffective) return;
        if (!now)
        {
            IsActive = false;
            if (Parent != null && Parent.ActiveChild == this)
            {
                Parent.ActiveChild = null;
                foreach (var w in Parent.children)
                {
                    if (w.IsEffectivelyEnabled && w.CanBeActivated)
                    {
                        Parent.Activate(w);
                        break;
                    }
                }
            }
        }
        else if (Parent != null && Parent.ActiveChild == null && CanBeActivated)
        {
            Parent.Activate(this);
        }

        OnPropertyChanged(nameof(IsEffectivelyEnabled));
    }

    /// <summary>Whether the widget is drawn.</summary>
    public bool Visible
    {
        get => visible;
        set
        {
            SetProperty(ref visible, value);
        }
    }

    private bool visible = true;

    /// <summary>Whether Tab/arrow focus traversal can land on this widget.</summary>
    public bool TabStop
    {
        get => tabStop;
        set => SetProperty(ref tabStop, value);
    }

    private bool tabStop = true;

    /// <summary>True when this widget is its parent's active child.</summary>
    public bool IsActive
    {
        get => isActive;
        internal set
        {
            if (!SetProperty(ref isActive, value) || value) return;

            // This widget and the chain of active descendants below it lose the keyboard focus.
            for (Widget? w = this; w != null; w = w.ActiveChildOrNull) w.OnLostFocus();
        }
    }

    private bool isActive;

    private Widget? ActiveChildOrNull
    {
        get
        {
            foreach (var c in children)
            {
                if (c.IsActive) return c;
            }

            return null;
        }
    }

    /// <summary>
    /// Called when this widget stops being on the focus path: it, or one of its ancestors, is no longer its parent's
    /// active child (another widget or window was activated).
    /// </summary>
    private protected virtual void OnLostFocus()
    {
    }

    /// <summary>True when this widget and all its ancestors are active.</summary>
    public bool IsAbsoluteActive => IsActive && (Parent == null || Parent.IsAbsoluteActive);

    /// <summary>The active child, or null.</summary>
    public Widget? ActiveChild { get; private set; }

    /// <summary>Position in the parent's tab order.</summary>
    public int TabOrder { get; internal set; }

    /// <summary>Whether the text cursor should be shown at (<see cref="CursorX"/>, <see cref="CursorY"/>) when focused.</summary>
    public bool CursorVisible { get; set; }

    /// <summary>Cursor column, local.</summary>
    public int CursorX { get; set; }

    /// <summary>Cursor row, local.</summary>
    public int CursorY { get; set; }

    /// <summary>When true, keystrokes are sent to all children rather than only the active one.</summary>
    public bool EchoKeystrokes
    {
        get => echoKeystrokes;
        set => SetProperty(ref echoKeystrokes, value);
    }

    private bool echoKeystrokes;

    /// <summary>Layout manager for the children (e.g. <c>StretchLayout</c>, <c>BoxLayout</c>, <c>AnchorLayout</c>).</summary>
    public LayoutManager? Layout
    {
        get => layout;
        set
        {
            if (ReferenceEquals(layout, value)) return;
            layout?.Detach();
            layout = value;
            if (layout != null && !IsLayoutSuspended) layout.Attach(this, ClientWidth, ClientHeight);
            OnPropertyChanged();
        }
    }

    /// <summary>Resources available to lookups from this widget and its descendants.</summary>
    public ResourceDictionary Resources => resources ??= [];

    /// <summary>The theme used to draw this widget (window theme, then application theme, then defaults).</summary>
    public ColorTheme Theme => Window?.OwnTheme ?? Application?.Theme ?? ColorTheme.Default;

    /// <summary>Absolute screen column of the widget's origin.</summary>
    public int AbsoluteX => Parent == null ? X : Parent.AbsoluteX + Parent.ChildOffset + X;

    /// <summary>Absolute screen row of the widget's origin.</summary>
    public int AbsoluteY => Parent == null ? Y : Parent.AbsoluteY + Parent.ChildOffset + Y;

    /// <summary>Absolute column of the cursor.</summary>
    public int CursorAbsoluteX => AbsoluteX + CursorX;

    /// <summary>Absolute row of the cursor.</summary>
    public int CursorAbsoluteY => AbsoluteY + CursorY;

    /// <summary>Offset applied to children (1 for bordered containers).</summary>
    protected internal virtual int ChildOffset => 0;

    /// <summary>Width available to children (excluding the border).</summary>
    public int ClientWidth => Math.Max(0, width - 2 * ChildOffset);

    /// <summary>Height available to children (excluding the border).</summary>
    public int ClientHeight => Math.Max(0, height - 2 * ChildOffset);

    /// <summary>Whether the widget can become a parent's active child (scrollers cannot).</summary>
    protected internal virtual bool CanBeActivated => true;

    /// <summary>Leaf-style keyboard navigation (Tab/arrows switch widgets) even when the widget has children.</summary>
    protected internal virtual bool IsKeyboardLeaf => children.Count == 0;

    /// <summary>Whether Up/Down navigate between widgets when this is a keyboard leaf.</summary>
    protected internal virtual bool UpDownSwitchesWidget => true;

    /// <summary>Whether Left/Right navigate between widgets when this widget has no children.</summary>
    protected internal virtual bool LeftRightSwitchesWidget => true;

    /// <summary>True while this widget or an ancestor is between BeginInit and EndInit.</summary>
    public bool IsLayoutSuspended => initDepth > 0 || (Parent?.IsLayoutSuspended ?? false);

    /// <inheritdoc/>
    public void BeginInit() => initDepth++;

    /// <inheritdoc/>
    public void EndInit()
    {
        Debug.Assert(initDepth > 0, "EndInit without BeginInit");
        if (initDepth > 0) initDepth--;
        if (!IsLayoutSuspended) ResumeLayout();
    }

    private void ResumeLayout()
    {
        if (layout != null && !layout.IsAttached) layout.Attach(this, ClientWidth, ClientHeight);
        if (sizePending)
        {
            sizePending = false;
            OnSizeChanged();
        }

        foreach (var child in children.ToArray()) child.ResumeLayout();
        OnInitialized();
    }

    /// <summary>Called after the outermost <see cref="EndInit"/> (or when added outside initialisation).</summary>
    protected virtual void OnInitialized()
    {
    }

    private void NotifySizeChanged()
    {
        if (IsLayoutSuspended)
        {
            sizePending = true;
            return;
        }

        OnSizeChanged();
        Invalidate();
    }

    /// <summary>
    /// Called when the size changes (Jexer <c>onResize(WIDGET)</c>). The base implementation forwards the client
    /// size to the layout manager.
    /// </summary>
    protected internal virtual void OnSizeChanged() => LayoutOnResize();

    /// <summary>Forwards the current client size to the layout manager.</summary>
    private protected void LayoutOnResize() => layout?.OnResize(ClientWidth, ClientHeight);

    /// <summary>Looks up a resource on this widget, its ancestors, then the application.</summary>
    public bool TryGetResource(string key, out object? value)
    {
        for (Widget? w = this; w != null; w = w.Parent)
        {
            if (w.resources != null && w.resources.TryGetValue(key, out value)) return true;
        }

        if (Application is { } app) return app.TryGetResource(key, out value);
        value = null;
        return false;
    }

    /// <summary>Looks up a theme colour.</summary>
    protected Nexer.Core.CellStyle GetColor(string key) => Theme.GetColor(key);

    /// <summary>Requests a redraw on the next loop iteration (no-op when not attached to an application).</summary>
    protected void Invalidate() => Application?.Invalidate();

    /// <summary>Finds a descendant (or self) by <see cref="Name"/>.</summary>
    public Widget? FindByName(string name)
    {
        if (Name == name) return this;
        foreach (var c in children)
        {
            if (c.FindByName(name) is { } found) return found;
        }

        return null;
    }

    // ---- tree management (TWidget.addChild/remove) ----

    internal void AttachChild(Widget child, int index)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child is Window) throw new InvalidOperationException("Windows cannot be children; add them to Application.Windows.");
        if (child.Parent != null) throw new InvalidOperationException("The widget already has a parent.");
        for (Widget? p = this; p != null; p = p.Parent)
        {
            if (p == child) throw new InvalidOperationException("A widget cannot be its own ancestor.");
        }

        children.Insert(index, child);
        child.Parent = this;
        // Deviation from Jexer (which activates the last added child): the first tab stop keeps the focus so markup
        // order gives the natural initial focus.
        if (child.IsEffectivelyEnabled && child.CanBeActivated &&
            (ActiveChild == null || (!ActiveChild.TabStop && child.TabStop)))
        {
            foreach (var w in children) w.IsActive = false;
            child.IsActive = true;
            ActiveChild = child;
        }

        ResetTabOrder();
        if (layout != null && layout.IsAttached) layout.Add(child);
        if (!child.IsLayoutSuspended && child.sizePending)
        {
            child.ResumeLayout();
        }

        if (child.dataContext is null && DataContext is not null) child.OnDataContextChanged();
        child.SetAttachedToTree(IsAttachedToTree);
        OnChildAdded(child);
    }

    internal void DetachChild(Widget child)
    {
        int index = children.IndexOf(child);
        Debug.Assert(index >= 0, "Child not found");
        if (index < 0) return;
        child.SetAttachedToTree(false);
        children.RemoveAt(index);
        layout?.Remove(child);
        child.Parent = null;
        child.IsActive = false;
        if (ActiveChild == child)
        {
            ActiveChild = null;
            foreach (var w in children)
            {
                if (w.IsEffectivelyEnabled && w.CanBeActivated && w.Visible)
                {
                    Activate(w);
                    break;
                }
            }
        }

        ResetTabOrder();
        if (child.dataContext is null && DataContext is not null) child.OnDataContextChanged();
        OnChildRemoved(child);
    }

    /// <summary>Called after a child is added.</summary>
    protected virtual void OnChildAdded(Widget child)
    {
    }

    /// <summary>Called after a child is removed.</summary>
    protected virtual void OnChildRemoved(Widget child)
    {
    }

    /// <summary>Resets tab order to the child index.</summary>
    protected void ResetTabOrder()
    {
        for (int i = 0; i < children.Count; i++) children[i].TabOrder = i;
    }

    /// <summary>Makes <paramref name="child"/> the active child (scrollers are ignored).</summary>
    public void Activate(Widget child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!child.CanBeActivated || !child.IsEffectivelyEnabled) return;
        Debug.Assert(child.Parent == this, "Activate called with a non-child");
        foreach (var w in children)
        {
            if (w != child) w.IsActive = false;
        }

        child.IsActive = true;
        ActiveChild = child;
    }

    /// <summary>Activates the first enabled child whose tab order is at least <paramref name="tabOrder"/>.</summary>
    public void Activate(int tabOrder)
    {
        foreach (var w in children)
        {
            if (w.IsEffectivelyEnabled && w.CanBeActivated && w.TabOrder >= tabOrder)
            {
                Activate(w);
                return;
            }
        }
    }

    /// <summary>Makes this widget active in its parent (Jexer <c>activate()</c>).</summary>
    public void Activate()
    {
        if (IsEffectivelyEnabled) Parent?.Activate(this);
    }

    /// <summary>Activates this widget and every ancestor; also activates the containing window.</summary>
    public void ActivateAll()
    {
        Activate();
        Parent?.ActivateAll();
    }

    /// <summary>Gives keyboard focus to this widget: activates it up the tree and activates its window.</summary>
    public void Focus()
    {
        ActivateAll();
        if (Window is { } w && w.Application is { } app && !w.IsDesktop) app.ActivateWindow(w);
    }

    /// <summary>The deepest active descendant (Jexer <c>getActiveChild</c>).</summary>
    public Widget DeepestActiveChild
    {
        get
        {
            var w = this;
            while (w.ActiveChild is { } c && c.IsActive) w = c;
            return w;
        }
    }

    private bool IsTabCandidate(Widget w) => w.IsEffectivelyEnabled && w.Visible && w.TabStop && w.CanBeActivated;

    /// <summary>
    /// Moves activation to the next/previous tab stop, bubbling to the parent at either end
    /// (Jexer <c>switchWidget</c>, with disabled/non-tab-stop widgets skipped).
    /// </summary>
    public void SwitchWidget(bool forward)
    {
        if (children.Count == 0) return;
        int count = children.Count;
        int index = ActiveChild?.TabOrder ?? (forward ? -1 : count);
        for (int step = 0; step < count; step++)
        {
            index += forward ? 1 : -1;
            if (index < 0 || index >= count)
            {
                if (Parent != null)
                {
                    Parent.SwitchWidget(forward);
                    return;
                }

                index = forward ? 0 : count - 1;
            }

            var candidate = children[index];
            if (IsTabCandidate(candidate))
            {
                if (candidate.children.Count > 0 && candidate.IsKeyboardLeaf == false)
                {
                    candidate.ActivateFirstOrLast(forward);
                }

                Activate(candidate);
                return;
            }
        }
    }

    private void ActivateFirstOrLast(bool first)
    {
        for (int i = 0; i < children.Count; i++)
        {
            var w = children[first ? i : children.Count - 1 - i];
            if (IsTabCandidate(w))
            {
                Activate(w);
                return;
            }
        }
    }

    // ---- event dispatch ----

    /// <summary>True when the widget is enabled, visible and (absolute) <paramref name="mouse"/> is inside it.</summary>
    public bool MouseWouldHit(UiMouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        if (!IsEffectivelyEnabled || !Visible) return false;
        int ax = AbsoluteX, ay = AbsoluteY;
        return mouse.AbsoluteX >= ax && mouse.AbsoluteX < ax + width && mouse.AbsoluteY >= ay && mouse.AbsoluteY < ay + height;
    }

    /// <summary>Dispatches a key to this widget (ignored when disabled).</summary>
    public void HandleKey(KeyEvent key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (IsEffectivelyEnabled) OnKeypress(key);
    }

    /// <summary>Dispatches a mouse event, already re-based into local coordinates (ignored when disabled).</summary>
    public void HandleMouse(UiMouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        if (!IsEffectivelyEnabled) return;
        switch (mouse.Action)
        {
            case MouseAction.Down: OnMouseDown(mouse); break;
            case MouseAction.Up: OnMouseUp(mouse); break;
            case MouseAction.Motion: OnMouseMotion(mouse); break;
            case MouseAction.DoubleClick: OnMouseDoubleClick(mouse); break;
        }
    }

    /// <summary>Dispatches a command (ignored when disabled).</summary>
    public void HandleCommand(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (IsEffectivelyEnabled) OnCommand(command);
    }

    /// <summary>Handles a key (Jexer <c>TWidget.onKeypress</c>: navigation, mnemonics, then the active child).</summary>
    protected internal virtual void OnKeypress(KeyEvent key)
    {
        var parent = Parent;
        bool plain = key.Modifiers == KeyModifiers.None;
        if (IsKeyboardLeaf && parent != null)
        {
            if ((key.Key == Key.Tab && plain) || (key.Key == Key.Down && plain && UpDownSwitchesWidget))
            {
                parent.SwitchWidget(true);
                return;
            }

            if (key.Key == Key.BackTab || (key.Key == Key.Tab && key.Modifiers == KeyModifiers.Shift) ||
                (key.Key == Key.Up && plain && UpDownSwitchesWidget))
            {
                parent.SwitchWidget(false);
                return;
            }
        }

        if (children.Count == 0 && parent != null && LeftRightSwitchesWidget && plain)
        {
            if (key.Key == Key.Right)
            {
                parent.SwitchWidget(true);
                return;
            }

            if (key.Key == Key.Left)
            {
                parent.SwitchWidget(false);
                return;
            }
        }

        if (key.Key == Key.Character && (key.Modifiers & KeyModifiers.Alt) != 0 && (key.Modifiers & KeyModifiers.Ctrl) == 0)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                foreach (var w in children.ToArray())
                {
                    if (w.MnemonicPass == pass && w.TryInvokeMnemonic(this, key.Char)) return;
                }
            }
        }

        if (EchoKeystrokes)
        {
            foreach (var w in children.ToArray()) w.OnKeypress(key);
            return;
        }

        foreach (var w in children)
        {
            if (w.IsActive)
            {
                w.OnKeypress(key);
                return;
            }
        }
    }

    /// <summary>Order in which mnemonic scanning considers this widget (0 buttons, 1 labels, 2 radios, 3 checkboxes; -1 none).</summary>
    internal virtual int MnemonicPass => -1;

    /// <summary>Invokes this widget's mnemonic if it matches <paramref name="c"/>.</summary>
    internal virtual bool TryInvokeMnemonic(Widget parent, Rune c) => false;

    /// <summary>Mouse press (Jexer: active child first if hit, else topmost hit child, which is activated).</summary>
    protected internal virtual void OnMouseDown(UiMouseEvent mouse) => RouteMouse(mouse, static (w, m) => w.OnMouseDown(m));

    /// <summary>Mouse release.</summary>
    protected internal virtual void OnMouseUp(UiMouseEvent mouse) => RouteMouse(mouse, static (w, m) => w.OnMouseUp(m));

    /// <summary>Double click.</summary>
    protected internal virtual void OnMouseDoubleClick(UiMouseEvent mouse) => RouteMouse(mouse, static (w, m) => w.OnMouseDoubleClick(m));

    /// <summary>Mouse motion: delivered to every child.</summary>
    protected internal virtual void OnMouseMotion(UiMouseEvent mouse)
    {
        foreach (var w in children.ToArray()) w.OnMouseMotion(mouse.RelativeTo(w));
    }

    private void RouteMouse(UiMouseEvent mouse, Action<Widget, UiMouseEvent> handler)
    {
        if (ActiveChild is { } active && active.MouseWouldHit(mouse))
        {
            handler(active, mouse.RelativeTo(active));
            return;
        }

        for (int i = children.Count - 1; i >= 0; i--)
        {
            var w = children[i];
            if (w.MouseWouldHit(mouse))
            {
                Activate(w);
                handler(w, mouse.RelativeTo(w));
                return;
            }
        }
    }

    /// <summary>Dispatches pasted text (bracketed paste) to this widget (ignored when disabled).</summary>
    public void HandlePaste(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (IsEffectivelyEnabled) OnPaste(text);
    }

    /// <summary>Handles pasted text; the base forwards to the active child.</summary>
    protected internal virtual void OnPaste(string text)
    {
        ActiveChild?.OnPaste(text);
    }

    /// <summary>Handles a command; the base forwards to the active child.</summary>
    protected internal virtual void OnCommand(Command command)
    {
        ActiveChild?.OnCommand(command);
    }

    /// <summary>Called on every idle pass; the base forwards to the children.</summary>
    protected internal virtual void OnIdle()
    {
        foreach (var w in children.ToArray()) w.OnIdle();
    }

    // ---- drawing ----

    /// <summary>Draws this widget in local coordinates. Children are drawn afterwards by the framework.</summary>
    protected internal virtual void Draw(DrawContext context)
    {
    }

    /// <summary>Clip rectangle used for this widget (Jexer drawChildren window-edge rules).</summary>
    internal Rect ComputeClip(Rect parentClip)
    {
        int ax = AbsoluteX, ay = AbsoluteY;
        var clip = parentClip.Intersect(new Rect(ax, ay, width, height));
        if (this is Window) return clip;
        var window = Window;
        if (window == null || window.IsDesktop) return clip;
        int right = window.AbsoluteX + window.Width - (ClipsToBorderRight ? 1 : 0);
        int bottom = window.AbsoluteY + window.Height - (ClipsToBorderBottom ? 1 : 0);
        return clip.Intersect(new Rect(window.AbsoluteX, window.AbsoluteY, right - window.AbsoluteX, bottom - window.AbsoluteY));
    }

    /// <summary>False for vertical scrollers, which may draw over the window's right border.</summary>
    internal virtual bool ClipsToBorderRight => true;

    /// <summary>False for horizontal scrollers, which may draw over the window's bottom border.</summary>
    internal virtual bool ClipsToBorderBottom => true;

    internal void DrawTree(Nexer.Core.CellSurface surface, Rect parentClip)
    {
        if (!Visible) return;
        var clip = ComputeClip(parentClip);
        var context = new DrawContext(surface, AbsoluteX, AbsoluteY, width, height, clip);
        Draw(context);
        foreach (var w in children.ToArray())
        {
            if (w.Visible && w != ActiveChild) w.DrawTree(surface, ChildClip(clip));
        }

        if (ActiveChild is { Visible: true } active && active.Parent == this) active.DrawTree(surface, ChildClip(clip));
        AfterDrawChildren(context);
    }

    /// <summary>Clip passed to children. Windows pass the full window rect (children apply the border rule).</summary>
    private protected virtual Rect ChildClip(Rect myClip) => myClip;

    /// <summary>Called after the children have been drawn.</summary>
    protected internal virtual void AfterDrawChildren(DrawContext context)
    {
    }

    /// <summary>True when the cursor should be shown (Jexer <c>isCursorVisible</c>).</summary>
    public bool IsCursorShown
    {
        get
        {
            if (CursorX < 0 || CursorY < 0 || CursorX >= width || CursorY >= height) return false;
            var window = Window;
            if (window == null) return false;
            if (window.IsDesktop) return CursorVisible;
            int cx = CursorAbsoluteX, cy = CursorAbsoluteY;
            if (cx >= window.AbsoluteX + window.Width - 1 || cx < 0 || cy >= window.AbsoluteY + window.Height - 1 || cy < 0)
            {
                return false;
            }

            return IsActive && CursorVisible;
        }
    }

    internal List<Widget> ChildList => children;
}

