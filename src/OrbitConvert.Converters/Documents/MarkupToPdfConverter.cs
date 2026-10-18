using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;
using OrbitConvert.Core.Tools;

namespace OrbitConvert.Converters.Documents;

/// <summary>
/// Markdown and EPUB to PDF: Pandoc produces an intermediate DOCX, LibreOffice renders it to PDF.
/// (Pandoc's own PDF output needs a LaTeX installation, which most Windows users do not have.)
/// </summary>
public sealed class MarkupToPdfConverter(PandocConverter pandoc, LibreOfficeConverter libreOffice, IToolLocator tools) : IConverter
{
    public string Id => "pandoc+libreoffice";
    public string DisplayName => "Pandoc + LibreOffice";

    public IReadOnlyList<ConversionRoute> Routes { get; } =
    [
        new ConversionRoute
        {
            TargetKey = KnownFormats.Pdf.Id,
            Label = "PDF",
            Kind = RouteKind.Convert,
            Inputs = ConversionRoute.Formats(KnownFormats.Markdown, KnownFormats.Epub),
            Output = KnownFormats.Pdf,
            Priority = 50,
            Description = "Render to PDF (Pandoc → DOCX → LibreOffice)",
            Limitations = "Uses LibreOffice's default document styles; Markdown has no page layout of its own.",
        },
    ];

    public RouteAvailability GetAvailability(ConversionRoute route)
    {
        if (tools.Find(ExternalTool.Pandoc) is null)
        {
            return RouteAvailability.Missing(ExternalTool.Pandoc);
        }
        return tools.Find(ExternalTool.LibreOffice) is null ? RouteAvailability.Missing(ExternalTool.LibreOffice) : RouteAvailability.Available;
    }

    public async Task<IReadOnlyList<string>> ExecuteAsync(ConversionContext context, CancellationToken cancellationToken)
    {
        var intermediate = Path.Combine(context.WorkDirectory, "intermediate.docx");
        await pandoc.ConvertAsync(context, context.Input, KnownFormats.Docx, intermediate, cancellationToken).ConfigureAwait(false);
        context.ReportProgress(0.4);

        var output = context.DefaultOutputPath();
        await libreOffice.ConvertAsync(intermediate, KnownFormats.Docx, KnownFormats.Pdf, output, context.WorkDirectory, cancellationToken)
            .ConfigureAwait(false);
        File.Delete(intermediate);
        return [output];
    }
}
