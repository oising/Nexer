// Ported from Jexer TComboBox.java (MIT, © Autumn Lamonte).
using System.Collections;
using Nexer.Input;
using Nexer.Ui.Data;
using Nexer.Ui.Markup;

namespace Nexer.Ui.Controls;

/// <summary>
/// A one-line editField with a drop-down <see cref="List"/> of values (Jexer <c>TComboBox</c>). Click the arrow or press
/// Alt+Down to open the list; Enter or a double click picks a value; Esc or Tab closes it. With
/// <see cref="LimitToListValue"/> (the default) the text can only come from the list.
/// </summary>
public class ComboBox : Widget
{
    private readonly Field editField;
    private readonly List list;
    private bool limitToListValue = true;
    private int maxValuesHeight = 3;

    /// <summary>Creates an empty combo box.</summary>
    public ComboBox()
    {
        editField = new Field { Enabled = false };
        list = new List { Y = 1, Height = 3, Enabled = false, Visible = false };
        editField.Submitted += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        list.ItemActivated += (_, _) =>
        {
            editField.Text = list.SelectedText ?? "";
            HideList();
            Changed?.Invoke(this, EventArgs.Empty);
        };
        list.ItemsChanged += (_, _) => OnItemsChanged();
        editField.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Field.Text)) OnPropertyChanged(nameof(Text));
        };
        list.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(List.SelectedIndex) or nameof(List.SelectedItem)) OnPropertyChanged(e.PropertyName);
            else if (e.PropertyName == nameof(Visible)) OnPropertyChanged(nameof(IsListOpen));
        };
        Children.Add(editField);
        Children.Add(list);
        Height = 1;
    }

    /// <summary>The values offered in the drop-down list (content property).</summary>
    [Content]
    public ListItemCollection Items => list.Items;

    /// <summary>
    /// The source of the drop-down values (any objects, shown via <see cref="DisplayMemberBinding"/> or
    /// <c>ToString()</c>); mutually exclusive with <see cref="Items"/> (see <see cref="List.ItemsSource"/>).
    /// </summary>
    public IEnumerable? ItemsSource
    {
        get => list.ItemsSource;
        set
        {
            if (ReferenceEquals(list.ItemsSource, value)) return;
            list.ItemsSource = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The binding evaluated against each <see cref="ItemsSource"/> item to get its display text.</summary>
    public IBinding? DisplayMemberBinding
    {
        get => list.DisplayMemberBinding;
        set
        {
            if (ReferenceEquals(list.DisplayMemberBinding, value)) return;
            list.DisplayMemberBinding = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The selected list value (the source item when <see cref="ItemsSource"/> is used), or null. Setting it copies its
    /// display text into the text. Binds two-way by default.
    /// </summary>
    [BindsTwoWayByDefault]
    public object? SelectedItem
    {
        get => list.SelectedItem;
        set
        {
            list.SelectedItem = value;
            if (list.Items.Count > 0 || value == null) editField.Text = list.SelectedText ?? "";
        }
    }

    /// <summary>
    /// The current text (typed when <see cref="LimitToListValue"/> is false). Setting it selects the matching list item
    /// (case-sensitive), if any. Binds two-way by default.
    /// </summary>
    [BindsTwoWayByDefault]
    public string Text
    {
        get => editField.Text;
        set => SetText(value, true);
    }

    /// <summary>Index of the selected list value, or -1. Setting it copies that value into the text. Binds two-way by default.</summary>
    [BindsTwoWayByDefault]
    public int SelectedIndex
    {
        get => list.SelectedIndex;
        set
        {
            list.SelectedIndex = value;
            if (list.Items.Count > 0 || value < 0) editField.Text = list.SelectedText ?? "";
        }
    }

    /// <summary>When true (default) the text editField is read-only and values come from the list.</summary>
    public bool LimitToListValue
    {
        get => limitToListValue;
        set
        {
            if (limitToListValue == value) return;
            limitToListValue = value;
            editField.Enabled = !value;
            if (!value) Activate(editField);
            OnPropertyChanged();
        }
    }

    /// <summary>Maximum height of the drop-down list including its scroll bar row (default 3, minimum 3).</summary>
    public int MaxValuesHeight
    {
        get => maxValuesHeight;
        set
        {
            if (maxValuesHeight == value) return;
            maxValuesHeight = value;
            SizeList();
            OnPropertyChanged();
        }
    }

    /// <summary>True while the drop-down list is shown.</summary>
    public bool IsListOpen => list.Visible;

    /// <summary>Raised when a value is picked from the list or Enter is pressed in the editField (Jexer update action).</summary>
    public event EventHandler? Changed;

    /// <inheritdoc/>
    protected internal override bool IsKeyboardLeaf => true;

    /// <inheritdoc/>
    protected internal override bool UpDownSwitchesWidget => false;

    /// <summary>Sets the text and selects the matching item (Jexer <c>setText</c>).</summary>
    public void SetText(string? text, bool caseSensitive)
    {
        text ??= "";
        editField.Text = text;
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (string.Equals(list.Items[i], text, comparison))
            {
                list.SelectedIndex = i;
                return;
            }
        }

        list.SelectedIndex = -1;
    }

    private void OnItemsChanged()
    {
        SizeList();
        if (editField.Text.Length > 0) SetText(editField.Text, true);
        else if (list.SelectedIndex >= 0) editField.Text = list.SelectedText ?? "";
    }

    private void SizeList()
    {
        list.Height = Math.Max(3, Math.Min(list.Items.Count + 1, maxValuesHeight));
        if (list.Visible) Height = list.Height + 1;
    }

    /// <summary>Opens the drop-down list.</summary>
    public void ShowList()
    {
        list.Enabled = true;
        list.Visible = true;
        Height = list.Height + 1;
        Activate(list);
    }

    /// <summary>Closes the drop-down list.</summary>
    public void HideList()
    {
        list.Enabled = false;
        list.Visible = false;
        Height = 1;
        if (!limitToListValue) Activate(editField);
    }

    /// <inheritdoc/>
    protected internal override void OnSizeChanged()
    {
        editField.Width = Math.Max(0, Width - 3);
        list.Width = Width;
        base.OnSizeChanged();
    }

    private bool MouseOnArrow(UiMouseEvent mouse) => mouse.Y == 0 && mouse.X >= Width - 3 && mouse.X <= Width - 1;

    /// <inheritdoc/>
    protected internal override void OnMouseDown(UiMouseEvent mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        if (MouseOnArrow(mouse) && mouse.IsLeft && !mouse.IsWheelUp && !mouse.IsWheelDown)
        {
            if (list.IsActive && list.Visible) HideList();
            else ShowList();
        }

        base.OnMouseDown(mouse);
    }

    /// <inheritdoc/>
    protected internal override void OnKeypress(KeyEvent key)
    {
        ArgumentNullException.ThrowIfNull(key);
        bool listActive = list.IsActive && list.Visible;
        if (key.Key == Key.Escape && key.Modifiers == KeyModifiers.None && listActive)
        {
            HideList();
            return;
        }

        if (key.Key == Key.Down && key.Modifiers == KeyModifiers.Alt)
        {
            ShowList();
            return;
        }

        if ((key.Key == Key.Tab || key.Key == Key.BackTab) && listActive)
        {
            HideList();
            return;
        }

        base.OnKeypress(key);
    }

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        bool active = IsAbsoluteActive;
        if (!active && list.IsActive && list.Visible) HideList();
        var color = GetColor(Validation.GetHasErrors(this) ? "tcombobox.error" : active ? "tcombobox.active" : "tcombobox.inactive");
        context.PutChar(Width - 3, 0, GraphicsChars.DownArrowLeft, color);
        context.PutChar(Width - 2, 0, GraphicsChars.DownArrow, color);
        context.PutChar(Width - 1, 0, GraphicsChars.DownArrowRight, color);
    }
}
