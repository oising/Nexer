using System.Globalization;
using System.Xml;
using XamlX;
using XamlX.Ast;
using XamlX.Transform;

namespace Nexer.Xaml.Compiler;

/// <summary>Diagnostic codes reported by the Nexer XAML compiler.</summary>
public static class NexerXamlDiagnosticCodes
{
    /// <summary>Generic XAML compilation error (unknown type/property, invalid content, ...).</summary>
    public const string General = "NXAML0001";

    /// <summary>An attribute or text value cannot be converted to the target type.</summary>
    public const string InvalidValue = "NXAML0002";

    /// <summary>The <c>x:Class</c> type does not exist or is incompatible with the root element.</summary>
    public const string ClassNotFound = "NXAML0003";

    /// <summary>The same <c>x:Name</c> is used twice in a document.</summary>
    public const string DuplicateName = "NXAML0004";

    /// <summary>The document is not well-formed XML.</summary>
    public const string XmlError = "NXAML0005";

    /// <summary>A directive is used in an unsupported place (e.g. <c>x:Class</c> on a non-root element).</summary>
    public const string InvalidDirective = "NXAML0006";

    /// <summary>Internal error while emitting code for a document.</summary>
    public const string EmitError = "NXAML0007";

    /// <summary>Warning: an obsolete member is used.</summary>
    public const string Obsolete = "NXAML0100";

    // NXAML0101-NXAML0149 are reserved for compiled bindings (docs\binding.md).

    /// <summary>A DataContext binding has no <c>x:DataType</c> in scope (or a per-item binding has no <c>DataType</c>).</summary>
    public const string MissingDataType = "NXAML0101";

    /// <summary>A binding path segment does not name a readable property of the source type.</summary>
    public const string UnknownMember = "NXAML0102";

    /// <summary>A binding path segment is not public.</summary>
    public const string InaccessibleMember = "NXAML0103";

    /// <summary>A two-way (or one-way-to-source) binding's path ends in a read-only property.</summary>
    public const string TwoWayToReadOnly = "NXAML0104";

    /// <summary>Warning: the source and target types of a binding are not convertible.</summary>
    public const string BindingTypeMismatch = "NXAML0105";

    /// <summary>A binding is assigned to something that cannot be bound (non-widget target, no public setter, collection item).</summary>
    public const string InvalidBindingTarget = "NXAML0106";

    /// <summary>A binding's <c>ElementName</c> does not exist in the document.</summary>
    public const string UnknownElementName = "NXAML0107";

    /// <summary>A binding's <c>RelativeSource</c> is invalid or unsupported.</summary>
    public const string InvalidRelativeSource = "NXAML0108";

    /// <summary>A binding path is malformed or uses unsupported syntax.</summary>
    public const string InvalidBindingPath = "NXAML0109";
}

/// <summary>Severity of a <see cref="NexerXamlDiagnostic"/>.</summary>
public enum NexerXamlSeverity
{
    /// <summary>A warning; compilation continues.</summary>
    Warning,

    /// <summary>An error; no code is produced for the document.</summary>
    Error,
}

/// <summary>A XAML compilation diagnostic.</summary>
/// <param name="Code">The diagnostic code (see <see cref="NexerXamlDiagnosticCodes"/>).</param>
/// <param name="Severity">The severity.</param>
/// <param name="Message">The message.</param>
/// <param name="File">The document path, if known.</param>
/// <param name="Line">1-based line, or 0 when unknown.</param>
/// <param name="Column">1-based column, or 0 when unknown.</param>
public sealed record NexerXamlDiagnostic(
    string Code,
    NexerXamlSeverity Severity,
    string Message,
    string? File,
    int Line,
    int Column)
{
    /// <summary>
    /// Formats the diagnostic in the canonical MSBuild form <c>file(line,col): error CODE: message</c>, which
    /// MSBuild's <c>Exec</c> task (and IDEs) recognise as an error/warning location.
    /// </summary>
    public override string ToString()
    {
        var location = File ?? "XAML";
        if (Line > 0)
        {
            location += Column > 0
                ? string.Create(CultureInfo.InvariantCulture, $"({Line},{Column})")
                : string.Create(CultureInfo.InvariantCulture, $"({Line})");
        }

        var severity = Severity == NexerXamlSeverity.Error ? "error" : "warning";
        var message = Message.ReplaceLineEndings(" ");
        return $"{location}: {severity} {Code}: {message}";
    }
}

/// <summary>A XAML error with a specific Nexer diagnostic code.</summary>
public class NexerXamlException : XamlTransformException
{
    /// <summary>Creates the exception.</summary>
    public NexerXamlException(string code, string message, IXamlLineInfo? lineInfo, Exception? innerException = null)
        : base(message, lineInfo, innerException)
    {
        Code = code;
        Title = message;
    }

