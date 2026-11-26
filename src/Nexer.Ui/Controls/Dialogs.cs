// Ported from Jexer TMessageBox.java, TInputBox.java and TFileOpenBox.java (MIT, © Autumn Lamonte).
using Nexer.Input;
using Nexer.Ui.Menus;

namespace Nexer.Ui.Controls;

/// <summary>The buttons of a <see cref="MessageBox"/> (Jexer <c>TMessageBox.Type</c>).</summary>
public enum MessageBoxButtons
{
    /// <summary>OK.</summary>
    Ok,

    /// <summary>OK and Cancel.</summary>
    OkCancel,

    /// <summary>Yes and No.</summary>
    YesNo,

    /// <summary>Yes, No and Cancel.</summary>
    YesNoCancel,
}

/// <summary>The button chosen in a <see cref="MessageBox"/> (Jexer <c>TMessageBox.Result</c>).</summary>
public enum MessageBoxResult
{
    /// <summary>OK.</summary>
    Ok,

    /// <summary>Cancel (also the result of closing an OK/Cancel or Yes/No/Cancel box without choosing).</summary>
    Cancel,

    /// <summary>Yes.</summary>
    Yes,

    /// <summary>No (also the result of closing a Yes/No box without choosing).</summary>
    No,
}

/// <summary>
/// A modal, centred message window with one to three buttons (Jexer <c>TMessageBox</c>). Set <see cref="Title"/>,
/// <see cref="Message"/> (lines separated by <c>'\n'</c>) and <see cref="Buttons"/>, then await
/// <see cref="ShowAsync()"/>; or call <see cref="ShowAsync(Application, string, string, MessageBoxButtons)"/>.
/// The buttons' first letters (o, c, y, n — with or without Alt) choose them.
/// </summary>
public class MessageBox : Window
{
    private readonly List<(Button Button, char Key)> buttons = [];
    private bool built;

    /// <summary>Creates a message box with an OK button.</summary>
    public MessageBox()
    {
        Modal = true;
        Resizable = false;
        ZoomBox = false;
        Width = 15;
        Height = 6;
    }

    /// <summary>The message; <c>'\n'</c> separates lines.</summary>
    public string Message
    {
        get => messageValue;
        set => SetProperty(ref messageValue, value);
    }

    private string messageValue = "";

    /// <summary>The buttons shown.</summary>
    public MessageBoxButtons Buttons
    {
        get => buttonsValue;
        set => SetProperty(ref buttonsValue, value);
    }

    private MessageBoxButtons buttonsValue = MessageBoxButtons.Ok;

    /// <summary>The chosen button. Until one is chosen: OK, Cancel, No or Cancel depending on <see cref="Buttons"/>.</summary>
    public MessageBoxResult Result { get; protected set; }

    /// <summary>Shows a message box modally and returns the chosen button (Jexer <c>TApplication.messageBox</c>).</summary>
    public static Task<MessageBoxResult> ShowAsync(
        Application application, string title, string message, MessageBoxButtons buttons = MessageBoxButtons.Ok)
    {
        ArgumentNullException.ThrowIfNull(application);
        var box = new MessageBox { Title = title, Message = message, Buttons = buttons };
        application.Windows.Add(box);
        return box.ShowAsync();
    }

    /// <summary>Shows this box modally (adding it to <see cref="Ui.Application.Current"/> if needed) and returns the chosen button.</summary>
    public async Task<MessageBoxResult> ShowAsync()
    {
        await ShowModalAsync().ConfigureAwait(true);
        return Result;
    }

