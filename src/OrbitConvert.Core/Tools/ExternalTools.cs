namespace OrbitConvert.Core.Tools;

public enum ExternalTool
{
    FFmpeg,
    FFprobe,
    Pandoc,
    LibreOffice,
    SevenZip,
    Ghostscript,
}

public sealed record ToolDescriptor(
    ExternalTool Tool,
    string DisplayName,
    string Purpose,
    string License,
    string Homepage,
    bool CanAutoInstall);

public static class ToolCatalog
{
    private static readonly Dictionary<ExternalTool, ToolDescriptor> Descriptors = new()
    {
        [ExternalTool.FFmpeg] = new(ExternalTool.FFmpeg, "FFmpeg", "Video and audio conversion",
            "LGPL-2.1+ / GPL-3.0 (depends on build)", "https://ffmpeg.org/", true),
        [ExternalTool.FFprobe] = new(ExternalTool.FFprobe, "FFprobe", "Media inspection and progress reporting (ships with FFmpeg)",
            "LGPL-2.1+ / GPL-3.0 (depends on build)", "https://ffmpeg.org/", true),
        [ExternalTool.Pandoc] = new(ExternalTool.Pandoc, "Pandoc", "Markdown, HTML, DOCX, ODT and EPUB conversion",
            "GPL-2.0-or-later", "https://pandoc.org/", true),
        [ExternalTool.LibreOffice] = new(ExternalTool.LibreOffice, "LibreOffice", "Office documents to PDF and between office formats",
            "MPL-2.0", "https://www.libreoffice.org/download/", false),
        [ExternalTool.SevenZip] = new(ExternalTool.SevenZip, "7-Zip", "Creating 7z archives",
            "LGPL-2.1 (+ unRAR restriction in full builds)", "https://www.7-zip.org/", true),
        [ExternalTool.Ghostscript] = new(ExternalTool.Ghostscript, "Ghostscript", "Lossy PDF compression",
            "AGPL-3.0", "https://ghostscript.com/releases/gsdnld.html", false),
    };

    public static ToolDescriptor Get(ExternalTool tool) => Descriptors[tool];

    public static IReadOnlyCollection<ToolDescriptor> All => Descriptors.Values;
}

public interface IToolLocator
{
    /// <summary>Returns the full path of the tool executable, or null when it is not installed.</summary>
    string? Find(ExternalTool tool);

    /// <summary>Clears cached lookups after the user installs a tool or changes a path.</summary>
    void Refresh();
}
