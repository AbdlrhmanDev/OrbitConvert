using Microsoft.Extensions.Logging;
using OrbitConvert.Core.Automation;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Settings;
using OrbitConvert.Infrastructure.Automation;

namespace OrbitConvert.App.Services;

/// <summary>
/// Runs folder automation: each new file in an enabled watched folder is converted with the
/// watcher's preset. Outputs are registered so they are never picked up again. Watchers are off
/// until the user turns them on, and automation batches do not raise a toast per file.
/// </summary>
public sealed class AutomationService : IDisposable
{
    private readonly ISettingsStore _settings;
    private readonly ConversionCoordinator _coordinator;
    private readonly ILogger<AutomationService> _logger;
    private readonly FolderAutomation _automation;
    private string _signature = "";

    public AutomationService(ISettingsStore settings, ConversionCoordinator coordinator, ILogger<AutomationService> logger)
    {
        _settings = settings;
        _coordinator = coordinator;
        _logger = logger;
        _automation = new FolderAutomation(() => _settings.Current.Watchers, ProcessAsync, logger);
    }

    /// <summary>Raised (on a worker thread) after a watcher processed a file or failed.</summary>
    public event EventHandler? StatisticsChanged;

    public int ActiveCount => _automation.ActiveCount;

    public void Start()
    {
        ReloadIfChanged();
        _settings.Changed += (_, _) => ReloadIfChanged();
        _coordinator.Queue.JobCompleted += (_, job) =>
        {
            // Anything OrbitConvert writes is never treated as a new file to convert.
            foreach (var output in job.Outputs)
            {
                _automation.Ignore(output);
            }
        };
    }

    /// <summary>Recreates file system watchers only when the watcher configuration changed.</summary>
    private void ReloadIfChanged()
    {
        var signature = string.Join("|", _settings.Current.Watchers.Select(w =>
            $"{w.Id};{w.Path};{w.Enabled};{string.Join(',', w.Extensions)}"));
        if (signature == _signature)
        {
            return;
        }
        _signature = signature;
        try
        {
            _automation.Reload();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not start folder automation");
        }
    }

    private async Task ProcessAsync(WatchFolder watcher, string path)
    {
        var settings = _settings.Current;
        var inputs = _coordinator.Registry.Analyze([path]);
        var preset = settings.Presets.FirstOrDefault(p => p.Id == watcher.PresetId);
        if (preset is null)
        {
            Record(watcher, "The preset for this watcher was deleted.");
            return;
        }
        var targets = _coordinator.Registry.GetTargets(inputs);
        if (!_coordinator.PresetApplies(preset, inputs, targets))
        {
            Record(watcher, $"\"{preset.Name}\" cannot convert {Path.GetFileName(path)}.");
            return;
        }

        var output = new OutputSettings(watcher.OutputFolder, settings.Collision);
        ConversionBatch batch;
        try
        {
            batch = _coordinator.StartPreset(inputs, preset.Id, output, notify: false);
        }
        catch (ConversionException ex)
        {
            Record(watcher, ex.Message);
            return;
        }

        var done = new TaskCompletionSource();
        batch.Completed += (_, _) => done.TrySetResult();
        if (batch.IsCompleted)
        {
            done.TrySetResult();
        }
        await done.Task.ConfigureAwait(false);

        foreach (var file in batch.Outputs)
        {
            _automation.Ignore(file);
        }
        Record(watcher, batch.FailedCount > 0 ? batch.FirstError : null, processed: batch.SucceededCount);
    }

    private void Record(WatchFolder watcher, string? error, int processed = 0)
    {
        watcher.ProcessedCount += processed;
        watcher.LastProcessed = DateTimeOffset.Now;
        watcher.LastError = error;
        try
        {
            _settings.Save(_settings.Current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save automation statistics");
        }
        StatisticsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _automation.Dispose();
}
