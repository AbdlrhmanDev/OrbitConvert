using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Pdf;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace OrbitConvert.Converters.Pdf;

public sealed class PdfEditor : IPdfEditor
{
    public int ReadPageCount(string inputPath)
    {
        EnsureReadable(inputPath);
        using var document = Open(inputPath);
        return document.PageCount;
    }

    public Task<string> SaveAsync(string inputPath, PdfEditRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => Save(inputPath, request, cancellationToken), cancellationToken);

    public Task<byte[]> RenderThumbnailAsync(string inputPath, int pageIndex, CancellationToken cancellationToken) =>
        Task.Run(() => RenderThumbnail(inputPath, pageIndex, cancellationToken), cancellationToken);

    private static byte[] RenderThumbnail(string inputPath, int pageIndex, CancellationToken ct)
    {
        EnsureReadable(inputPath);
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new ConversionException("PDF thumbnails are not supported on this platform.");
        }
        ct.ThrowIfCancellationRequested();
#pragma warning disable CA1416
        using var pdf = File.OpenRead(inputPath);
        var count = PDFtoImage.Conversion.GetPageCount(pdf, leaveOpen: true);
        if (pageIndex < 0 || pageIndex >= count) throw new ConversionException("The PDF page no longer exists.");
        pdf.Position = 0;
        using var output = new MemoryStream();
        PDFtoImage.Conversion.SavePng(output, pdf, pageIndex, leaveOpen: true,
            options: new PDFtoImage.RenderOptions(Dpi: 48, WithAnnotations: true, WithFormFill: true));
#pragma warning restore CA1416
        return output.ToArray();
    }

    private static string Save(string inputPath, PdfEditRequest request, CancellationToken ct)
    {
        EnsureReadable(inputPath);
        if (request.Pages.Count == 0 || request.Pages.All(p => !p.Include))
        {
            throw new ConversionException("Keep at least one PDF page.");
        }
        var included = request.Pages.Where(p => p.Include).ToArray();
        if (included.Select(p => p.SourceIndex).Distinct().Count() != included.Length)
        {
            throw new ConversionException("Each source page can appear only once.");
        }

        using var source = Open(inputPath);
        foreach (var page in included)
        {
            if (page.SourceIndex < 0 || page.SourceIndex >= source.PageCount)
            {
                throw new ConversionException("The PDF page list is no longer valid.");
            }
            if (page.RotationDegrees is not (0 or 90 or 180 or 270))
            {
                throw new ConversionException("Page rotation must be 0, 90, 180 or 270 degrees.");
            }
        }

        using var output = new PdfDocument();
        foreach (var edit in included)
        {
            ct.ThrowIfCancellationRequested();
            var page = output.AddPage(source.Pages[edit.SourceIndex]);
            page.Rotate = (page.Rotate + edit.RotationDegrees) % 360;
        }
        output.Info.Title = source.Info.Title;
        output.Info.Author = source.Info.Author;

        var directory = Path.GetDirectoryName(Path.GetFullPath(inputPath))!;
        var name = Path.GetFileNameWithoutExtension(inputPath) + "-reordered.pdf";
        var destination = OutputPlanner.ChooseDestination(directory, name, CollisionStrategy.KeepBoth, [Path.GetFullPath(inputPath)])!;
        var temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.partial");
        try
        {
            output.Save(temporary);
            OutputPlanner.Commit(temporary, destination, overwrite: false);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static PdfDocument Open(string path)
    {
        try
        {
            return PdfReader.Open(path, PdfDocumentOpenMode.Import);
        }
        catch (PdfReaderException ex)
        {
            throw new ConversionException(ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
                ? "Password-protected PDFs are not supported."
                : "The PDF is damaged or cannot be read.", ex, ex.Message);
        }
    }

    private static void EnsureReadable(string inputPath)
    {
        if (!File.Exists(inputPath)) throw new ConversionException("The PDF no longer exists.");
    }
}