    /// <summary>The message lines.</summary>
    protected IReadOnlyList<string> Lines => (Message ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    /// <inheritdoc/>
    protected internal override void OnAddedToApplication()
    {
        base.OnAddedToApplication();
        if (built) return;
        built = true;
        Build();
    }

    /// <summary>Creates the labels and buttons and sizes the window (Jexer's constructor).</summary>
    protected virtual void Build()
    {
        var lines = Lines;
        int width = TextMetrics.Width(Title) + 12;
        Height = 6 + lines.Count;
        foreach (var line in lines) width = Math.Max(width, TextMetrics.Width(line) + 4);
        int screenWidth = Application?.ScreenWidth ?? width;
        Width = Math.Min(width, screenWidth);

        int lineY = 1;
        foreach (var line in lines)
        {
            // Literal text: escape '&' so it is not taken as a mnemonic.
            Children.Add(new Label
            {
                Text = line.Replace("&", "&&", StringComparison.Ordinal),
                X = 1,
                Y = lineY++,
                ColorKey = "twindow.background.modal",
            });
        }

        lineY++;
        int buttonX;
        switch (Buttons)
        {
            case MessageBoxButtons.Ok:
                Result = MessageBoxResult.Ok;
                if (Width < 15) Width = 15;
                buttonX = (Width - 11) / 2;
                AddButton("  &OK  ", 'o', buttonX, lineY, MessageBoxResult.Ok);
                break;
            case MessageBoxButtons.OkCancel:
                Result = MessageBoxResult.Cancel;
                if (Width < 26) Width = 26;
                buttonX = (Width - 22) / 2;
                AddButton("  &OK  ", 'o', buttonX, lineY, MessageBoxResult.Ok);
                buttonX += 8 + 4;
                AddButton("&Cancel", 'c', buttonX, lineY, MessageBoxResult.Cancel);
                break;
            case MessageBoxButtons.YesNo:
                Result = MessageBoxResult.No;
                if (Width < 20) Width = 20;
                buttonX = (Width - 16) / 2;
                AddButton("&Yes", 'y', buttonX, lineY, MessageBoxResult.Yes);
                buttonX += 5 + 4;
                AddButton("&No", 'n', buttonX, lineY, MessageBoxResult.No);
                break;
            case MessageBoxButtons.YesNoCancel:
                Result = MessageBoxResult.Cancel;
                if (Width < 31) Width = 31;
                buttonX = (Width - 27) / 2;
                AddButton("&Yes", 'y', buttonX, lineY, MessageBoxResult.Yes);
                buttonX += 5 + 4;
                AddButton("&No", 'n', buttonX, lineY, MessageBoxResult.No);
                buttonX += 4 + 4;
                AddButton("&Cancel", 'c', buttonX, lineY, MessageBoxResult.Cancel);
                break;
            default:
                throw new InvalidOperationException("Invalid message box buttons: " + Buttons);
        }
    }

    /// <summary>The buttons created by <see cref="Build"/>, left to right.</summary>
    protected IReadOnlyList<Button> ButtonWidgets => buttons.Select(b => b.Button).ToList();

    private void AddButton(string text, char key, int x, int y, MessageBoxResult result)
    {
        var button = new Button { Text = text, X = x, Y = y };
        button.Click += (_, _) => Choose(result);
        Children.Add(button);
        buttons.Add((button, key));
    }

    /// <summary>Sets <see cref="Result"/> and closes the box.</summary>
    protected void Choose(MessageBoxResult result)
    {
        Result = result;
        Close();
    }

    /// <inheritdoc/>
    protected internal override void OnClosed()
    {
        DialogResult = Result;
        base.OnClosed();
    }

    /// <summary>Whether the plain/Alt letter shortcuts choose buttons (false for <see cref="InputBox"/>).</summary>
    protected virtual bool LetterShortcuts => true;

    /// <inheritdoc/>
    protected internal override void OnKeypress(KeyEvent key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (LetterShortcuts && key.Key == Key.Character &&
            key.Modifiers is KeyModifiers.None or KeyModifiers.Alt or KeyModifiers.Shift)
        {
            string ch = key.Char.ToString().ToLowerInvariant();
            foreach (var (button, letter) in buttons)
            {
                if (ch.Length == 1 && ch[0] == letter)
                {
                    button.Dispatch();
                    return;
                }
            }
        }

        base.OnKeypress(key);
    }
}

/// <summary>
/// A <see cref="MessageBox"/> with a text field (Jexer <c>TInputBox</c>). Enter in the field chooses OK/Yes.
/// </summary>
public class InputBox : MessageBox
{
    private readonly Field inputField = new();

