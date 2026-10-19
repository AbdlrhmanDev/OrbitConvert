using System.ComponentModel;
using Microsoft.Extensions.Logging;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.History;
using OrbitConvert.Core.Queue;
using OrbitConvert.Core.Settings;

namespace OrbitConvert.App.Services;

/// <summary>
/// A group of jobs started together (one radial selection, one "Start" click).
/// Aggregates progress and completion for the UI and for a single notification.
/// </summary>
public sealed class ConversionBatch : INotifyPropertyChanged
{
    private readonly object _gate = new();
    private readonly HashSet<ConversionJob> _pending = [];

    public ConversionBatch(IReadOnlyList<ConversionJob> jobs, int skipped, string targetLabel)
    {
        Jobs = jobs;
        Skipped = skipped;
        TargetLabel = targetLabel;
        lock (_gate)
        {
            // Subscribe before checking state so a job finishing in between is never missed.
            foreach (var job in jobs)
            {
                job.PropertyChanged += OnJobChanged;
            }
            foreach (var job in jobs.Where(j => !j.IsFinished))
            {
                _pending.Add(job);
            }
            IsCompleted = _pending.Count == 0;
        }
    }

    public IReadOnlyList<ConversionJob> Jobs { get; }
    public int Skipped { get; }
    public string TargetLabel { get; }
    public bool IsCompleted { get; private set; }

    public double Progress
    {
        get
        {
            if (Jobs.Count == 0)
            {
                return 1;
            }
            var known = Jobs.Select(j => j.IsFinished ? 1 : Math.Max(0, j.Progress)).Sum();
            var anyIndeterminate = Jobs.Any(j => j.Status == JobStatus.Running && j.Progress < 0);
            return anyIndeterminate && known == 0 ? -1 : known / Jobs.Count;
        }
    }

    public int SucceededCount => Jobs.Count(j => j.Status == JobStatus.Succeeded);
    public int SkippedCount => Jobs.Count(j => j.Status == JobStatus.Skipped);
    public int FailedCount => Jobs.Count(j => j.Status is JobStatus.Failed or JobStatus.Canceled);
    public IEnumerable<string> Outputs => Jobs.SelectMany(j => j.Outputs);
    public string? FirstError => Jobs.Select(j => j.Error).FirstOrDefault(e => e is not null && Jobs.Any(x => x.Status == JobStatus.Failed));

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Completed;

    private void OnJobChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConversionJob.Progress))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
        }
        else if (e.PropertyName == nameof(ConversionJob.Status) && sender is ConversionJob { IsFinished: true } job)
        {
            bool done;
            lock (_gate)
            {
                job.PropertyChanged -= OnJobChanged;
                done = _pending.Remove(job) && _pending.Count == 0;
                if (done)
                {
                    IsCompleted = true;
                }
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
            if (done)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCompleted)));
                Completed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}

/// <summary>
/// The single entry point the UI uses to start conversions. Builds plans through the capability
/// registry, queues them, records history and raises completion notifications.
/// </summary>
public sealed class ConversionCoordinator
{
    private readonly ConversionCapabilityRegistry _registry;
    private readonly ConversionQueue _queue;
    private readonly ISettingsStore _settings;
    private readonly IHistoryStore _history;
    private readonly NotificationService _notifications;
    private readonly ILogger<ConversionCoordinator> _logger;

    public ConversionCoordinator(ConversionCapabilityRegistry registry, ConversionQueue queue, ISettingsStore settings,
        IHistoryStore history, NotificationService notifications, ILogger<ConversionCoordinator> logger)
    {
        _registry = registry;
        _queue = queue;
        _settings = settings;
        _history = history;
        _notifications = notifications;
        _logger = logger;
        _queue.JobCompleted += (_, job) => Record(job);
        _settings.Changed += (_, s) => _queue.MaxConcurrency = s.MaxParallelJobs;
    }

    public ConversionQueue Queue => _queue;
    public ConversionCapabilityRegistry Registry => _registry;

