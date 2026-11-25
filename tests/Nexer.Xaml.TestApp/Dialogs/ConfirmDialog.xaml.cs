using Nexer.Xaml.Fixtures;

namespace Nexer.Xaml.TestApp.Dialogs;

/// <summary>Code-behind using the source-generated <c>InitializeComponent()</c> and x:Name fields.</summary>
public partial class ConfirmDialog : Window
{
    /// <summary>Creates the dialog.</summary>
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>Whether the user confirmed.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>The generated (internal) field for the message label, exposed for tests.</summary>
    public Label MessageLabel => Message;

    /// <summary>The generated (internal) field for the "No" button, exposed for tests.</summary>
    public Button NoButtonField => NoButton;

    private void OnYes(object? sender, EventArgs e) => Confirmed = true;
}
