using Microsoft.Extensions.Logging;
using OrbitConvert.Core.Formats;

namespace OrbitConvert.Core.Conversion;

/// <summary>A conversion engine plug-in (ImageMagick, FFmpeg, Pandoc, ...).</summary>
public interface IConverter
{
    string Id { get; }
    string DisplayName { get; }
    IReadOnlyList<ConversionRoute> Routes { get; }

    /// <summary>Checks at runtime whether the route can run (engine installed, encoder present, ...).</summary>
    RouteAvailability GetAvailability(ConversionRoute route);

    /// <summary>
    /// Performs the conversion. Implementations must write every output inside
    /// <see cref="ConversionContext.WorkDirectory"/> and return the paths they produced;
    /// the engine validates and moves them to the final location.
    /// </summary>
    Task<IReadOnlyList<string>> ExecuteAsync(ConversionContext context, CancellationToken cancellationToken);
}

public sealed record InputFile(string Path, FileFormat? Format, long Length, DetectionResult? Detection = null)
{
    public string FileName => System.IO.Path.GetFileName(Path);
    public string BaseName => System.IO.Path.GetFileNameWithoutExtension(Path);
}

public sealed class ConversionContext
{
    public required ConversionRoute Route { get; init; }
    public required IReadOnlyList<InputFile> Inputs { get; init; }
    public required ConversionOptions Options { get; init; }
    public required string WorkDirectory { get; init; }
    public required ILogger Logger { get; init; }
    public IProgress<double>? Progress { get; init; }

    public InputFile Input => Inputs[0];

    /// <summary>Optional human-readable note for the result, e.g. "Reduced by 34%".</summary>
    public string? ResultNote { get; set; }

    /// <summary>Builds a path inside the work directory: "<c>{base}{suffix}{ext}</c>".</summary>
    public string WorkPath(string baseName, string extension, string? suffix = null)
    {
        var safe = SanitizeFileName(baseName + (suffix ?? string.Empty));
        return System.IO.Path.Combine(WorkDirectory, safe + extension);
    }

    /// <summary>Default output path for single-output routes.</summary>
    public string DefaultOutputPath(string? suffix = null) =>
        WorkPath(Input.BaseName, Route.Output?.PrimaryExtension ?? System.IO.Path.GetExtension(Input.Path), suffix);

    public void ReportProgress(double fraction) => Progress?.Report(Math.Clamp(fraction, 0, 1));

    public static string SanitizeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars).Trim().TrimEnd('.');
        return result.Length == 0 ? "output" : result;
    }
}
