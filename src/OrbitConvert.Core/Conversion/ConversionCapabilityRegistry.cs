using OrbitConvert.Core.Formats;
using OrbitConvert.Core.Tools;

namespace OrbitConvert.Core.Conversion;

/// <summary>A target a user can choose for a given selection of files.</summary>
public sealed record ConversionTarget(
    string Key,
    string Label,
    RouteKind Kind,
    FileFormat? Output,
    bool IsAvailable,
    string? UnavailableReason,
    string? Description,
    string? Limitations,
    bool Experimental,
    int Priority,
    IReadOnlyList<ExternalTool> MissingTools,
    bool RequiresOptions = false)
{
    public FormatCategory? Category => Output?.Category;
}

/// <summary>A concrete unit of work: one converter route applied to one or more inputs.</summary>
public sealed record ConversionPlan(
    IConverter Converter,
    ConversionRoute Route,
    IReadOnlyList<InputFile> Inputs,
    ConversionOptions Options,
    OutputSettings Output)
{
    public string Title => Route.Kind == RouteKind.Combine && Inputs.Count > 1
        ? $"{Inputs.Count} files → {Route.Label}"
        : $"{Inputs[0].FileName} → {Route.Label}";
}

/// <summary>
/// Knows every route of every installed converter, identifies inputs by content,
/// and answers "what can these files become?" — offering only real, available conversions.
/// </summary>
public sealed class ConversionCapabilityRegistry
{
    private readonly IReadOnlyList<IConverter> _converters;
    private readonly IFileTypeDetector _detector;

    public ConversionCapabilityRegistry(IEnumerable<IConverter> converters, IFileTypeDetector detector)
    {
        _converters = converters.ToList();
        _detector = detector;
    }

    public IReadOnlyList<IConverter> Converters => _converters;

    public IEnumerable<(IConverter Converter, ConversionRoute Route)> AllRoutes =>
        _converters.SelectMany(c => c.Routes.Select(r => (c, r)));

    /// <summary>Identifies a file by signature. Never throws for unreadable files; the error is in the warning.</summary>
    public InputFile Analyze(string path)
    {
        var full = Path.GetFullPath(path);
        try
        {
            var info = new FileInfo(full);
            if (!info.Exists)
            {
                return new InputFile(full, null, 0, DetectionResult.Unknown("The file does not exist."));
            }
            var detection = _detector.Detect(full);
            return new InputFile(full, detection.Format, info.Length, detection);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new InputFile(full, null, 0, DetectionResult.Unknown($"The file could not be read: {ex.Message}"));
        }
    }

    public IReadOnlyList<InputFile> Analyze(IEnumerable<string> paths) => paths.Select(Analyze).ToList();

