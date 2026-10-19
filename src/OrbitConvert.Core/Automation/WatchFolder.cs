namespace OrbitConvert.Core.Automation;

/// <summary>
/// "When a matching file appears in <see cref="Path"/>, run preset <see cref="PresetId"/>."
/// Watchers are off until the user turns them on.
/// </summary>
public sealed class WatchFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Path { get; set; } = "";
    public bool Enabled { get; set; }

    /// <summary>Extensions to react to (".heic", ".png"); empty means every file the preset accepts.</summary>
    public List<string> Extensions { get; set; } = [];

    public string PresetId { get; set; } = "";

    /// <summary>Destination folder; null saves beside the original.</summary>
    public string? OutputFolder { get; set; }

    public int ProcessedCount { get; set; }
    public DateTimeOffset? LastProcessed { get; set; }
    public string? LastError { get; set; }

    /// <summary>True when <paramref name="file"/> should trigger this watcher (extension filter only).</summary>
    public bool Matches(string file)
    {
        if (Extensions.Count == 0)
        {
            return true;
        }
        var ext = System.IO.Path.GetExtension(file);
        return Extensions.Any(e => string.Equals(Normalize(e), ext, StringComparison.OrdinalIgnoreCase));
    }

    public static string Normalize(string extension)
    {
        var e = extension.Trim().TrimStart('*');
        return e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant();
    }

    /// <summary>Parses user input such as ".heic, png; *.jpg".</summary>
    public static List<string> ParseExtensions(string? text) =>
        (text ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize).Where(e => e.Length > 1).Distinct().ToList();

    /// <summary>
    /// Ignores files that are still being written or are temporary: partial downloads, Office lock
    /// files, hidden or system files, and OrbitConvert's own in-progress copies.
    /// </summary>
    public static bool IsTransient(string file)
    {
        var name = System.IO.Path.GetFileName(file);
        if (name.Length == 0 || name.StartsWith("~$", StringComparison.Ordinal) || name.StartsWith('.'))
        {
            return true;
        }
        string[] transient = [".tmp", ".partial", ".crdownload", ".part", ".download", ".opdownload"];
        return transient.Any(t => name.EndsWith(t, StringComparison.OrdinalIgnoreCase));
    }
}
