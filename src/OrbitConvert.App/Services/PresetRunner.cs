using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;
using OrbitConvert.Core.Presets;

namespace OrbitConvert.App.Services;

/// <summary>Result of running a preset: <see cref="Started"/> is false when the user canceled or it could not run.</summary>
public sealed record PresetRun(bool Started, string? Message);

/// <summary>Runs a saved preset on files the user picks, checking first that it really applies to them.</summary>
public sealed class PresetRunner(ConversionCoordinator coordinator, DialogService dialogs)
{
    public PresetRun PickAndRun(ConversionPreset preset)
    {
        FormatCategory? filter = preset.AppliesTo.Count == 1 ? preset.AppliesTo[0] : null;
        var files = dialogs.PickFiles($"Choose files for \"{preset.Name}\"", filter);
        return files.Count == 0 ? new PresetRun(false, null) : Run(preset, files);
    }

    public PresetRun Run(ConversionPreset preset, IReadOnlyList<string> files)
    {
        var inputs = coordinator.Registry.Analyze(files);
        var targets = coordinator.Registry.GetTargets(inputs);
        if (!coordinator.PresetApplies(preset, inputs, targets))
        {
            var reason = targets.FirstOrDefault(t => string.Equals(t.Key, preset.TargetKey, StringComparison.OrdinalIgnoreCase))?.UnavailableReason;
            return new PresetRun(false, reason is not null
                ? $"\"{preset.Name}\" is unavailable: {reason}"
                : $"\"{preset.Name}\" cannot convert these files.");
        }
        try
        {
            var batch = coordinator.StartPreset(inputs, preset.Id);
            return new PresetRun(true, $"Queued {batch.Jobs.Count} file(s) with \"{preset.Name}\".");
        }
        catch (ConversionException ex)
        {
            return new PresetRun(false, ex.Message);
        }
    }
}