    public IReadOnlyList<ConversionTarget> GetTargets(IReadOnlyList<InputFile> inputs)
    {
        if (inputs.Count == 0)
        {
            return [];
        }

        var targets = new List<ConversionTarget>();
        var perFileKinds = AllRoutes.Where(x => x.Route.Kind != RouteKind.Combine).GroupBy(x => x.Route.TargetKey);

        foreach (var group in perFileKinds)
        {
            var target = EvaluatePerFile(group.Key, group.ToList(), inputs);
            if (target is not null)
            {
                targets.Add(target);
            }
        }

        foreach (var (converter, route) in AllRoutes.Where(x => x.Route.Kind == RouteKind.Combine))
        {
            if (inputs.Count < route.MinInputs || !inputs.All(i => route.Accepts(i.Format)))
            {
                continue;
            }
            if (targets.Any(t => t.Key == route.TargetKey))
            {
                continue;
            }
            var availability = converter.GetAvailability(route);
            targets.Add(ToTarget(route, availability.IsAvailable, availability.Reason,
                availability.MissingTool is { } t ? [t] : []));
        }

        return targets
            .OrderByDescending(t => t.IsAvailable)
            .ThenBy(t => t.Priority)
            .ThenBy(t => t.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ConversionTarget? EvaluatePerFile(
        string key, List<(IConverter Converter, ConversionRoute Route)> candidates, IReadOnlyList<InputFile> inputs)
    {
        ConversionRoute? representative = null;
        string? reason = null;
        var missing = new HashSet<ExternalTool>();
        var allAvailable = true;
        var convertedCount = 0;

        foreach (var input in inputs)
        {
            var matches = candidates.Where(c => c.Route.Accepts(input.Format)).ToList();
            if (matches.Count == 0)
            {
                // Inputs already in the target format are skipped instead of disqualifying the target,
                // so "PNG" is still offered for a mix of PNG and JPEG files.
                if (IsAlreadyInTargetFormat(input, candidates[0].Route))
                {
                    continue;
                }
                return null;
            }

            convertedCount++;
            var best = matches
                .Select(m => (m.Route, Availability: m.Converter.GetAvailability(m.Route)))
                .OrderByDescending(m => m.Availability.IsAvailable)
                .ThenBy(m => m.Route.Priority)
                .First();
            representative ??= best.Route;

            if (!best.Availability.IsAvailable)
            {
                allAvailable = false;
                reason ??= best.Availability.Reason;
                if (best.Availability.MissingTool is { } tool)
                {
                    missing.Add(tool);
                }
            }
        }

        if (representative is null || convertedCount == 0)
        {
            return null;
        }

        return ToTarget(representative, allAvailable, allAvailable ? null : reason, missing.ToList());
    }

    private static bool IsAlreadyInTargetFormat(InputFile input, ConversionRoute route) =>
        route.Kind == RouteKind.Convert && route.Output is not null && input.Format == route.Output;

    private static ConversionTarget ToTarget(ConversionRoute route, bool available, string? reason, IReadOnlyList<ExternalTool> missing) =>
        new(route.TargetKey, route.Label, route.Kind, route.Output, available, reason,
            route.Description, route.Limitations, route.Experimental, route.Priority, missing, route.RequiresOptions);

    /// <summary>
    /// Builds executable plans for a chosen target. Per-file targets produce one plan per input;
    /// combine targets produce a single plan. Inputs already in the target format are returned in
    /// <paramref name="skipped"/>.
    /// </summary>
    public IReadOnlyList<ConversionPlan> CreatePlans(
        IReadOnlyList<InputFile> inputs,
        string targetKey,
        ConversionOptions options,
        OutputSettings output,
        out IReadOnlyList<InputFile> skipped)
    {
        var skippedList = new List<InputFile>();
        skipped = skippedList;
        var candidates = AllRoutes.Where(x => string.Equals(x.Route.TargetKey, targetKey, StringComparison.OrdinalIgnoreCase)).ToList();
        if (candidates.Count == 0 && KnownFormats.FindById(targetKey) is { } alias)
        {
            // Accept extensions as targets ("jpg" → "jpeg", "markdown" → "md").
            targetKey = alias.Id;
            candidates = AllRoutes.Where(x => string.Equals(x.Route.TargetKey, targetKey, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (candidates.Count == 0)
        {
            throw new ConversionException($"Unknown conversion target '{targetKey}'.");
        }

        var combine = candidates.FirstOrDefault(c => c.Route.Kind == RouteKind.Combine);
        if (combine.Route is not null)
        {
            if (inputs.Count < combine.Route.MinInputs)
            {
                throw new ConversionException($"{combine.Route.Label} needs at least {combine.Route.MinInputs} files.");
            }
            var unsupported = inputs.FirstOrDefault(i => !combine.Route.Accepts(i.Format));
            if (unsupported is not null)
            {
                throw new ConversionException($"{unsupported.FileName} cannot be used for {combine.Route.Label}.");
            }
            EnsureAvailable(combine.Converter, combine.Route);
            combine.Converter.ValidateOptions(combine.Route, inputs, options);
            return [new ConversionPlan(combine.Converter, combine.Route, inputs, options.Clone(), output)];
        }

        var plans = new List<ConversionPlan>();
        foreach (var input in inputs)
        {
            var match = candidates
                .Where(c => c.Route.Accepts(input.Format))
                .OrderByDescending(c => c.Converter.GetAvailability(c.Route).IsAvailable)
                .ThenBy(c => c.Route.Priority)
                .FirstOrDefault();

            if (match.Route is null)
            {
                if (IsAlreadyInTargetFormat(input, candidates[0].Route))
                {
                    skippedList.Add(input);
                    continue;
                }
                var what = input.Format?.Label ?? "this file type";
                throw new ConversionException($"{input.FileName}: {what} cannot be converted to {candidates[0].Route.Label}.");
            }

            EnsureAvailable(match.Converter, match.Route);
            match.Converter.ValidateOptions(match.Route, [input], options);
            plans.Add(new ConversionPlan(match.Converter, match.Route, [input], options.Clone(), output));
        }
        return plans;
    }

    private static void EnsureAvailable(IConverter converter, ConversionRoute route)
    {
        var availability = converter.GetAvailability(route);
        if (!availability.IsAvailable)
        {
            throw new ConversionException(availability.Reason ?? $"{route.Label} is not available.");
        }
    }
}
