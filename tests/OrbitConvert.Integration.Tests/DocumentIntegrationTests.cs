using System.IO.Compression;
using ImageMagick;
using OrbitConvert.Converters.Archives;
using OrbitConvert.Converters.Pdf;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;
using OrbitConvert.Core.Tools;
using OrbitConvert.Infrastructure.Archives;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace OrbitConvert.Integration.Tests;

[Collection("integration")]
public class DocumentIntegrationTests(IntegrationFixture fx)
{
    private const string MarkdownSample = """
        # Release notes

        Some *emphasis*, a [link](https://example.org) and a list:

        - first
        - second

        | Format | Engine |
        |--------|--------|
        | DOCX   | Pandoc |
        """;

    private string Write(string name, string content)
    {
        var path = Path.Combine(fx.NewDir(), name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string DocxText(string docx)
    {
        using var zip = ZipFile.OpenRead(docx);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        return reader.ReadToEnd();
    }

    [SkippableTheory]
    [InlineData("docx")]
    [InlineData("html")]
    [InlineData("odt")]
    [InlineData("epub")]
    [InlineData("txt")]
    public async Task Markdown_converts_with_pandoc(string target)
    {
        Skip.IfNot(fx.Has(ExternalTool.Pandoc), "Pandoc not installed");

        var result = (await fx.ConvertAsync(Write("notes.md", MarkdownSample), target)).AssertSucceeded();

        var output = result.Outputs[0];
        Assert.Equal(KnownFormats.FindById(target), fx.Registry.Analyze(output).Format);
        if (target == "docx")
        {
            Assert.Contains("Release notes", DocxText(output));
        }
        if (target is "html" or "txt")
        {
            Assert.Contains("second", File.ReadAllText(output));
        }
    }

    [SkippableFact]
    public async Task Plain_text_keeps_markdown_like_characters_literally()
    {
        Skip.IfNot(fx.Has(ExternalTool.Pandoc), "Pandoc not installed");

        var result = (await fx.ConvertAsync(Write("plain.txt", "# not a heading\n*literal stars*\n"), "docx")).AssertSucceeded();

        var xml = DocxText(result.Outputs[0]);
        Assert.Contains("# not a heading", xml);
        Assert.Contains("*literal stars*", xml);
    }

    [SkippableFact]
    public async Task Sandbox_blocks_local_file_inclusion_by_default()
    {
        Skip.IfNot(fx.Has(ExternalTool.Pandoc), "Pandoc not installed");
        var dir = fx.NewDir();
        var secret = Path.Combine(dir, "secret.png");
        File.WriteAllBytes(secret, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
        var md = Path.Combine(dir, "doc.md");
        File.WriteAllText(md, $"# Doc\n\n![x]({secret.Replace('\\', '/')})\n");

        var safe = (await fx.ConvertAsync(md, "docx")).AssertSucceeded();
        using (var zip = ZipFile.OpenRead(safe.Outputs[0]))
        {
            Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal));
        }

        var allowed = (await fx.ConvertAsync(md, "docx", new ConversionOptions { Document = { AllowLocalResources = true } })).AssertSucceeded();
        using (var zip = ZipFile.OpenRead(allowed.Outputs[0]))
        {
            Assert.Contains(zip.Entries, e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal));
        }
    }

    [SkippableFact]
    public async Task Office_document_to_pdf_with_libreoffice()
    {
        Skip.IfNot(fx.Has(ExternalTool.Pandoc) && fx.Has(ExternalTool.LibreOffice), "Pandoc and LibreOffice required");
        var docx = (await fx.ConvertAsync(Write("report ü.md", MarkdownSample), "docx")).AssertSucceeded().Outputs[0];

        var pdf = (await fx.ConvertAsync(docx, "pdf")).AssertSucceeded().Outputs[0];

        using var doc = PdfReader.Open(pdf, PdfDocumentOpenMode.Import);
        Assert.True(doc.PageCount >= 1);
        var odt = (await fx.ConvertAsync(docx, "odt")).AssertSucceeded().Outputs[0];
        Assert.Equal(KnownFormats.Odt, fx.Registry.Analyze(odt).Format);
    }

