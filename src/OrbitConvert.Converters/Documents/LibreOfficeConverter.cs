using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;
using OrbitConvert.Core.Processes;
using OrbitConvert.Core.Tools;
using OrbitConvert.Infrastructure;

namespace OrbitConvert.Converters.Documents;

/// <summary>
/// Office document conversion through a headless LibreOffice. Uses a private user profile so it
/// never interferes with (or is blocked by) a LibreOffice window the user has open. LibreOffice
/// conversions are serialized because one profile cannot be used by two instances at once.
/// </summary>
public sealed class LibreOfficeConverter : IConverter
{
    public const string PdfToDocxLimitations =
        "Experimental. LibreOffice imports PDF pages as positioned text boxes and drawings, not flowing paragraphs, " +
        "so editing is awkward and complex layouts, tables and fonts may not survive. Scanned PDFs contain no text (no OCR is performed).";

    private static readonly SemaphoreSlim Serial = new(1, 1);

    private static readonly FileFormat[] OfficeInputs =
        [KnownFormats.Docx, KnownFormats.Doc, KnownFormats.Odt, KnownFormats.Rtf, KnownFormats.Txt, KnownFormats.Html];

    private readonly IToolLocator _tools;
    private readonly IProcessRunner _runner;
    private readonly AppPaths _paths;

    public LibreOfficeConverter(IToolLocator tools, IProcessRunner runner, AppPaths paths)
    {
        _tools = tools;
        _runner = runner;
        _paths = paths;
        Routes =
        [
            Route(KnownFormats.Pdf, OfficeInputs, 50, "Convert to PDF with LibreOffice"),
            Route(KnownFormats.Docx, [KnownFormats.Doc, KnownFormats.Odt, KnownFormats.Rtf], 51, "Convert to Word with LibreOffice"),
            Route(KnownFormats.Odt, [KnownFormats.Docx, KnownFormats.Doc, KnownFormats.Rtf], 55, "Convert to OpenDocument with LibreOffice"),
            Route(KnownFormats.Txt, [KnownFormats.Doc, KnownFormats.Rtf], 57, "Extract plain text with LibreOffice"),
            new ConversionRoute
            {
                TargetKey = KnownFormats.Docx.Id,
                Label = "DOCX",
                Kind = RouteKind.Convert,
                Inputs = ConversionRoute.Formats(KnownFormats.Pdf),
                Output = KnownFormats.Docx,
                Priority = 60,
                Experimental = true,
                Description = "Import PDF into Word format (experimental)",
                Limitations = PdfToDocxLimitations,
            },
        ];
    }

    private static ConversionRoute Route(FileFormat output, FileFormat[] inputs, int priority, string description) => new()
    {
        TargetKey = output.Id,
        Label = output.Label,
        Kind = RouteKind.Convert,
        Inputs = ConversionRoute.Formats(inputs.Where(i => i != output).ToArray()),
        Output = output,
        Priority = priority,
        Description = description,
        Limitations = "Rendering depends on installed fonts; documents with unusual layouts may differ from the original application.",
    };

    public string Id => "libreoffice";
    public string DisplayName => "LibreOffice";
    public IReadOnlyList<ConversionRoute> Routes { get; }

    public RouteAvailability GetAvailability(ConversionRoute route) =>
        _tools.Find(ExternalTool.LibreOffice) is null ? RouteAvailability.Missing(ExternalTool.LibreOffice) : RouteAvailability.Available;

    public async Task<IReadOnlyList<string>> ExecuteAsync(ConversionContext context, CancellationToken cancellationToken)
    {
        var output = context.DefaultOutputPath();
        await ConvertAsync(context.Input.Path, context.Input.Format!, context.Route.Output!, output, context.WorkDirectory, cancellationToken)
            .ConfigureAwait(false);
        return [output];
    }

    internal async Task ConvertAsync(string inputPath, FileFormat inputFormat, FileFormat outputFormat, string outputPath,
        string workDirectory, CancellationToken ct)
    {
        var soffice = _tools.Find(ExternalTool.LibreOffice) ?? throw new ConversionException("LibreOffice is not installed.");

        // Stage the input under a plain ASCII name: LibreOffice derives the output name from it and
        // handles unusual Unicode or very long paths inconsistently.
        var stageDir = Path.Combine(workDirectory, "lo-" + Guid.NewGuid().ToString("N")[..8]);
        var outDir = Path.Combine(stageDir, "out");
        Directory.CreateDirectory(outDir);
        var staged = Path.Combine(stageDir, "input" + inputFormat.PrimaryExtension);
        File.Copy(inputPath, staged);

        var args = new List<string>
        {
            "--headless", "--invisible", "--nologo", "--nodefault", "--nolockcheck", "--norestore", "--nofirststartwizard",
            "-env:UserInstallation=" + new Uri(_paths.LibreOfficeProfile + Path.DirectorySeparatorChar).AbsoluteUri,
        };
        if (inputFormat == KnownFormats.Pdf)
        {
            args.Add("--infilter=writer_pdf_import");
        }
        args.AddRange(["--convert-to", FilterFor(inputFormat, outputFormat), "--outdir", outDir, staged]);

        await Serial.WaitAsync(ct).ConfigureAwait(false);
        ProcessResult result;
        try
        {
            result = await _runner.RunAsync(new ProcessSpec(soffice, args)
            {
                WorkingDirectory = stageDir,
                Timeout = TimeSpan.FromMinutes(10),
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            Serial.Release();
        }

        // LibreOffice often exits with 0 even when it could not convert, so check for the file.
        var produced = Path.Combine(outDir, "input" + outputFormat.PrimaryExtension);
        if (!File.Exists(produced))
        {
            var detail = (result.StandardErrorTail + "\n" + result.StandardOutputTail).Trim();
            throw new ConversionException(
                $"LibreOffice could not convert this {inputFormat.Label} file to {outputFormat.Label}." +
                (detail.Contains("source file could not be loaded", StringComparison.OrdinalIgnoreCase) ? " The file may be damaged or password-protected." : ""),
                technicalDetail: detail);
        }
        File.Move(produced, outputPath);
        Directory.Delete(stageDir, recursive: true);
    }

    internal static string FilterFor(FileFormat input, FileFormat output) => output.Id switch
    {
        "pdf" when input == KnownFormats.Html => "pdf:writer_web_pdf_Export",
        "pdf" => "pdf:writer_pdf_Export",
        "docx" => "docx:MS Word 2007 XML",
        "odt" => "odt:writer8",
        "txt" => "txt:Text (encoded):UTF8",
        _ => throw new ConversionException($"LibreOffice conversion to {output.Label} is not supported."),
    };
}
