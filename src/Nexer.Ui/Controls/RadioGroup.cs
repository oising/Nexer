// Ported from Jexer TRadioGroup.java and TRadioButton.java (MIT, © Autumn Lamonte).
using System.Text;
using Nexer.Input;

namespace Nexer.Ui.Controls;

/// <summary>
/// A titled box of <see cref="RadioButton"/>s (Jexer <c>TRadioGroup</c>). Add buttons as children (XAML content); each
/// is placed on its own row and gets the 1-based <see cref="RadioButton.Id"/> of its position. The group grows to fit.
/// </summary>
public class RadioGroup : Widget
{
    private string label = "";
    private bool requiresSelection;
    private bool matchWindowBackground = true;

    /// <summary>Creates a radio group.</summary>
    public RadioGroup()
    {
        Height = 2;
        Width = 4;
    }

    /// <summary>The caption drawn over the top border. Widens the group to the caption width + 4 if needed.</summary>
    public string Text
    {
        get => label;
        set
        {
            label = value ?? "";
            Width = Math.Max(Width, TextMetrics.Width(label) + 4);
        }
    }

    /// <summary>The 1-based id of the selected button, or 0 for none.</summary>
    public int Selected
    {
        get => SelectedButton?.Id ?? 0;
        set => Select(value);
    }

    /// <summary>The selected button, or null.</summary>
    public RadioButton? SelectedButton { get; private set; }

    /// <summary>The buttons, in id order.</summary>
    public IEnumerable<RadioButton> Buttons => Children.OfType<RadioButton>();

    /// <summary>When true, a selection is always kept (Esc does not clear it; the first button is selected).</summary>
    public bool RequiresSelection
    {
        get => requiresSelection;
        set
        {
            requiresSelection = value;
            if (value && SelectedButton == null && Buttons.Any()) Select(1);
        }
    }

    /// <summary>When true (default) the group and its buttons keep the background already drawn beneath them.</summary>
    public bool MatchWindowBackground
    {
        get => matchWindowBackground;
        set
        {
            matchWindowBackground = value;
            foreach (var b in Buttons) b.MatchWindowBackground = value;
        }
    }

    /// <summary>Raised when the selection changes.</summary>
    public event EventHandler? SelectionChanged;

    internal override int MnemonicPass => 2;

    internal override bool TryInvokeMnemonic(Widget parent, Rune c)
    {
        if (!Enabled || !Visible) return false;
        foreach (var button in Buttons)
        {
            if (button.Enabled && button.Mnemonic.Matches(c))
            {
                parent.Activate(this);
                Activate(button);
                button.HandleKey(new KeyEvent(Key.Character, new Rune(' ')));
                return true;
            }
        }

        return false;
    }

    internal void Select(int id)
    {
        var buttons = Buttons.ToList();
        if (id < 0 || id > buttons.Count) return;
        var previous = SelectedButton;
        foreach (var b in buttons) b.IsSelectedField = false;
        SelectedButton = id == 0 ? null : buttons[id - 1];
        if (SelectedButton != null) SelectedButton.IsSelectedField = true;
        if (previous != SelectedButton)
        {
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc/>
    protected override void OnChildAdded(Widget child)
    {
        base.OnChildAdded(child);
        if (child is not RadioButton button) return;
        int count = Buttons.Count();
        Height = count + 2;
        button.X = 1;
        button.Y = count;
        button.Id = count;
        button.MatchWindowBackground = matchWindowBackground;
        int labelWidth = TextMetrics.Width(button.Text);
        if (labelWidth + 4 > Width) Width = labelWidth + 7;
        var first = Buttons.First();
        Activate(first);
        if (button.PendingSelected) Select(button.Id);
        else if (requiresSelection && SelectedButton == null) Select(1);
    }

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        var color = GetColor(IsAbsoluteActive ? "tradiogroup.active" : "tradiogroup.inactive");
        var style = BorderStyle.SingleVDoubleH;
        int labelWidth = TextMetrics.Width(label);
        if (matchWindowBackground)
        {
            context.DrawForegroundBox(0, 0, Width, Height, color, style);
            context.HForegroundLine(1, 0, labelWidth + 2, ' ', color);
            context.PutForegroundString(2, 0, label, color);
        }
        else
        {
            context.DrawBox(0, 0, Width, Height, color, color, style, false);
            context.HLine(1, 0, labelWidth + 2, ' ', color);
            context.PutString(2, 0, label, color);
        }
    }
}

/// <summary>
/// A radio button drawn as <c>(•) label</c> (Jexer <c>TRadioButton</c>); must be a child of a <see cref="RadioGroup"/>.
/// Selected by a left click on the brackets, Space or Alt+mnemonic; Esc clears the selection unless the group requires one.
/// </summary>
public class RadioButton : Widget
{
    private MnemonicString mnemonic = new("");

    /// <summary>Creates a radio button.</summary>
    public RadioButton()
    {
        Height = 1;
        Width = 4;
        CursorVisible = true;
        CursorX = 1;
    }

    /// <summary>The label, with an optional <c>&amp;</c> mnemonic marker. Sets the width (label + 4).</summary>
    public string Text
    {
        get => mnemonic.RawLabel;
        set
        {
            mnemonic = new MnemonicString(value ?? "");
            Width = TextMetrics.Width(mnemonic.RawLabel) + 4;
        }
    }

    /// <summary>The parsed mnemonic.</summary>
    public MnemonicString Mnemonic => mnemonic;

    /// <summary>1-based position within the group (0 before being added).</summary>
    public int Id { get; internal set; }

    /// <summary>When true (default) the button keeps the background already drawn beneath it.</summary>
    public bool MatchWindowBackground { get; set; } = true;

    internal bool IsSelectedField;
    internal bool PendingSelected;

    /// <summary>Whether this button is the group's selection. May be set before the button is added to its group.</summary>
    public bool IsSelected
    {
        get => Parent is RadioGroup ? IsSelectedField : PendingSelected;
        set
        {
            if (Parent is RadioGroup group) group.Select(value ? Id : 0);
            else PendingSelected = value;
        }
    }

    internal override int MnemonicPass => 2;

    internal override bool TryInvokeMnemonic(Widget parent, Rune c)
    {
        if (!Enabled || !Visible || !mnemonic.Matches(c)) return false;
        parent.Activate(this);
        OnKeypress(new KeyEvent(Key.Character, new Rune(' ')));
        return true;
    }

    /// <inheritdoc/>
    protected internal override void OnMouseDown(UiMouseEvent mouse)
    {
        if (mouse.Y == 0 && mouse.X >= 0 && mouse.X <= 2 && mouse.IsLeft) (Parent as RadioGroup)?.Select(Id);
    }

    /// <inheritdoc/>
    protected internal override void OnKeypress(KeyEvent key)
    {
        if (key.Modifiers == KeyModifiers.None && Parent is RadioGroup group)
        {
            if (key.Key == Key.Character && key.Char.Value == ' ')
            {
                group.Select(Id);
                return;
            }

            if (key.Key == Key.Escape)
            {
                if (!group.RequiresSelection) group.Select(0);
                return;
            }
        }

        base.OnKeypress(key);
    }

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        bool active = IsAbsoluteActive;
        var color = GetColor(active ? "tradiobutton.active" : "tradiobutton.inactive");
        var mnemonicColor = GetColor(active ? "tradiobutton.mnemonic.highlighted" : "tradiobutton.mnemonic");
        ToggleDrawing.Draw(context, MatchWindowBackground, '(', IsSelectedField ? GraphicsChars.CP437[0x07] : ' ', ')', mnemonic, color,
            mnemonicColor);
    }
}
