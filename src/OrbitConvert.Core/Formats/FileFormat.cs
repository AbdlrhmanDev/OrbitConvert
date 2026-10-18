namespace OrbitConvert.Core.Formats;

public enum FormatCategory
{
    Image,
    Video,
    Audio,
    Document,
    Pdf,
    Archive,
}

/// <summary>
/// A file format OrbitConvert understands. <see cref="Id"/> is a stable lowercase key
/// (e.g. "jpeg", "mp4") used in routes, settings and the command line.
/// </summary>
public sealed record FileFormat(
    string Id,
    string DisplayName,
    FormatCategory Category,
    IReadOnlyList<string> Extensions,
    string MimeType)
{
    /// <summary>The extension used when OrbitConvert writes this format, including the dot.</summary>
    public string PrimaryExtension => Extensions[0];

    /// <summary>Short uppercase label suitable for a badge ("JPG", "MP4").</summary>
    public string Label => PrimaryExtension.TrimStart('.').ToUpperInvariant();

    public bool HasExtension(string extension) =>
        Extensions.Any(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase));

    public override string ToString() => Id;
}
