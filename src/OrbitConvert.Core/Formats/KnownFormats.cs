namespace OrbitConvert.Core.Formats;

public static class KnownFormats
{
    // Images
    public static readonly FileFormat Jpeg = new("jpeg", "JPEG image", FormatCategory.Image, [".jpg", ".jpeg", ".jpe", ".jfif"], "image/jpeg");
    public static readonly FileFormat Png = new("png", "PNG image", FormatCategory.Image, [".png"], "image/png");
    public static readonly FileFormat WebP = new("webp", "WebP image", FormatCategory.Image, [".webp"], "image/webp");
    public static readonly FileFormat Bmp = new("bmp", "Bitmap image", FormatCategory.Image, [".bmp", ".dib"], "image/bmp");
    public static readonly FileFormat Tiff = new("tiff", "TIFF image", FormatCategory.Image, [".tiff", ".tif"], "image/tiff");
    public static readonly FileFormat Gif = new("gif", "GIF image", FormatCategory.Image, [".gif"], "image/gif");
    public static readonly FileFormat Heic = new("heic", "HEIC/HEIF image", FormatCategory.Image, [".heic", ".heif", ".hif"], "image/heic");
    public static readonly FileFormat Avif = new("avif", "AVIF image", FormatCategory.Image, [".avif"], "image/avif");
    public static readonly FileFormat Ico = new("ico", "Windows icon", FormatCategory.Image, [".ico"], "image/x-icon");

    // Video
    public static readonly FileFormat Mp4 = new("mp4", "MP4 video", FormatCategory.Video, [".mp4", ".m4v"], "video/mp4");
    public static readonly FileFormat Mkv = new("mkv", "Matroska video", FormatCategory.Video, [".mkv"], "video/x-matroska");
    public static readonly FileFormat Mov = new("mov", "QuickTime video", FormatCategory.Video, [".mov", ".qt"], "video/quicktime");
    public static readonly FileFormat Avi = new("avi", "AVI video", FormatCategory.Video, [".avi"], "video/x-msvideo");
    public static readonly FileFormat WebM = new("webm", "WebM video", FormatCategory.Video, [".webm"], "video/webm");

    // Audio
    public static readonly FileFormat Mp3 = new("mp3", "MP3 audio", FormatCategory.Audio, [".mp3"], "audio/mpeg");
    public static readonly FileFormat Wav = new("wav", "WAV audio", FormatCategory.Audio, [".wav"], "audio/wav");
    public static readonly FileFormat Flac = new("flac", "FLAC audio", FormatCategory.Audio, [".flac"], "audio/flac");
    public static readonly FileFormat Aac = new("aac", "AAC audio (ADTS)", FormatCategory.Audio, [".aac"], "audio/aac");
    public static readonly FileFormat Ogg = new("ogg", "Ogg Vorbis audio", FormatCategory.Audio, [".ogg", ".oga"], "audio/ogg");
    public static readonly FileFormat M4a = new("m4a", "M4A audio", FormatCategory.Audio, [".m4a"], "audio/mp4");
    public static readonly FileFormat Opus = new("opus", "Opus audio", FormatCategory.Audio, [".opus"], "audio/opus");

    // Documents
    public static readonly FileFormat Docx = new("docx", "Word document", FormatCategory.Document, [".docx"], "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
    public static readonly FileFormat Doc = new("doc", "Word 97-2003 document", FormatCategory.Document, [".doc"], "application/msword");
    public static readonly FileFormat Odt = new("odt", "OpenDocument text", FormatCategory.Document, [".odt"], "application/vnd.oasis.opendocument.text");
    public static readonly FileFormat Rtf = new("rtf", "Rich Text Format", FormatCategory.Document, [".rtf"], "application/rtf");
    public static readonly FileFormat Txt = new("txt", "Plain text", FormatCategory.Document, [".txt", ".text", ".log"], "text/plain");
    public static readonly FileFormat Html = new("html", "HTML page", FormatCategory.Document, [".html", ".htm", ".xhtml"], "text/html");
    public static readonly FileFormat Markdown = new("md", "Markdown", FormatCategory.Document, [".md", ".markdown", ".mdown"], "text/markdown");
    public static readonly FileFormat Epub = new("epub", "EPUB e-book", FormatCategory.Document, [".epub"], "application/epub+zip");

    // PDF
    public static readonly FileFormat Pdf = new("pdf", "PDF document", FormatCategory.Pdf, [".pdf"], "application/pdf");

    // Archives
    public static readonly FileFormat Zip = new("zip", "ZIP archive", FormatCategory.Archive, [".zip"], "application/zip");
    public static readonly FileFormat SevenZip = new("7z", "7-Zip archive", FormatCategory.Archive, [".7z"], "application/x-7z-compressed");

    public static IReadOnlyList<FileFormat> All { get; } =
    [
        Jpeg, Png, WebP, Bmp, Tiff, Gif, Heic, Avif, Ico,
        Mp4, Mkv, Mov, Avi, WebM,
        Mp3, Wav, Flac, Aac, Ogg, M4a, Opus,
        Docx, Doc, Odt, Rtf, Txt, Html, Markdown, Epub,
        Pdf,
        Zip, SevenZip,
    ];

    private static readonly Dictionary<string, FileFormat> ById =
        All.ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, FileFormat> ByExtension = BuildExtensionMap();

    private static Dictionary<string, FileFormat> BuildExtensionMap()
    {
        var map = new Dictionary<string, FileFormat>(StringComparer.OrdinalIgnoreCase);
        foreach (var format in All)
        {
            foreach (var ext in format.Extensions)
            {
                map.TryAdd(ext, format);
            }
        }
        return map;
    }

    public static FileFormat? FindById(string? id) =>
        id is not null && ById.TryGetValue(id.Trim().TrimStart('.'), out var f) ? f : FindByAlias(id);

    public static FileFormat? FindByExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        return ByExtension.GetValueOrDefault(ext);
    }

    public static FileFormat? FindByPath(string path) => FindByExtension(Path.GetExtension(path));

    /// <summary>Resolves user-typed names such as "jpg", "htm" or "markdown".</summary>
    private static FileFormat? FindByAlias(string? id) => id is null ? null : FindByExtension(id.Trim());
}
