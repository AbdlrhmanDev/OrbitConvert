using System.Text;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;
using OrbitConvert.Core.Processes;
using OrbitConvert.Core.Tools;

namespace OrbitConvert.Converters.Documents;

/// <summary>
/// Markup and e-book conversion via Pandoc. Runs with <c>--sandbox</c> by default so a document
/// cannot make Pandoc read other local files (for example through image references or includes).
/// </summary>
public sealed class PandocConverter : IConverter
{
    private static readonly FileFormat[] Inputs =
        [KnownFormats.Markdown, KnownFormats.Html, KnownFormats.Docx, KnownFormats.Odt, KnownFormats.Epub, KnownFormats.Txt];

    private static readonly (FileFormat Format, int Priority)[] Outputs =
    [
        (KnownFormats.Docx, 51), (KnownFormats.Html, 53), (KnownFormats.Markdown, 54),
        (KnownFormats.Odt, 55), (KnownFormats.Epub, 56), (KnownFormats.Txt, 57),
    ];

    private readonly IToolLocator _tools;
    private readonly IProcessRunner _runner;

    public PandocConverter(IToolLocator tools, IProcessRunner runner)
    {
        _tools = tools;
        _runner = runner;
        Routes = Outputs.Select(o => new ConversionRoute
        {
            TargetKey = o.Format.Id,
            Label = o.Format.Label,
            Kind = RouteKind.Convert,
            Inputs = Inputs.Where(i => i != o.Format && !(i == KnownFormats.Txt && o.Format == KnownFormats.Markdown))
                .Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase),
            Output = o.Format,
            // Office-to-office conversions prefer LibreOffice (better layout); Pandoc is the fallback.
            Priority = o.Priority + 10,
            Description = $"Convert to {o.Format.DisplayName} with Pandoc",
            Limitations = "Pandoc converts document structure (headings, lists, tables, images, links). Page layout, fonts and styling are not preserved.",
        }).ToList();
    }

    public string Id => "pandoc";
    public string DisplayName => "Pandoc";
    public IReadOnlyList<ConversionRoute> Routes { get; }

    public RouteAvailability GetAvailability(ConversionRoute route) =>
        _tools.Find(ExternalTool.Pandoc) is null ? RouteAvailability.Missing(ExternalTool.Pandoc) : RouteAvailability.Available;

    public async Task<IReadOnlyList<string>> ExecuteAsync(ConversionContext context, CancellationToken cancellationToken)
    {
        var output = context.DefaultOutputPath();
        var produced = await ConvertAsync(context, context.Input, context.Route.Output!, output, cancellationToken).ConfigureAwait(false);
        return produced;
    }

    /// <summary>Converts <paramref name="input"/> into <paramref name="outputPath"/>; also used by the PDF pipeline.</summary>
    internal async Task<IReadOnlyList<string>> ConvertAsync(
        ConversionContext context, InputFile input, FileFormat outputFormat, string outputPath, CancellationToken ct)
    {
        var pandoc = _tools.Find(ExternalTool.Pandoc) ?? throw new ConversionException("Pandoc is not installed.");
        var source = input.Path;
        var reader = ReaderFor(input.Format!);

        if (input.Format == KnownFormats.Txt)
        {
            // Pandoc has no plain-text reader; escape the text so it is reproduced literally.
            source = Path.Combine(context.WorkDirectory, "source.md");
            await File.WriteAllTextAsync(source, EscapePlainText(await File.ReadAllTextAsync(input.Path, ct).ConfigureAwait(false)), Encoding.UTF8, ct)
                .ConfigureAwait(false);
            reader = "markdown";
        }

        var args = new List<string> { "--from", reader, "--to", WriterFor(outputFormat), "--output", outputPath };
        if (!context.Options.Document.AllowLocalResources)
        {
            args.Add("--sandbox");
        }
        if (outputFormat == KnownFormats.Html)
        {
            args.AddRange(["--standalone", "--embed-resources"]);
        }
        if (outputFormat == KnownFormats.Epub || outputFormat == KnownFormats.Html)
        {
            args.AddRange(["--metadata", "pagetitle=" + input.BaseName]);
        }

        string? mediaDir = null;
        if (outputFormat == KnownFormats.Markdown && input.Format != KnownFormats.Html)
        {
            mediaDir = Path.Combine(context.WorkDirectory, Path.GetFileNameWithoutExtension(outputPath) + "_media");
            args.Add("--extract-media=" + Path.GetFileName(mediaDir));
        }
        // "--" ends option parsing so a file name starting with "-" is never read as an option.
        args.AddRange(["--", Path.GetFullPath(source)]);

        var result = await _runner.RunAsync(new ProcessSpec(pandoc, args)
        {
            WorkingDirectory = context.WorkDirectory,
            Timeout = TimeSpan.FromMinutes(10),
        }, ct).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new ConversionException(
                $"Pandoc could not convert the document (exit code {result.ExitCode}): {FirstLine(result.StandardErrorTail)}",
                technicalDetail: result.StandardErrorTail);
        }

        var produced = new List<string> { outputPath };
        if (mediaDir is not null && Directory.Exists(mediaDir) && Directory.EnumerateFileSystemEntries(mediaDir).Any())
        {
            produced.Add(mediaDir);
        }
        if (source != input.Path)
        {
            File.Delete(source);
        }
        return produced;
    }

    internal static string ReaderFor(FileFormat f) => f.Id switch
    {
        "md" => "markdown",
        "html" => "html",
        "docx" => "docx",
        "odt" => "odt",
        "epub" => "epub",
        "txt" => "markdown",
        _ => throw new ConversionException($"Pandoc cannot read {f.Label}."),
    };

    internal static string WriterFor(FileFormat f) => f.Id switch
    {
        "md" => "gfm",
        "html" => "html5",
        "docx" => "docx",
        "odt" => "odt",
        "epub" => "epub3",
        "txt" => "plain",
        _ => throw new ConversionException($"Pandoc cannot write {f.Label}."),
    };

    /// <summary>Escapes Markdown syntax and keeps line breaks, so plain text survives as-is.</summary>
    internal static string EscapePlainText(string text)
    {
        const string special = "\\`*_{}[]()<>#+-.!|~^$&";
        var sb = new StringBuilder(text.Length + text.Length / 8);
        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0)
            {
                sb.Append('\n');
                continue;
            }
            var leading = line.Length - line.TrimStart().Length;
            for (var i = 0; i < leading; i++)
            {
                sb.Append("&nbsp;");
            }
            foreach (var c in line.TrimStart())
            {
                if (special.Contains(c))
                {
                    sb.Append('\\');
                }
                sb.Append(c);
            }
            sb.Append("\\\n"); // hard line break
        }
        return sb.ToString();
    }

    private static string FirstLine(string s)
    {
        var line = s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        return line.Length > 200 ? line[..200] : line;
    }
}
