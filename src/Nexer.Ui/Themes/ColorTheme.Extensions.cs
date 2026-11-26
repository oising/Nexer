namespace Nexer.Ui;

public partial class ColorTheme
{
    /// <summary>
    /// Nexer extension keys (not in Jexer) applied on top of <see cref="DefaultColorSpecs"/>: the validation error
    /// colours used when <see cref="Data.Validation.GetHasErrors"/> is true, and the status bar row that shows the
    /// focused widget's first error.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> ExtensionColorSpecs { get; } =
    [
        new("tfield.error", "bold white on red"),
        new("tcheckbox.error", "bold red on blue"),
        new("tcombobox.error", "bold white on red"),
        new("tlist.error", "white on red"),
        new("tstatusbar.error", "bold white on red"),
    ];
}
