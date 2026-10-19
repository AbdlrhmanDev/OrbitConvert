using OrbitConvert.Converters.Pdf;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace OrbitConvert.Converters.Tests;

public sealed class PdfEditorTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly PdfEditor _editor = new();

    public void Dispose() => _dir.Dispose();

    private string MakePdf()
    {
        var path = _dir.Combine("pages.pdf");
        using var doc = new PdfDocument();
        for (var i = 0; i < 4; i++)
        {
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(300 + i * 10);
            page.Height = XUnit.FromPoint(400);
        }
        doc.Save(path);
        return path;
    }

    [Fact]
    public async Task Reorders_removes_and_rotates_pages_atomically()
    {
        var input = MakePdf();
        var original = File.ReadAllBytes(input);
        var output = await _editor.SaveAsync(input, new PdfEditRequest([
            new(3), new(1, 90), new(0), new(2, Include: false),
        ]), CancellationToken.None);

        Assert.Equal(original, File.ReadAllBytes(input));
        using var doc = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Equal([330, 310, 300], doc.Pages.Cast<PdfPage>().Select(p => Math.Round(p.Width.Point)));
        Assert.Equal([0, 90, 0], doc.Pages.Cast<PdfPage>().Select(p => p.Rotate));
    }

    [Fact]
    public async Task Rejects_empty_or_duplicate_page_plan()
    {
        var input = MakePdf();
        await Assert.ThrowsAsync<ConversionException>(() => _editor.SaveAsync(input, new PdfEditRequest([]), CancellationToken.None));
        await Assert.ThrowsAsync<ConversionException>(() => _editor.SaveAsync(input,
            new PdfEditRequest([new(0), new(0)]), CancellationToken.None));
    }
}
