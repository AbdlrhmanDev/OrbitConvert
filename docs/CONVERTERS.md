# Converters

Each engine is an `IConverter` registered in `ConvertersServiceCollectionExtensions`. The capability
registry discovers every registered converter automatically.

## Built-in converters

### ImageMagick (`ImageMagickConverter`) — bundled

- Inputs: JPG, PNG, WebP, BMP, TIFF, GIF, HEIC/HEIF, AVIF, ICO. Outputs: the same set minus HEIC, plus PDF.
- Offered outputs are filtered by `MagickNET.SupportedFormats` at run time: the bundled native build
  cannot **write** HEIC, so HEIC appears as unavailable with that reason.
- The ImageMagick coder is chosen from the **detected** format, never from file content hints, so a
  disguised file cannot reach script-like coders (MVG, MSL, SVG, TXT). Resource limits cap dimensions.
- Processing order: EXIF auto-orient → crop → rotate → resize (box fit, never enlarges) or scale →
  optional sRGB conversion → optional metadata strip (keeping the ICC profile) → transparency flattening
  for formats without alpha → quality.
- Animation is kept between GIF and WebP; multi-page TIFF is kept for TIFF and PDF; otherwise the first
  frame (largest frame for ICO) is used. ICO output embeds standard sizes up to the source size.

### FFmpeg (`FfmpegConverter`) — external

- Video → MP4, MKV, MOV, AVI, WebM, GIF, and “Compress” (same container, smaller, fails honestly if
  the result is not smaller). Audio and video → MP3, WAV, FLAC, AAC (ADTS), OGG (Vorbis), M4A, Opus.
- Encoders are discovered with `ffmpeg -encoders`; a target whose encoder is missing is unavailable.
  Defaults: H.264 + AAC (MP4/MKV/MOV), VP9 + Opus (WebM), MPEG-4 Part 2 + MP3 (AVI). User choices: H.265,
  VP9, AV1; an explicit choice never silently falls back to another codec.
- Paths are passed as `file:<absolute path>`, so names like `-y.mp4` or `concat:x` cannot become
  options or protocols. `-nostdin -n` prevent prompts and overwrites.
- Progress comes from `-progress pipe:1`; duration from ffprobe, or from FFmpeg's banner.
- Subtitles and data streams are not copied (first video and first audio stream only).

### Pandoc (`PandocConverter`) — external

- Markdown, HTML, DOCX, ODT, EPUB, TXT → DOCX, HTML, Markdown (GFM), ODT, EPUB, TXT.
- Runs with `--sandbox` unless the user enables “Allow documents to include local images and files”;
  the sandbox stops a document from pulling arbitrary local files into the output.
- Plain text is escaped into Markdown first so `#`, `*` and similar characters stay literal.
- Converts structure (headings, lists, tables, links, images), not page layout or fonts.

### LibreOffice (`LibreOfficeConverter`) — external

- DOCX, DOC, ODT, RTF, TXT, HTML → PDF; DOC/ODT/RTF → DOCX; DOCX/DOC/RTF → ODT; DOC/RTF → TXT.
- **PDF → DOCX is experimental**: LibreOffice's PDF import yields positioned text boxes, not flowing
  text; complex layouts may not survive; scanned PDFs contain no text (there is no OCR).
- Runs headless with a private profile in `%LOCALAPPDATA%\OrbitConvert\libreoffice-profile`;
  conversions are serialized because one profile cannot be shared by concurrent instances.
- The input is staged under an ASCII name; success is judged by the output file existing (LibreOffice
  can exit with 0 after failing).

### Markdown/EPUB → PDF (`MarkupToPdfConverter`)

Pandoc → DOCX → LibreOffice → PDF. Requires both tools. (Pandoc's own PDF output needs LaTeX.)

### PDF tools (`PdfToolsConverter`) — bundled

PDFsharp for page operations, PDFium (via PDFtoImage) for rendering, Magick.NET for image decoding.

| Target key | Kind | Notes |
|---|---|---|
| `pdf-merge` | Combine (2+ PDFs) | Order = selection order (reorderable in the app) |
| `pdf-combine-images` | Combine (2+ images) | Page size follows image DPI (96 if unknown); JPEG kept as JPEG, others embedded losslessly |
| `pdf-split` | Extract | Folder `<name>-pages` with one PDF per page |
| `pdf-extract` | Transform | Needs a page range (`1-3, 7, 9-`); hidden from the radial menu |
| `pdf-rotate` | Transform | Optional page range; 90/180/270° |
| `pdf-compress` | Transform | Lossless: re-imports pages, drops unused objects and recompresses streams (never touches JPEG/JPX/CCITT data). Balanced/Strong: Ghostscript `pdfwrite` `/ebook` or `/screen`. Fails if the result is not smaller |
| `png`, `jpeg`, `webp` | Extract | One image per page (folder when more than one page) at the chosen DPI |

Password-protected PDFs are reported as unsupported.

### Archives (`ArchiveConverter`)

- `zip` (bundled), `7z` (needs 7-Zip): combine any files; duplicate names get “ (1)”.
- `extract`: ZIP and 7z through `SafeArchiveExtractor` (see [SECURITY.md](SECURITY.md#archive-extraction)).
- Integrity check (`ArchiveConverter.Test`) verifies every entry's CRC-32.

## Adding a converter

1. Implement `IConverter`:

   ```csharp
   public sealed class MyConverter(IToolLocator tools, IProcessRunner runner) : IConverter
   {
       public string Id => "mytool";
       public string DisplayName => "My Tool";
       public IReadOnlyList<ConversionRoute> Routes { get; } =
       [
           new() { TargetKey = "xyz", Label = "XYZ", Kind = RouteKind.Convert,
                   Inputs = ConversionRoute.Formats(KnownFormats.Png), Output = MyFormats.Xyz,
                   Description = "…", Limitations = "…" },
       ];

       public RouteAvailability GetAvailability(ConversionRoute route) => …; // check tools/encoders

       public async Task<IReadOnlyList<string>> ExecuteAsync(ConversionContext ctx, CancellationToken ct)
       {
           var output = ctx.DefaultOutputPath();          // inside ctx.WorkDirectory
           await runner.RunAsync(new ProcessSpec(exe, ["--in", ctx.Input.Path, "--out", output]), ct);
           return [output];
       }
   }
   ```

2. Register it in `AddOrbitConvertConverters` (`services.AddSingleton<IConverter, MyConverter>()`).
3. If it needs a new external tool, add it to `ExternalTool`, `ToolCatalog` and `ToolLocator`; if it can
   be downloaded, add a pinned `ToolPackage` with a verified SHA-256.
4. Add a new `FileFormat` to `KnownFormats` and a signature to `FileTypeDetector` if needed.
5. Write tests: unit tests for argument building, and integration tests that skip when the tool is missing.