    /// <summary>The entered text (the initial text before showing).</summary>
    public string Text
    {
        get => inputField.Text;
        set
        {
            inputField.Text = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Shows an input box modally and returns the text, or null when the box was cancelled (result Cancel or No).
    /// </summary>
    public static async Task<string?> ShowAsync(
        Application application, string title, string message, string text = "", MessageBoxButtons buttons = MessageBoxButtons.Ok)
    {
        ArgumentNullException.ThrowIfNull(application);
        var box = new InputBox { Title = title, Message = message, Text = text, Buttons = buttons };
        application.Windows.Add(box);
        var result = await box.ShowAsync().ConfigureAwait(true);
        return result is MessageBoxResult.Ok or MessageBoxResult.Yes ? box.Text : null;
    }

    /// <inheritdoc/>
    protected override bool LetterShortcuts => false;

    /// <inheritdoc/>
    protected override void Build()
    {
        base.Build();
        foreach (var button in ButtonWidgets) button.Y += 2;
        Height += 2;
        inputField.SetDimensions(1, Height - 6, Width - 4, 1);
        inputField.Submitted += (_, _) => Choose(Buttons is MessageBoxButtons.Ok or MessageBoxButtons.OkCancel
            ? MessageBoxResult.Ok
            : MessageBoxResult.Yes);
        Children.Add(inputField);
        Activate(inputField);
        inputField.End();
    }
}

/// <summary>What a <see cref="FileOpenBox"/> is used for (Jexer <c>TFileOpenBox.Type</c>).</summary>
public enum FileOpenBoxType
{
    /// <summary>Open an existing file.</summary>
    Open,

    /// <summary>Save: any file name is accepted.</summary>
    Save,

    /// <summary>Select an existing file or directory, or a new name.</summary>
    Select,
}

/// <summary>
/// A modal file chooser (Jexer <c>TFileOpenBox</c>): a path inputField, a directory tree, a file list and Open/Cancel
/// buttons. Set <see cref="Path"/>, <see cref="Type"/> and optionally <see cref="Filter"/>, then await
/// <see cref="ShowAsync()"/>; the result is the chosen file name or null when cancelled.
/// </summary>
public class FileOpenBox : Window
{
    private readonly Field entryField = new();
    private readonly TreeView treeView = new();
    private readonly DirectoryList directoryList = new();
    private readonly Button openButton = new();
    private readonly Button cancelButton = new() { Text = "&Cancel" };
    private readonly StatusBar statusBar = new();
    private bool built;

    /// <summary>Creates a file open box (78×22).</summary>
    public FileOpenBox()
    {
        Modal = true;
        Width = 78;
        Height = 22;
        MinimumWidth = 78;
        MinimumHeight = 22;
    }

    /// <summary>The starting directory (default: the current directory).</summary>
    public string? Path
    {
        get => pathValue;
        set => SetProperty(ref pathValue, value);
    }

    private string? pathValue;

    /// <summary>The purpose; sets the title and the label of the Open button.</summary>
    public FileOpenBoxType Type
    {
        get => typeValue;
        set => SetProperty(ref typeValue, value);
    }

    private FileOpenBoxType typeValue = FileOpenBoxType.Open;

    /// <summary>File name filters: regular expressions separated by <c>';'</c> (see <see cref="DirectoryList.Filter"/>).</summary>
    public string? Filter
    {
        get => filterValue;
        set => SetProperty(ref filterValue, value);
    }

    private string? filterValue;

    /// <summary>The chosen file name, or null.</summary>
    public string? FileName { get; private set; }

    /// <summary>Shows a file open box modally and returns the chosen file name, or null when cancelled.</summary>
    public static Task<string?> ShowAsync(
        Application application, string path, FileOpenBoxType type = FileOpenBoxType.Open, string? filter = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        var box = new FileOpenBox { Path = path, Type = type, Filter = filter };
        application.Windows.Add(box);
        return box.ShowAsync();
    }

    /// <summary>Shows this box modally and returns the chosen file name, or null when cancelled.</summary>
    public async Task<string?> ShowAsync()
    {
        await ShowModalAsync().ConfigureAwait(true);
        return FileName;
    }

    /// <inheritdoc/>
    protected internal override void OnAddedToApplication()
    {
        base.OnAddedToApplication();
        if (built) return;
        built = true;
        Build();
    }

    private void Build()
    {
        string path = System.IO.Path.GetFullPath(string.IsNullOrEmpty(Path) ? "." : Path);
        string openLabel;
        switch (Type)
        {
            case FileOpenBoxType.Save:
                openLabel = " &Save ";
                Title = "Save File...";
                break;
            case FileOpenBoxType.Select:
                openLabel = "S&elect";
                Title = "Select File...";
                break;
            default:
                openLabel = " &Open ";
                Title = "Open File...";
                break;
        }

        StatusBar = statusBar;
        entryField.Text = path;
        entryField.Submitted += (_, _) => CheckFilename(entryField.Text);
        Children.Add(entryField);

        Children.Add(treeView);
        treeView.ItemActivated += (_, _) =>
        {
            ShowSelectedDirectory();
            if (treeView.SelectedItem is DirectoryTreeItem { Path: { } dir }) entryField.Text = dir;
            Activate(treeView);
        };
        treeView.SelectionChanged += (_, _) =>
        {
            ShowSelectedDirectory();
            statusBar.Text = "";
        };

        directoryList.Filter = Filter;
        Children.Add(directoryList);
        directoryList.ItemActivated += (_, _) =>
        {
            if (!PickListedFile()) return;
            CheckFilename(entryField.Text);
        };
        directoryList.ItemClicked += (_, _) =>
        {
            if (PickListedFile()) statusBar.Text = entryField.Text;
        };
        directoryList.SelectionChanged += (_, _) =>
        {
            if (directoryList.IsActive) statusBar.Text = directoryList.SelectedFile?.FullName ?? "";
        };

        openButton.Text = openLabel;
        openButton.Enabled = Type != FileOpenBoxType.Open;
        openButton.Click += (_, _) => CheckFilename(entryField.Text);
        Children.Add(openButton);
        cancelButton.Click += (_, _) => Cancel();
        Children.Add(cancelButton);

        LayoutContent();
        DirectoryTreeItem.OpenPath(treeView, path);
        directoryList.Path = path;
        if (Type == FileOpenBoxType.Save)
        {
            entryField.Text = path.EndsWith(System.IO.Path.DirectorySeparatorChar)
                ? path
                : path + System.IO.Path.DirectorySeparatorChar;
            entryField.End();
            Activate(entryField);
        }
        else
        {
            entryField.End();
            Activate(directoryList);
        }

        statusBar.Text = directoryList.SelectedFile?.FullName ?? "";
    }

    private void ShowSelectedDirectory()
    {
        if (treeView.SelectedItem is not DirectoryTreeItem { Path: { } dir }) return;
        if (directoryList.Path != dir) directoryList.Path = dir;
        if (Type == FileOpenBoxType.Open) openButton.Enabled = false;
    }

    private bool PickListedFile()
    {
        if (directoryList.SelectedFile is not { } file) return false;
        entryField.Text = file.FullName;
        entryField.End();
        openButton.Enabled = true;
        Activate(entryField);
        return true;
    }

    private void LayoutContent()
    {
        if (!built) return;
        int clientWidth = Width - 2;
        int listHeight = Math.Max(1, Height - 6);
        entryField.SetDimensions(1, 1, Width - 4, 1);
        treeView.SetDimensions(1, 3, 30, listHeight);
        directoryList.SetDimensions(35, 3, Math.Max(1, Width - 51), listHeight);
        int buttonX = clientWidth - TextMetrics.Width(openButton.Text) - 3;
        openButton.SetDimensions(buttonX, 3, openButton.Width, 1);
        cancelButton.SetDimensions(buttonX, 6, cancelButton.Width, 1);
    }

    /// <inheritdoc/>
    protected internal override void OnSizeChanged()
    {
        base.OnSizeChanged();
        LayoutContent();
    }

    private void Cancel()
    {
        FileName = null;
        Close();
    }

    private void CheckFilename(string name)
    {
        if (File.Exists(name) || (Type == FileOpenBoxType.Select && System.IO.Directory.Exists(name)))
        {
            FileName = name;
            Close();
            return;
        }

        if (System.IO.Directory.Exists(name))
        {
            DirectoryTreeItem.OpenPath(treeView, name);
            if (Type == FileOpenBoxType.Open) openButton.Enabled = false;
            directoryList.Path = name;
            return;
        }

        if (Type != FileOpenBoxType.Open)
        {
            FileName = name;
            Close();
        }
    }

    /// <inheritdoc/>
    protected internal override void OnClosed()
    {
        DialogResult = FileName;
        base.OnClosed();
    }

    /// <inheritdoc/>
    protected internal override void OnKeypress(KeyEvent key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Key == Key.Escape && key.Modifiers == KeyModifiers.None && !IsInMovement)
        {
            Cancel();
            return;
        }

        base.OnKeypress(key);
    }

    /// <inheritdoc/>
    protected internal override void Draw(DrawContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Draw(context);
        if (!built) return;
        // The divider between the tree and the file list (window coordinates: client + border).
        context.VLine(treeView.X + treeView.Width + 2, treeView.Y + 1, treeView.Height, GraphicsChars.WindowSide, BackgroundColor);
    }
}
