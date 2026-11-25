namespace Nexer.Ui;

/// <summary>Asks the user to confirm exiting (replaced by a MessageBox in the dialogs milestone).</summary>
internal static class ExitConfirmation
{
    public static Task<bool> ConfirmAsync(Application application) => Task.FromResult(application != null);
}
