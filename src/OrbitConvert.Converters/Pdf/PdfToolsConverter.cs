using ImageMagick;
using OrbitConvert.Converters.Images;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;
using OrbitConvert.Core.Processes;
using OrbitConvert.Core.Tools;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace OrbitConvert.Converters.Pdf;

/// <summary>
/// PDF operations: merge, split, extract pages, rotate, compress, images to PDF and PDF to images.
/// Page manipulation uses PDFsharp (MIT); rendering uses PDFium through PDFtoImage (MIT/BSD-3).
/// Lossy compression optionally uses an installed Ghostscript.
/// </summary>
public sealed class PdfToolsConverter : IConverter
{
    public const string MergeKey = "pdf-merge";
    public const string CombineImagesKey = "pdf-combine-images";
    public const string SplitKey = "pdf-split";
    public const string ExtractKey = "pdf-extract";
    public const string RotateKey = "pdf-rotate";
    public const string CompressKey = "pdf-compress";

    private static readonly IReadOnlySet<string> PdfOnly = ConversionRoute.Formats(KnownFormats.Pdf);

    private readonly IToolLocator _tools;
    private readonly IProcessRunner _runner;

    public PdfToolsConverter(IToolLocator tools, IProcessRunner runner)
    {
        _tools = tools;
        _runner = runner;
        Routes =
        [
            new() { TargetKey = MergeKey, Label = "Merge PDFs", Kind = RouteKind.Combine, Inputs = PdfOnly, MinInputs = 2,
                    Output = KnownFormats.Pdf, Priority = 5, Description = "Combine PDFs into one document, in the selected order" },
            new() { TargetKey = CombineImagesKey, Label = "Combine to PDF", Kind = RouteKind.Combine,
                    Inputs = ConversionRoute.Formats(ImageMagickConverter.RasterInputs.Where(f => f != KnownFormats.Ico).ToArray()),
                    MinInputs = 2, Output = KnownFormats.Pdf, Priority = 6, Description = "One PDF with a page per image" },
            new() { TargetKey = SplitKey, Label = "Split pages", Kind = RouteKind.Extract, Inputs = PdfOnly,
                    Output = KnownFormats.Pdf, Priority = 70, Description = "One PDF per page, in a new folder" },
            new() { TargetKey = ExtractKey, Label = "Extract pages", Kind = RouteKind.Transform, Inputs = PdfOnly,
                    Output = KnownFormats.Pdf, Priority = 71, RequiresOptions = true, Description = "Copy selected pages into a new PDF" },
            new() { TargetKey = RotateKey, Label = "Rotate", Kind = RouteKind.Transform, Inputs = PdfOnly,
                    Output = KnownFormats.Pdf, Priority = 72, Description = "Rotate pages clockwise" },
            new() { TargetKey = CompressKey, Label = "Compress", Kind = RouteKind.Transform, Inputs = PdfOnly,
                    Output = KnownFormats.Pdf, Priority = 73, Description = "Make the PDF smaller",
                    Limitations = "Lossless mode only removes waste and recompresses streams; large savings need Ghostscript's Balanced or Strong mode, which recompresses images." },
            RenderRoute(KnownFormats.Png, 74),
            RenderRoute(KnownFormats.Jpeg, 75),
            RenderRoute(KnownFormats.WebP, 76),
        ];
    }

    private static ConversionRoute RenderRoute(FileFormat format, int priority) => new()
    {
        TargetKey = format.Id,
        Label = format.Label,
        Kind = RouteKind.Extract,
        Inputs = PdfOnly,
        Output = format,
        Priority = priority,
        Description = $"Render each page as {format.Label}",
    };

    public string Id => "pdf-tools";
    public string DisplayName => "PDF tools (PDFsharp, PDFium)";
    public IReadOnlyList<ConversionRoute> Routes { get; }

    public RouteAvailability GetAvailability(ConversionRoute route) => RouteAvailability.Available;

    public Task<IReadOnlyList<string>> ExecuteAsync(ConversionContext context, CancellationToken cancellationToken)
    {
        return context.Route.TargetKey switch
        {
            MergeKey => Run(() => Merge(context, cancellationToken)),
            CombineImagesKey => Run(() => CombineImages(context, cancellationToken)),
            SplitKey => Run(() => Split(context, cancellationToken)),
            ExtractKey => Run(() => ExtractPages(context)),
            RotateKey => Run(() => Rotate(context)),
            CompressKey => CompressAsync(context, cancellationToken),
            _ => Run(() => Render(context, cancellationToken)),
        };

        static Task<IReadOnlyList<string>> Run(Func<IReadOnlyList<string>> work) => Task.Run(() =>
        {
            try
            {
                return work();
            }
            catch (PdfReaderException ex)
            {
                throw new ConversionException(DescribeReadError(ex), ex, ex.Message);
            }
        });
    }

