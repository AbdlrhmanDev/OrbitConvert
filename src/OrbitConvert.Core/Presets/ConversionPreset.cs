using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;

namespace OrbitConvert.Core.Presets;

/// <summary>
/// A saved conversion: a target plus its options. Starred presets appear in the Orbit Menu for
/// files they apply to, and presets drive folder automation.
/// </summary>
public sealed class ConversionPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public required string Name { get; set; }

    /// <summary>A conversion target key from the capability registry ("webp", "mp4", "image-strip").</summary>
    public required string TargetKey { get; set; }

    /// <summary>Short mono tag shown on cards ("IMG → WEBP").</summary>
    public string Tag { get; set; } = "";

    /// <summary>Short uppercase label for an Orbit Menu segment (at most 8 characters).</summary>
    public string MenuLabel { get; set; } = "";

    /// <summary>File category whose hue colors the tag: Image, Video, Audio, Document, Pdf or Archive.</summary>
    public string Category { get; set; } = "Other";

    /// <summary>Plain-language parameter summary; must describe only what the options really do.</summary>
    public string Summary { get; set; } = "";

    /// <summary>Input categories the preset is meant for; empty means any file its target accepts.</summary>
    public List<FormatCategory> AppliesTo { get; set; } = [];

    public bool Starred { get; set; }
    public bool BuiltIn { get; set; }
    public ConversionOptions Options { get; set; } = new();

    /// <summary>True when every input belongs to a category this preset is meant for.</summary>
    public bool IsFor(IEnumerable<FormatCategory?> inputCategories) =>
        AppliesTo.Count == 0 || inputCategories.All(c => c is { } category && AppliesTo.Contains(category));

    public ConversionPreset Clone() => new()
    {
        Id = Id, Name = Name, TargetKey = TargetKey, Tag = Tag, MenuLabel = MenuLabel, Category = Category,
        Summary = Summary, AppliesTo = [.. AppliesTo], Starred = Starred, BuiltIn = BuiltIn, Options = Options.Clone(),
    };
}

public static class BuiltInPresets
{
    public static List<ConversionPreset> Create()
    {
        var web = new ConversionOptions();
        web.Image.Quality = 80;
        web.Image.MaxWidth = 2560;
        web.Image.MaxHeight = 2560;
        web.Image.StripMetadata = true;
        web.Image.ConvertToSrgb = true;

        var whatsapp = new ConversionOptions();
        whatsapp.Video.Resolution = VideoResolution.P720;
        whatsapp.Video.Codec = VideoCodec.H264;
        whatsapp.Video.Quality = VideoQuality.Balanced;
        whatsapp.Video.AudioBitrateKbps = 128;

        var audio = new ConversionOptions();
        audio.Audio.BitrateKbps = 192;

        var email = new ConversionOptions();
        email.Image.Quality = 70;
        email.Image.MaxWidth = 1600;
        email.Image.MaxHeight = 1600;
        email.Image.StripMetadata = true;

        return
        [
            new() { Id = "builtin-web", Name = "Optimize for web", TargetKey = "webp", Tag = "IMG → WEBP", MenuLabel = "WEB",
                    Category = "Image", Summary = "quality 80 · max 2560 px\nstrip EXIF · sRGB", AppliesTo = [FormatCategory.Image],
                    Starred = true, BuiltIn = true, Options = web },
            new() { Id = "builtin-whatsapp", Name = "Video for WhatsApp", TargetKey = "mp4", Tag = "MP4 · 720p", MenuLabel = "SHARE",
                    Category = "Video", Summary = "H.264 · 720p · balanced\nAAC 128k · fast start", AppliesTo = [FormatCategory.Video],
                    Starred = true, BuiltIn = true, Options = whatsapp },
            new() { Id = "builtin-audio", Name = "Extract audio", TargetKey = "mp3", Tag = "VID → MP3", MenuLabel = "AUDIO",
                    Category = "Audio", Summary = "MP3 · 192 kbps\nkeeps tags", AppliesTo = [FormatCategory.Video, FormatCategory.Audio],
                    BuiltIn = true, Options = audio },
            new() { Id = "builtin-docpdf", Name = "Document to PDF", TargetKey = "pdf", Tag = "DOC → PDF", MenuLabel = "PDF",
                    Category = "Pdf", Summary = "LibreOffice, headless\nlayout as in the document", AppliesTo = [FormatCategory.Document],
                    Starred = true, BuiltIn = true, Options = new() },
            new() { Id = "builtin-email", Name = "Compress for email", TargetKey = "jpeg", Tag = "IMG → JPG", MenuLabel = "EMAIL",
                    Category = "Image", Summary = "quality 70 · max 1600 px\nstrip EXIF", AppliesTo = [FormatCategory.Image],
                    BuiltIn = true, Options = email },
            new() { Id = "builtin-clean", Name = "Remove photo metadata", TargetKey = "image-strip", Tag = "IMG", MenuLabel = "CLEAN",
                    Category = "Image", Summary = "same format\nstrip EXIF, GPS, XMP", AppliesTo = [FormatCategory.Image],
                    BuiltIn = true, Options = new() },
        ];
    }
}
