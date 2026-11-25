// Ported from Jexer TComboBox.java (MIT, © Autumn Lamonte).
using Nexer.Input;
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
            editField.Text = list.SelectedItem ?? "";
            HideList();
            Changed?.Invoke(this, EventArgs.Empty);
        };
        list.ItemsChanged += (_, _) => OnItemsChanged();
        Children.Add(editField);
        Children.Add(list);
        Height = 1;
    }

    /// <summary>The values offered in the drop-down list (content property).</summary>
    [Content]
    public ListItemCollection Items => list.Items;

    /// <summary>The current text. Setting it selects the matching list item (case-sensitive), if any.</summary>
    public string Text
    {
        get => editField.Text;
        set => SetText(value, true);
    }

    /// <summary>Index of the selected list value, or -1. Setting it copies that value into the text.</summary>
    public int SelectedIndex
    {
        get => list.SelectedIndex;
        set
        {
            list.SelectedIndex = value;
            if (list.Items.Count > 0 || value < 0) editField.Text = list.SelectedItem ?? "";
        }
    }

    /// <summary>When true (default) the text editField is read-only and values come from the list.</summary>
    public bool LimitToListValue
    {
        get => limitToListValue;
        set
        {
            limitToListValue = value;
            editField.Enabled = !value;
            if (!value) Activate(editField);
        }
    }

    /// <summary>Maximum height of the drop-down list including its scroll bar row (default 3, minimum 3).</summary>
    public int MaxValuesHeight
    {
        get => maxValuesHeight;
        set
        {
            maxValuesHeight = value;
            SizeList();
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
        else if (list.SelectedIndex >= 0) editField.Text = list.SelectedItem ?? "";
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
        var color = GetColor(active ? "tcombobox.active" : "tcombobox.inactive");
        context.PutChar(Width - 3, 0, GraphicsChars.DownArrowLeft, color);
        context.PutChar(Width - 2, 0, GraphicsChars.DownArrow, color);
        context.PutChar(Width - 1, 0, GraphicsChars.DownArrowRight, color);
    }
}