    private static PdfDocument Open(InputFile input, PdfDocumentOpenMode mode)
    {
        try
        {
            return PdfReader.Open(input.Path, mode);
        }
        catch (PdfReaderException ex)
        {
            throw new ConversionException($"{input.FileName}: {DescribeReadError(ex)}", ex, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConversionException($"{input.FileName} could not be read as a PDF: {ex.Message}", ex);
        }
    }

    private static string DescribeReadError(Exception ex) =>
        ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
            ? "Password-protected PDFs are not supported."
            : "The PDF is damaged or uses features that cannot be read.";

    private static IReadOnlyList<string> Merge(ConversionContext context, CancellationToken ct)
    {
        using var output = new PdfDocument();
        var index = 0;
        foreach (var input in context.Inputs)
        {
            ct.ThrowIfCancellationRequested();
            using var source = Open(input, PdfDocumentOpenMode.Import);
            foreach (var page in source.Pages)
            {
                output.AddPage(page);
            }
            context.ReportProgress(0.9 * ++index / context.Inputs.Count);
        }
        var path = context.WorkPath(context.Input.BaseName, ".pdf", "-merged");
        // PDFsharp locks a document once saved, so read everything needed first.
        context.ResultNote = $"{output.PageCount} pages from {context.Inputs.Count} files";
        output.Save(path);
        return [path];
    }

    private static IReadOnlyList<string> Split(ConversionContext context, CancellationToken ct)
    {
        using var source = Open(context.Input, PdfDocumentOpenMode.Import);
        var folder = Path.Combine(context.WorkDirectory, ConversionContext.SanitizeFileName(context.Input.BaseName + "-pages"));
        Directory.CreateDirectory(folder);
        var digits = Math.Max(3, source.PageCount.ToString().Length);
        for (var i = 0; i < source.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var single = new PdfDocument();
            single.AddPage(source.Pages[i]);
            single.Save(Path.Combine(folder, $"{ConversionContext.SanitizeFileName(context.Input.BaseName)}-page-{(i + 1).ToString().PadLeft(digits, '0')}.pdf"));
            context.ReportProgress((double)(i + 1) / source.PageCount);
        }
        context.ResultNote = $"{source.PageCount} files";
        return [folder];
    }

    private static IReadOnlyList<string> ExtractPages(ConversionContext context)
    {
        var range = context.Options.Pdf.PageRange;
        if (string.IsNullOrWhiteSpace(range))
        {
            throw new ConversionException("Enter the pages to extract, for example \"1-3, 5\".");
        }
        using var source = Open(context.Input, PdfDocumentOpenMode.Import);
        var pages = PageRange.Parse(range, source.PageCount);
        using var output = new PdfDocument();
        foreach (var p in pages)
        {
            output.AddPage(source.Pages[p]);
        }
        var path = context.WorkPath(context.Input.BaseName, ".pdf", PageRange.ToSuffix(range));
        output.Save(path);
        context.ResultNote = $"{pages.Count} of {source.PageCount} pages";
        return [path];
    }

    private static IReadOnlyList<string> Rotate(ConversionContext context)
    {
        var degrees = context.Options.Pdf.RotationDegrees;
        if (degrees is not (90 or 180 or 270))
        {
            throw new ConversionException("Rotation must be 90, 180 or 270 degrees.");
        }
        // Work on a copy so the original is never opened for writing.
        var path = context.WorkPath(context.Input.BaseName, ".pdf", "-rotated");
        File.Copy(context.Input.Path, path);
        using (var doc = PdfReader.Open(path, PdfDocumentOpenMode.Modify))
        {
            var pages = PageRange.Parse(context.Options.Pdf.PageRange, doc.PageCount);
            foreach (var p in pages)
            {
                var page = doc.Pages[p];
                page.Rotate = (page.Rotate + degrees) % 360;
            }
            var temp = path + ".tmp";
            doc.Save(temp);
            doc.Close();
            File.Move(temp, path, overwrite: true);
            context.ResultNote = $"{pages.Count} page(s) rotated {degrees}°";
        }
        return [path];
    }

    private async Task<IReadOnlyList<string>> CompressAsync(ConversionContext context, CancellationToken ct)
    {
        var level = context.Options.Pdf.Compression;
        var path = context.WorkPath(context.Input.BaseName, ".pdf", "-compressed");

        if (level == PdfCompressionLevel.Lossless)
        {
            await Task.Run(() =>
            {
                using var source = Open(context.Input, PdfDocumentOpenMode.Import);
                using var output = new PdfDocument();
                output.Options.NoCompression = false;
                output.Options.CompressContentStreams = true;
                output.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
                output.Options.UseFlateDecoderForJpegImages = PdfUseFlateDecoderForJpegImages.Automatic;
                output.Options.EnableCcittCompressionForBilevelImages = true;
                CopyInfo(source, output);
                // Importing pages drops unreferenced objects and incremental-update history.
                foreach (var page in source.Pages)
                {
                    ct.ThrowIfCancellationRequested();
                    output.AddPage(page);
                }
                RecompressStreams(output, ct);
                output.Save(path);
            }, ct).ConfigureAwait(false);
        }
        else
        {
            var gs = _tools.Find(ExternalTool.Ghostscript)
                ?? throw new ConversionException("Balanced and Strong compression require Ghostscript, which was not found. Use Lossless, or install Ghostscript.");
            // Ghostscript treats '%' in -sOutputFile as a format specifier; use a fixed safe name.
            var gsOut = Path.Combine(context.WorkDirectory, "gs-output.pdf");
            var preset = level == PdfCompressionLevel.Strong ? "/screen" : "/ebook";
            var result = await _runner.RunAsync(new ProcessSpec(gs,
            [
                "-dSAFER", "-dBATCH", "-dNOPAUSE", "-dQUIET", "-sDEVICE=pdfwrite", "-dCompatibilityLevel=1.6",
                "-dPDFSETTINGS=" + preset, "-dDetectDuplicateImages=true", "-sOutputFile=" + gsOut, "-f", Path.GetFullPath(context.Input.Path),
            ])
            { WorkingDirectory = context.WorkDirectory, Timeout = TimeSpan.FromMinutes(15) }, ct).ConfigureAwait(false);
            if (!result.Succeeded || !File.Exists(gsOut))
            {
                throw new ConversionException($"Ghostscript could not compress the PDF (exit code {result.ExitCode}).", technicalDetail: result.StandardErrorTail);
            }
            File.Move(gsOut, path);
        }

        var before = context.Input.Length;
        var after = new FileInfo(path).Length;
        if (after >= before)
        {
            throw new ConversionException(level == PdfCompressionLevel.Lossless
                ? "This PDF is already compact; lossless compression could not make it smaller."
                : "Compression did not make this PDF smaller.");
        }
        context.ResultNote = $"Reduced by {100.0 * (before - after) / before:0}%";
        return [path];
    }

    /// <summary>
    /// Imported streams are copied byte for byte, so compress them explicitly: unfiltered streams are
    /// deflated, and plain Flate streams are re-deflated at the best level. Image codecs (DCT, CCITT,
    /// JBIG2, JPX) and predictor-encoded streams are left untouched, so nothing is decoded lossily.
    /// </summary>
    private static void RecompressStreams(PdfDocument document, CancellationToken ct)
    {
        foreach (var obj in document.Internals.GetAllObjects())
        {
            ct.ThrowIfCancellationRequested();
            if (obj is not PdfDictionary { Stream: { } stream } dict || stream.Value.Length == 0)
            {
                continue;
            }
            if (!stream.IsFiltered())
            {
                stream.Zip();
            }
            else if (dict.Elements.GetName("/Filter") == "/FlateDecode" && !dict.Elements.ContainsKey("/DecodeParms")
                     && stream.TryUncompress())
            {
                stream.Zip();
            }
        }
    }

    private static void CopyInfo(PdfDocument source, PdfDocument target)
    {
        target.Info.Title = source.Info.Title;
        target.Info.Author = source.Info.Author;
        target.Info.Subject = source.Info.Subject;
        target.Info.Keywords = source.Info.Keywords;
        target.Info.Creator = source.Info.Creator;
    }

    private static IReadOnlyList<string> CombineImages(ConversionContext context, CancellationToken ct)
    {
        using var document = new PdfDocument();
        var index = 0;
        foreach (var input in context.Inputs)
        {
            ct.ThrowIfCancellationRequested();
            using var stream = PrepareImage(input, out var widthPt, out var heightPt);
            using var image = XImage.FromStream(stream);
            var page = document.AddPage();
            page.Width = XUnit.FromPoint(widthPt);
            page.Height = XUnit.FromPoint(heightPt);
            using (var gfx = XGraphics.FromPdfPage(page))
            {
                gfx.DrawImage(image, 0, 0, widthPt, heightPt);
            }
            context.ReportProgress(0.95 * ++index / context.Inputs.Count);
        }
        var path = context.WorkPath(context.Input.BaseName, ".pdf", context.Inputs.Count > 1 ? "-combined" : null);
        context.ResultNote = $"{document.PageCount} pages";
        document.Save(path);
        return [path];
    }

    /// <summary>
    /// Normalizes an image for embedding: applies EXIF orientation, flattens animation to the first frame,
    /// and encodes as JPEG (for photos) or PNG (lossless). Page size follows the image's DPI (default 96).
    /// </summary>
    private static MemoryStream PrepareImage(InputFile input, out double widthPt, out double heightPt)
    {
        if (input.Format is null)
        {
            throw new ConversionException($"{input.FileName} is not a supported image.");
        }
        try
        {
            using var image = new MagickImage(input.Path, new MagickReadSettings { Format = ImageMagickConverter.ToMagick(input.Format) });
            image.AutoOrient();
            var dpiX = image.Density.X > 1 ? image.Density.X : 96;
            var dpiY = image.Density.Y > 1 ? image.Density.Y : 96;
            if (image.Density.Units == DensityUnit.PixelsPerCentimeter)
            {
                dpiX *= 2.54;
                dpiY *= 2.54;
            }
            widthPt = image.Width * 72.0 / dpiX;
            heightPt = image.Height * 72.0 / dpiY;

            var stream = new MemoryStream();
            if (input.Format == KnownFormats.Jpeg)
            {
                image.Quality = 92;
                image.Write(stream, MagickFormat.Jpeg);
            }
            else
            {
                image.Write(stream, MagickFormat.Png);
            }
            stream.Position = 0;
            return stream;
        }
        catch (MagickException ex)
        {
            throw new ConversionException($"{input.FileName} could not be read as an image.", ex, ex.Message);
        }
    }

    // PDFium is available on Windows, Linux and macOS; the check below guards the calls at run time.
#pragma warning disable CA1416
    private static IReadOnlyList<string> Render(ConversionContext context, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new ConversionException("PDF rendering is not supported on this platform.");
        }
        var format = context.Route.Output!;
        var dpi = Math.Clamp(context.Options.Pdf.ImageDpi, 36, 600);
        var baseName = ConversionContext.SanitizeFileName(context.Input.BaseName);

        using var pdf = File.OpenRead(context.Input.Path);
        int pageCount;
        try
        {
            pageCount = PDFtoImage.Conversion.GetPageCount(pdf, leaveOpen: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ConversionException(DescribeRenderError(ex), ex, ex.Message);
        }

        var pages = PageRange.Parse(context.Options.Pdf.PageRange, pageCount);
        var single = pages.Count == 1;
        var folder = single ? context.WorkDirectory : Path.Combine(context.WorkDirectory, baseName + "-images");
        Directory.CreateDirectory(folder);
        var digits = Math.Max(3, pageCount.ToString().Length);
        var options = new PDFtoImage.RenderOptions(Dpi: dpi, WithAnnotations: true, WithFormFill: true);

        var done = 0;
        foreach (var page in pages)
        {
            ct.ThrowIfCancellationRequested();
            var name = single ? baseName + format.PrimaryExtension : $"{baseName}-page-{(page + 1).ToString().PadLeft(digits, '0')}{format.PrimaryExtension}";
            var target = Path.Combine(folder, name);
            pdf.Position = 0;
            try
            {
                switch (format.Id)
                {
                    case "png":
                        PDFtoImage.Conversion.SavePng(target, pdf, page, leaveOpen: true, options: options);
                        break;
                    case "jpeg":
                        PDFtoImage.Conversion.SaveJpeg(target, pdf, page, leaveOpen: true, options: options with { BackgroundColor = SkiaSharp.SKColors.White });
                        break;
                    default:
                        PDFtoImage.Conversion.SaveWebp(target, pdf, page, leaveOpen: true, options: options);
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new ConversionException(DescribeRenderError(ex), ex, ex.Message);
            }
            context.ReportProgress((double)++done / pages.Count);
        }

        context.ResultNote = single ? null : $"{pages.Count} images at {dpi} dpi";
        return single ? [Directory.EnumerateFiles(folder).Single(f => f.EndsWith(format.PrimaryExtension, StringComparison.OrdinalIgnoreCase))] : [folder];
    }

#pragma warning restore CA1416

    private static string DescribeRenderError(Exception ex) =>
        ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
            ? "Password-protected PDFs are not supported."
            : "The PDF could not be rendered; it may be damaged.";
}
