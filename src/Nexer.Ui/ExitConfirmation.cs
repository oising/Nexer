// Ported from Jexer TApplication.java (MIT, © Autumn Lamonte).
using Nexer.Ui.Controls;

namespace Nexer.Ui;

/// <summary>Asks the user to confirm exiting (Jexer's "Exit application?" Yes/No box).</summary>
internal static class ExitConfirmation
{
    public static async Task<bool> ConfirmAsync(Application application)
    {
        var result = await MessageBox.ShowAsync(application, "Confirmation", "Exit application?", MessageBoxButtons.YesNo)
            .ConfigureAwait(true);
        return result == MessageBoxResult.Yes;
    }
}