    /// <summary>Suppresses the toast when the user is already looking at the result (e.g. the radial menu).</summary>
    public Func<bool> IsResultVisible { get; set; } = () => false;

    public ConversionBatch Start(IReadOnlyList<InputFile> inputs, string targetKey, ConversionOptions? options = null, OutputSettings? output = null)
    {
        var settings = _settings.Current;
        var plans = _registry.CreatePlans(inputs, targetKey, options ?? settings.Defaults, output ?? settings.ToOutputSettings(), out var skipped);
        return Enqueue(plans, skipped.Count, plans.FirstOrDefault()?.Route.Label ?? targetKey);
    }

    public IReadOnlyList<Core.Presets.ConversionPreset> Presets => _settings.Current.Presets;

    /// <summary>Runs a saved preset (its target and options) on the inputs.</summary>
    public ConversionBatch StartPreset(IReadOnlyList<InputFile> inputs, string presetId, OutputSettings? output = null, bool notify = true)
    {
        var preset = _settings.Current.Presets.FirstOrDefault(p => p.Id == presetId)
            ?? throw new ConversionException("That preset no longer exists.");
        var settings = _settings.Current;
        var plans = _registry.CreatePlans(inputs, preset.TargetKey, preset.Options, output ?? settings.ToOutputSettings(), out var skipped);
        return Enqueue(plans, skipped.Count, preset.Name, notify);
    }

    /// <summary>True when the preset's target can run, with defaults, on every input.</summary>
    public bool PresetApplies(Core.Presets.ConversionPreset preset, IReadOnlyList<InputFile> inputs, IReadOnlyList<ConversionTarget> targets) =>
        preset.IsFor(inputs.Select(i => i.Format?.Category))
        && targets.Any(t => t.IsAvailable && string.Equals(t.Key, preset.TargetKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>Starts pre-built plans, e.g. when each file has its own options.</summary>
    public ConversionBatch Enqueue(IReadOnlyList<ConversionPlan> plans, int skipped, string label, bool notify = true)
    {
        var jobs = _queue.Enqueue(plans);
        var batch = new ConversionBatch(jobs, skipped, label);
        if (notify)
        {
            batch.Completed += (_, _) => Ui.Post(() => Notify(batch));
        }
        _logger.LogInformation("Queued {Count} job(s) for {Target}", jobs.Count, label);
        return batch;
    }

    private void Record(ConversionJob job)
    {
        try
        {
            _history.Add(new HistoryEntry
            {
                Inputs = job.Plan.Inputs.Select(i => i.Path).ToList(),
                TargetKey = job.Plan.Route.TargetKey,
                TargetLabel = job.Plan.Route.Label,
                Succeeded = job.Status == JobStatus.Succeeded,
                Status = job.Status.ToString(),
                Outputs = job.Outputs,
                Error = job.Error,
                Note = job.Note,
                DurationSeconds = job.StartedAt is { } s && job.CompletedAt is { } c ? (c - s).TotalSeconds : 0,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not record history");
        }
    }

    private void Notify(ConversionBatch batch)
    {
        if (!_settings.Current.NotificationsEnabled || batch.Jobs.Count == 0 || IsResultVisible())
        {
            return;
        }
        var ok = batch.SucceededCount;
        var failed = batch.FailedCount;
        if (failed == 0)
        {
            var first = batch.Outputs.FirstOrDefault();
            _notifications.Show(
                ok == 1 ? "Conversion complete" : $"{ok} conversions complete",
                ok == 1 && first is not null ? $"Saved {Path.GetFileName(first)}" : $"Converted to {batch.TargetLabel}",
                first);
        }
        else
        {
            _notifications.Show(
                failed == batch.Jobs.Count ? "Conversion failed" : $"{failed} of {batch.Jobs.Count} conversions failed",
                batch.FirstError ?? "Open OrbitConvert for details.",
                null,
                isError: true);
        }
    }
}