    [SkippableFact]
    public async Task Markdown_to_pdf_uses_the_pandoc_libreoffice_pipeline()
    {
        Skip.IfNot(fx.Has(ExternalTool.Pandoc) && fx.Has(ExternalTool.LibreOffice), "Pandoc and LibreOffice required");

        var result = (await fx.ConvertAsync(Write("notes.md", MarkdownSample), "pdf")).AssertSucceeded();

        Assert.Equal(KnownFormats.Pdf, fx.Registry.Analyze(result.Outputs[0]).Format);
    }

    [SkippableFact]
    public async Task Pdf_to_docx_is_available_as_experimental()
    {
        Skip.IfNot(fx.Has(ExternalTool.Pandoc) && fx.Has(ExternalTool.LibreOffice), "Pandoc and LibreOffice required");
        var docx = (await fx.ConvertAsync(Write("source.md", MarkdownSample), "docx")).AssertSucceeded().Outputs[0];
        var pdf = (await fx.ConvertAsync(docx, "pdf")).AssertSucceeded().Outputs[0];

        var target = fx.Registry.GetTargets(fx.Registry.Analyze([pdf])).Single(t => t.Key == "docx");
        Assert.True(target.Experimental);

        var back = (await fx.ConvertAsync(pdf, "docx")).AssertSucceeded().Outputs[0];
        Assert.Equal(KnownFormats.Docx, fx.Registry.Analyze(back).Format);
        Assert.Contains("Release", DocxText(back));
    }

    [SkippableFact]
    public async Task SevenZip_round_trip()
    {
        Skip.IfNot(fx.Has(ExternalTool.SevenZip), "7-Zip not installed");
        var dir = fx.NewDir();
        var a = Path.Combine(dir, "a.txt");
        var b = Path.Combine(dir, "b ü.txt");
        File.WriteAllText(a, "alpha");
        File.WriteAllText(b, "beta");

        var inputs = fx.Registry.Analyze([a, b]);
        var plan = Assert.Single(fx.Registry.CreatePlans(inputs, "7z", new ConversionOptions { Archive = { ArchiveName = "pack" } }, OutputSettings.BesideOriginal, out _));
        var archive = (await fx.Engine.RunAsync(plan, null, CancellationToken.None)).AssertSucceeded().Outputs[0];

        Assert.Equal(KnownFormats.SevenZip, fx.Registry.Analyze(archive).Format);
        Assert.True(SafeArchiveExtractor.Test(archive).IsValid);
        var folder = (await fx.ConvertAsync(archive, ArchiveConverter.ExtractKey)).AssertSucceeded().Outputs[0];
        Assert.Equal("beta", File.ReadAllText(Path.Combine(folder, "b ü.txt")));
    }

    [SkippableFact]
    public async Task Ghostscript_strong_compression_reduces_an_image_heavy_pdf()
    {
        Skip.IfNot(fx.Has(ExternalTool.Ghostscript), "Ghostscript not installed");
        var dir = fx.NewDir();
        var imagePath = Path.Combine(dir, "photo.jpg");
        using (var image = new MagickImage(MagickColors.Gray, 2400, 2400))
        {
            image.AddNoise(NoiseType.Gaussian);
            image.Quality = 96;
            image.Write(imagePath, MagickFormat.Jpeg);
        }
        var pdfPath = Path.Combine(dir, "scan.pdf");
        using (var document = new PdfDocument())
        {
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(600);
            page.Height = XUnit.FromPoint(600);
            using var graphics = XGraphics.FromPdfPage(page);
            using var source = XImage.FromFile(imagePath);
            graphics.DrawImage(source, 0, 0, 600, 600);
            document.Save(pdfPath);
        }

        var result = (await fx.ConvertAsync(pdfPath, PdfToolsConverter.CompressKey,
            new ConversionOptions { Pdf = { Compression = PdfCompressionLevel.Strong } })).AssertSucceeded();

        var output = Assert.Single(result.Outputs);
        Assert.True(new FileInfo(output).Length < new FileInfo(pdfPath).Length);
        using var compressed = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Single(compressed.Pages);
    }

    [Fact]
    public void Targets_reflect_installed_tools()
    {
        var dir = fx.NewDir();
        var md = Path.Combine(dir, "x.md");
        File.WriteAllText(md, "# x");

        var docx = fx.Registry.GetTargets(fx.Registry.Analyze([md])).Single(t => t.Key == "docx");

        Assert.Equal(fx.Has(ExternalTool.Pandoc), docx.IsAvailable);
        if (!docx.IsAvailable)
        {
            Assert.Contains("Pandoc", docx.UnavailableReason);
        }
    }
}
