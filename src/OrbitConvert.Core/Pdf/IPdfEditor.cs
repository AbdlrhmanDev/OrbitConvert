namespace OrbitConvert.Core.Pdf;

public sealed record PdfPageEdit(int SourceIndex, int RotationDegrees = 0, bool Include = true);

public sealed record PdfEditRequest(IReadOnlyList<PdfPageEdit> Pages);

public interface IPdfEditor
{
    int ReadPageCount(string inputPath);
    Task<byte[]> RenderThumbnailAsync(string inputPath, int pageIndex, CancellationToken cancellationToken);
    Task<string> SaveAsync(string inputPath, PdfEditRequest request, CancellationToken cancellationToken);
}
