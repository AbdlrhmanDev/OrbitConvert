using OrbitConvert.Core.Formats;
using OrbitConvert.Core.Tools;

namespace OrbitConvert.Core.Conversion;

public enum RouteKind
{
    /// <summary>One input becomes one output in a different format.</summary>
    Convert,
    /// <summary>One input is modified and written in the same format (rotate, compress, split).</summary>
    Transform,
    /// <summary>Several inputs become one output (merge PDFs, create archive).</summary>
    Combine,
    /// <summary>One input expands into a folder or several files (extract archive, PDF pages).</summary>
    Extract,
}

/// <summary>
/// A conversion a converter can perform. <see cref="TargetKey"/> is what a user picks
/// ("png", "pdf-merge"); several converters may offer the same key for different inputs.
/// </summary>
public sealed record ConversionRoute
{
    public const string AnyFormat = "*";

    public required string TargetKey { get; init; }
    public required string Label { get; init; }
    public required RouteKind Kind { get; init; }
    public required IReadOnlySet<string> Inputs { get; init; }
    public FileFormat? Output { get; init; }
    public int MinInputs { get; init; } = 1;
    public string? Description { get; init; }
    public string? Limitations { get; init; }
    public bool Experimental { get; init; }

    /// <summary>The route cannot run on defaults (e.g. a page range is needed); quick menus hide it.</summary>
    public bool RequiresOptions { get; init; }

    /// <summary>Lower values appear first in menus.</summary>
    public int Priority { get; init; } = 100;

    public bool AcceptsAnyFormat => Inputs.Contains(AnyFormat);

    public bool Accepts(FileFormat? format) =>
        AcceptsAnyFormat || (format is not null && Inputs.Contains(format.Id));

    public static IReadOnlySet<string> Formats(params FileFormat[] formats) =>
        formats.Select(f => f.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
}

public readonly record struct RouteAvailability(bool IsAvailable, string? Reason, ExternalTool? MissingTool = null)
{
    public static RouteAvailability Available { get; } = new(true, null);

    public static RouteAvailability Unavailable(string reason) => new(false, reason);

    public static RouteAvailability Missing(ExternalTool tool) =>
        new(false, $"Requires {ToolCatalog.Get(tool).DisplayName}, which was not found.", tool);
}
