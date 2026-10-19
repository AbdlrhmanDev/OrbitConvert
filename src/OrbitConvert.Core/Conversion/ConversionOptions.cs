namespace OrbitConvert.Core.Conversion;

/// <summary>
/// All user-adjustable conversion settings. Each converter reads only the section relevant to it.
/// Instances are mutable for UI binding; the engine always works on a <see cref="Clone"/>.
/// </summary>
public sealed class ConversionOptions
{
    public ImageOptions Image { get; set; } = new();
    public VideoOptions Video { get; set; } = new();
    public AudioOptions Audio { get; set; } = new();
    public PdfOptions Pdf { get; set; } = new();
    public DocumentOptions Document { get; set; } = new();
    public ArchiveOptions Archive { get; set; } = new();

    public ConversionOptions Clone() => new()
    {
        Image = Image.Clone(),
        Video = Video.Clone(),
        Audio = Audio.Clone(),
        Pdf = Pdf.Clone(),
        Document = Document.Clone(),
        Archive = Archive.Clone(),
    };
}

public sealed class ImageOptions
{
    /// <summary>Lossy quality 1-100 (JPEG, WebP, AVIF, HEIC). Ignored by lossless formats.</summary>
    public int Quality { get; set; } = 90;

    /// <summary>Fit inside this box, preserving aspect ratio. Never upscales.</summary>
    public int? MaxWidth { get; set; }
    public int? MaxHeight { get; set; }

    /// <summary>Uniform scale in percent, applied when no max size is set.</summary>
    public int? ScalePercent { get; set; }

    /// <summary>Clockwise rotation in degrees: 0, 90, 180 or 270. EXIF orientation is always applied first.</summary>
    public int Rotation { get; set; }

    /// <summary>Optional crop rectangle in source pixels (after orientation, before resize).</summary>
    public int? CropX { get; set; }
    public int? CropY { get; set; }
    public int? CropWidth { get; set; }
    public int? CropHeight { get; set; }

    /// <summary>Remove EXIF/XMP/IPTC metadata (including GPS location).</summary>
    public bool StripMetadata { get; set; }

    /// <summary>Convert embedded color profiles to sRGB; otherwise the original profile is kept.</summary>
    public bool ConvertToSrgb { get; set; }

    /// <summary>Color used to flatten transparency for formats without alpha (JPEG, BMP).</summary>
    public string BackgroundColor { get; set; } = "#FFFFFF";

    /// <summary>Keep animation frames when both formats support animation (GIF, WebP).</summary>
    public bool KeepAnimation { get; set; } = true;

    public bool HasCrop => CropWidth is > 0 && CropHeight is > 0;

    public ImageOptions Clone() => (ImageOptions)MemberwiseClone();
}

public enum VideoResolution { Original, P2160, P1440, P1080, P720, P480, P360 }

public enum VideoQuality { High, Balanced, Small }

public enum VideoCodec { Auto, H264, H265, VP9, AV1 }

public enum SubtitleMode
{
    /// <summary>Do not include subtitle tracks in the output.</summary>
    DropAll,
    /// <summary>Copy every compatible subtitle track without re-encoding.</summary>
    CopyAll,
    /// <summary>Copy one subtitle track by its zero-based subtitle-stream index.</summary>
    Select,
}

public sealed class VideoOptions
{
    public VideoResolution Resolution { get; set; } = VideoResolution.Original;
    public VideoQuality Quality { get; set; } = VideoQuality.Balanced;
    public VideoCodec Codec { get; set; } = VideoCodec.Auto;

    /// <summary>Output frame rate; null keeps the source rate.</summary>
    public int? FrameRate { get; set; }

    public bool RemoveAudio { get; set; }
    public int AudioBitrateKbps { get; set; } = 160;

    /// <summary>Optional inclusive start position. Null starts at the beginning.</summary>
    public TimeSpan? TrimStart { get; set; }

    /// <summary>Optional exclusive end position. Must be later than <see cref="TrimStart"/>.</summary>
    public TimeSpan? TrimEnd { get; set; }

    public SubtitleMode Subtitles { get; set; } = SubtitleMode.DropAll;
    public int? SubtitleStreamIndex { get; set; }

    public int GifFps { get; set; } = 12;
    public int GifMaxWidth { get; set; } = 480;

    public VideoOptions Clone() => (VideoOptions)MemberwiseClone();

    public static int? ResolutionHeight(VideoResolution r) => r switch
    {
        VideoResolution.P2160 => 2160,
        VideoResolution.P1440 => 1440,
        VideoResolution.P1080 => 1080,
        VideoResolution.P720 => 720,
        VideoResolution.P480 => 480,
        VideoResolution.P360 => 360,
        _ => null,
    };
}

public sealed class AudioOptions
{
    /// <summary>Bitrate for lossy codecs (MP3, AAC, OGG, Opus, M4A).</summary>
    public int BitrateKbps { get; set; } = 192;

    /// <summary>Output sample rate in Hz; null keeps the source rate.</summary>
    public int? SampleRate { get; set; }

    /// <summary>1 = mono, 2 = stereo; null keeps the source layout.</summary>
    public int? Channels { get; set; }

    public TimeSpan? TrimStart { get; set; }
    public TimeSpan? TrimEnd { get; set; }

    public AudioOptions Clone() => (AudioOptions)MemberwiseClone();
}

public enum PdfCompressionLevel
{
    /// <summary>Lossless re-write with maximum stream compression (built in).</summary>
    Lossless,
    /// <summary>Image downsampling to ~150 dpi (requires Ghostscript).</summary>
    Balanced,
    /// <summary>Image downsampling to ~72 dpi (requires Ghostscript).</summary>
    Strong,
}

public enum PdfImageFormat { Png, Jpeg, WebP }

public sealed class PdfOptions
{
    /// <summary>Page selection such as "1-3, 5, 8-". Empty means all pages.</summary>
    public string? PageRange { get; set; }

    /// <summary>Clockwise rotation for the rotate tool: 90, 180 or 270.</summary>
    public int RotationDegrees { get; set; } = 90;

    /// <summary>Render resolution for PDF to image.</summary>
    public int ImageDpi { get; set; } = 150;

    public PdfCompressionLevel Compression { get; set; } = PdfCompressionLevel.Lossless;

    /// <summary>Ghostscript OCR language code, such as eng or ara.</summary>
    public string OcrLanguage { get; set; } = "eng";

    public PdfOptions Clone() => (PdfOptions)MemberwiseClone();
}

public sealed class DocumentOptions
{
    /// <summary>
    /// Allow Pandoc to read local files referenced by the document (e.g. images in Markdown).
    /// Off by default: an untrusted document could otherwise embed arbitrary local files.
    /// </summary>
    public bool AllowLocalResources { get; set; }

    public DocumentOptions Clone() => (DocumentOptions)MemberwiseClone();
}

public enum ArchiveCompression { Fastest, Optimal, Smallest }

public sealed class ArchiveOptions
{
    /// <summary>Base name for created archives; defaults to the input or folder name.</summary>
    public string? ArchiveName { get; set; }

    public ArchiveCompression Compression { get; set; } = ArchiveCompression.Optimal;

    public ArchiveOptions Clone() => (ArchiveOptions)MemberwiseClone();
}