    /// <summary>The diagnostic code.</summary>
    public string Code { get; }

    /// <summary>The message without location information.</summary>
    public string Title { get; }
}

/// <summary>Thrown by the runtime loader when a document fails to compile.</summary>
public sealed class NexerXamlCompilationException : Exception
{
    /// <summary>Creates the exception.</summary>
    public NexerXamlCompilationException(IReadOnlyList<NexerXamlDiagnostic> diagnostics)
        : base(FormatMessage(diagnostics))
    {
        Diagnostics = diagnostics;
    }

    /// <summary>All diagnostics reported for the document.</summary>
    public IReadOnlyList<NexerXamlDiagnostic> Diagnostics { get; }

    private static string FormatMessage(IReadOnlyList<NexerXamlDiagnostic> diagnostics)
    {
        var errors = diagnostics.Where(d => d.Severity == NexerXamlSeverity.Error).ToList();
        return errors.Count switch
        {
            0 => "XAML compilation failed.",
            1 => errors[0].ToString(),
            _ => $"XAML compilation failed with {errors.Count} errors:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors),
        };
    }
}

/// <summary>Collects XamlX diagnostics and converts them to <see cref="NexerXamlDiagnostic"/>.</summary>
internal sealed class NexerXamlDiagnosticSink
{
    private readonly List<NexerXamlDiagnostic> _diagnostics = [];

    public string? CurrentFile { get; set; }

    public IReadOnlyList<NexerXamlDiagnostic> Diagnostics => _diagnostics;

    public bool HasErrors => _diagnostics.Any(d => d.Severity == NexerXamlSeverity.Error);

    public void Clear() => _diagnostics.Clear();

    public XamlDiagnosticsHandler CreateHandler() => new()
    {
        CodeMappings = MapCode,
        ExceptionFormatter = FormatException,
        HandleDiagnostic = Handle,
    };

    public void Add(NexerXamlDiagnostic diagnostic) => _diagnostics.Add(diagnostic);

    public void AddError(string code, string message, IXamlLineInfo? lineInfo) =>
        Add(new NexerXamlDiagnostic(code, NexerXamlSeverity.Error, message, CurrentFile, lineInfo?.Line ?? 0, lineInfo?.Position ?? 0));

    /// <summary>Records an exception thrown outside the transformation pipeline (parsing, emitting).</summary>
    public void AddException(Exception exception, string fallbackCode)
    {
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                AddException(inner, fallbackCode);
            }

            return;
        }

        var lineInfo = exception as XmlException;
        var code = exception switch
        {
            NexerXamlException nexer => nexer.Code,
            XamlParseException => fallbackCode,
            XmlException => NexerXamlDiagnosticCodes.XmlError,
            _ => fallbackCode,
        };
        Add(new NexerXamlDiagnostic(code, NexerXamlSeverity.Error, FormatException(exception), CurrentFile,
            lineInfo?.LineNumber ?? 0, lineInfo?.LinePosition ?? 0));
    }

    private XamlDiagnosticSeverity Handle(XamlDiagnostic diagnostic)
    {
        var severity = diagnostic.Severity >= XamlDiagnosticSeverity.Error ? NexerXamlSeverity.Error : NexerXamlSeverity.Warning;
        if (diagnostic.Severity == XamlDiagnosticSeverity.None)
        {
            return diagnostic.Severity;
        }

        Add(new NexerXamlDiagnostic(
            diagnostic.Code,
            severity,
            diagnostic.Title,
            CurrentFile ?? diagnostic.Document,
            diagnostic.LineNumber ?? 0,
            diagnostic.LinePosition ?? 0));
        return diagnostic.Severity;
    }

    private static string MapCode(object code) => code switch
    {
        NexerXamlException nexer => nexer.Code,
        XamlXWellKnownDiagnosticCodes.Obsolete => NexerXamlDiagnosticCodes.Obsolete,
        XamlParseException => NexerXamlDiagnosticCodes.General,
        XmlException => NexerXamlDiagnosticCodes.XmlError,
        Exception => NexerXamlDiagnosticCodes.General,
        string s => s,
        _ => code.ToString() ?? NexerXamlDiagnosticCodes.General,
    };

    // XmlException.Message appends " Line x, position y."; diagnostics carry the location separately.
    private static string FormatException(Exception exception) => exception switch
    {
        NexerXamlException nexer => nexer.Title,
        XmlException xml when xml.LineNumber > 0 && xml.Message.EndsWith(
            string.Create(CultureInfo.InvariantCulture, $" Line {xml.LineNumber}, position {xml.LinePosition}."), StringComparison.Ordinal) =>
            xml.Message[..xml.Message.LastIndexOf(" Line ", StringComparison.Ordinal)],
        _ => exception.Message,
    };
}
