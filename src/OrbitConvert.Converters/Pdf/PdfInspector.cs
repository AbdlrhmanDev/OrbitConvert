using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace OrbitConvert.Converters.Pdf;

public sealed record PdfInfo(
    string Path,
    long FileSize,
    int PageCount,
    string Version,
    bool IsEncrypted,
    string? Title,
    string? Author,
    string? Subject,
    string? Keywords,
    string? Creator,
    string? Producer,
    DateTime? Created,
    DateTime? Modified,
    IReadOnlyList<(double WidthPt, double HeightPt)> PageSizes,
    string? Error = null);

/// <summary>Reads PDF metadata without modifying the file.</summary>
public static class PdfInspector
{
    public static PdfInfo Inspect(string path)
    {
        var size = new FileInfo(path).Length;
        try
        {
            using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            var info = doc.Info;
            return new PdfInfo(path, size, doc.PageCount, $"{doc.Version / 10}.{doc.Version % 10}", false,
                NullIfEmpty(info.Title), NullIfEmpty(info.Author), NullIfEmpty(info.Subject), NullIfEmpty(info.Keywords),
                NullIfEmpty(info.Creator), NullIfEmpty(info.Producer),
                ValidDate(info.CreationDate), ValidDate(info.ModificationDate),
                PageSizes(path));
        }
        catch (PdfReaderException ex) when (ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            return Empty(path, size, encrypted: true, "This PDF is password-protected.");
        }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or IOException)
        {
            return Empty(path, size, encrypted: false, "The PDF could not be read: " + ex.Message);
        }
    }

    private static IReadOnlyList<(double, double)> PageSizes(string path)
    {
        try
        {
            using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            return doc.Pages.Cast<PdfPage>().Take(1000).Select(p => (p.Width.Point, p.Height.Point)).ToList();
        }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException)
        {
            return [];
        }
    }

    private static PdfInfo Empty(string path, long size, bool encrypted, string error) =>
        new(path, size, 0, "?", encrypted, null, null, null, null, null, null, null, null, [], error);

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static DateTime? ValidDate(DateTime d) => d == DateTime.MinValue ? null : d;
}
