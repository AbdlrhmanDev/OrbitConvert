using ImageMagick;
using OrbitConvert.Converters.Pdf;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace OrbitConvert.Converters.Tests;

/// <summary>Real PDF operations using PDFsharp and PDFium; no external tools needed.</summary>
public class PdfToolsTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly PdfToolsConverter _converter = new(new NoTools(), new NeverRun());

    public void Dispose() => _dir.Dispose();

    /// <summary>Creates a PDF with distinct page sizes (A4, Letter, ...) so page order can be verified.</summary>
    private string MakePdf(string name, int pages, bool compress = true)
    {
        using var doc = new PdfDocument();
        doc.Options.NoCompression = !compress;
        doc.Options.CompressContentStreams = compress;
        for (var i = 0; i < pages; i++)
        {
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(300 + i * 10);
            page.Height = XUnit.FromPoint(400);
            using var gfx = XGraphics.FromPdfPage(page);
            for (var y = 0; y < 400; y += 4)
            {
                gfx.DrawLine(XPens.SteelBlue, 0, y, page.Width.Point, (y * 7) % 400);
            }
        }
        var path = _dir.Combine(name);
        doc.Save(path);
        return path;
    }

    private static int PageCount(string path)
    {
        using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return doc.PageCount;
    }

    private static double[] Widths(string path)
    {
        using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return doc.Pages.Cast<PdfPage>().Select(p => Math.Round(p.Width.Point)).ToArray();
    }

    [Fact]
    public async Task Merges_in_the_given_order()
    {
        var a = MakePdf("a.pdf", 2);
        var b = MakePdf("b.pdf", 3);

        var result = (await Run.Convert(_converter, PdfToolsConverter.MergeKey, [b, a])).AssertSucceeded();

        var merged = Assert.Single(result.Outputs);
        Assert.EndsWith("b-merged.pdf", merged);
        Assert.Equal([300, 310, 320, 300, 310], Widths(merged));
        Assert.Equal("5 pages from 2 files", result.Note);
    }

    [Fact]
    public async Task Splits_into_a_folder_of_single_page_pdfs()
    {
        var result = (await Run.Convert(_converter, PdfToolsConverter.SplitKey, [MakePdf("doc.pdf", 3)])).AssertSucceeded();

        var folder = Assert.Single(result.Outputs);
        var files = Directory.GetFiles(folder).Order().ToArray();
        Assert.Equal(3, files.Length);
        Assert.EndsWith("doc-page-001.pdf", files[0]);
        Assert.All(files, f => Assert.Equal(1, PageCount(f)));
    }

    [Fact]
    public async Task Extracts_a_page_range()
    {
        var options = new ConversionOptions { Pdf = { PageRange = "2-3" } };
        var result = (await Run.Convert(_converter, PdfToolsConverter.ExtractKey, [MakePdf("doc.pdf", 4)], options)).AssertSucceeded();

        Assert.Equal([310, 320], Widths(result.Outputs[0]));
        Assert.EndsWith("doc-pages-2-3.pdf", result.Outputs[0]);
    }

    [Fact]
    public async Task Extract_requires_a_page_range()
    {
        var result = await Run.Convert(_converter, PdfToolsConverter.ExtractKey, [MakePdf("doc.pdf", 2)]);
        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Contains("Enter the pages", result.Error);
    }

    [Fact]
    public async Task Rotates_selected_pages_without_touching_the_original()
    {
        var input = MakePdf("doc.pdf", 3);
        var before = File.ReadAllBytes(input);
        var options = new ConversionOptions { Pdf = { RotationDegrees = 90, PageRange = "2" } };

        var result = (await Run.Convert(_converter, PdfToolsConverter.RotateKey, [input], options)).AssertSucceeded();

        using var doc = PdfReader.Open(result.Outputs[0], PdfDocumentOpenMode.Import);
        Assert.Equal([0, 90, 0], doc.Pages.Cast<PdfPage>().Select(p => p.Rotate));
        Assert.Equal(before, File.ReadAllBytes(input));
    }

    [Fact]
    public async Task Lossless_compression_shrinks_an_uncompressed_pdf()
    {
        var input = MakePdf("fat.pdf", 3, compress: false);

        var result = (await Run.Convert(_converter, PdfToolsConverter.CompressKey, [input])).AssertSucceeded();

        Assert.True(new FileInfo(result.Outputs[0]).Length < new FileInfo(input).Length);
        Assert.StartsWith("Reduced by", result.Note);
        Assert.Equal(3, PageCount(result.Outputs[0]));

        // The recompressed streams must still decode: render every page with PDFium.
        using var pdf = File.OpenRead(result.Outputs[0]);
        for (var page = 0; page < 3; page++)
        {
            pdf.Position = 0;
            using var bitmap = PDFtoImage.Conversion.ToImage(pdf, page, leaveOpen: true);
            Assert.True(bitmap.Width > 0);
        }
    }

    [Fact]
    public async Task Compression_that_cannot_help_fails_honestly()
    {
        var once = (await Run.Convert(_converter, PdfToolsConverter.CompressKey, [MakePdf("fat.pdf", 2, compress: false)])).AssertSucceeded();

        var again = await Run.Convert(_converter, PdfToolsConverter.CompressKey, [once.Outputs[0]]);

        Assert.Equal(ConversionStatus.Failed, again.Status);
        Assert.Contains("could not make it smaller", again.Error);
    }

    [Fact]
    public async Task Lossy_compression_without_ghostscript_explains_what_is_missing()
    {
        var options = new ConversionOptions { Pdf = { Compression = PdfCompressionLevel.Strong } };
        var result = await Run.Convert(_converter, PdfToolsConverter.CompressKey, [MakePdf("doc.pdf", 1)], options);

        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Contains("Ghostscript", result.Error);
    }

    [Fact]
    public async Task Combines_images_into_one_pdf_page_each()
    {
        var images = new[] { (300u, 200u, "a.png"), (100u, 400u, "b.jpg") }.Select(x =>
        {
            using var img = new MagickImage(MagickColors.Teal, x.Item1, x.Item2) { Density = new Density(96) };
            var path = _dir.Combine(x.Item3);
            img.Write(path);
            return path;
        }).ToArray();

        var result = (await Run.Convert(_converter, PdfToolsConverter.CombineImagesKey, images)).AssertSucceeded();

        using var doc = PdfReader.Open(result.Outputs[0], PdfDocumentOpenMode.Import);
        Assert.Equal(2, doc.PageCount);
        Assert.Equal(225, Math.Round(doc.Pages[0].Width.Point)); // 300 px at 96 dpi = 225 pt
        Assert.Equal(300, Math.Round(doc.Pages[1].Height.Point));
    }

    [Fact]
    public async Task Renders_pages_to_images()
    {
        var options = new ConversionOptions { Pdf = { ImageDpi = 72 } };
        var multi = (await Run.Convert(_converter, "png", [MakePdf("doc.pdf", 2)], options)).AssertSucceeded();
        var folder = Assert.Single(multi.Outputs);
        var pngs = Directory.GetFiles(folder, "*.png").Order().ToArray();
        Assert.Equal(2, pngs.Length);
        using (var first = new MagickImage(pngs[0]))
        {
            Assert.Equal(300u, first.Width); // 300 pt at 72 dpi
        }

        var single = (await Run.Convert(_converter, "jpeg", [MakePdf("one.pdf", 1)], options)).AssertSucceeded();
        Assert.Equal(KnownFormats.Jpeg, Run.Detect(Assert.Single(single.Outputs)));
    }

    [Fact]
    public async Task Password_protected_pdfs_are_reported_clearly()
    {
        var path = _dir.Combine("secret.pdf");
        using (var doc = new PdfDocument())
        {
            doc.AddPage();
            doc.SecuritySettings.UserPassword = "pw";
            doc.SecuritySettings.OwnerPassword = "owner";
            doc.Save(path);
        }

        var result = await Run.Convert(_converter, PdfToolsConverter.SplitKey, [path]);
        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Contains("Password-protected", result.Error);

        var info = PdfInspector.Inspect(path);
        Assert.True(info.IsEncrypted);
    }

    [Fact]
    public void Inspects_metadata()
    {
        var path = _dir.Combine("meta.pdf");
        using (var doc = new PdfDocument())
        {
            doc.Info.Title = "Quarterly report";
            doc.Info.Author = "Ada";
            doc.AddPage();
            doc.AddPage();
            doc.Save(path);
        }

        var info = PdfInspector.Inspect(path);

        Assert.Null(info.Error);
        Assert.Equal(2, info.PageCount);
        Assert.Equal("Quarterly report", info.Title);
        Assert.Equal("Ada", info.Author);
        Assert.Equal(2, info.PageSizes.Count);
    }
}
