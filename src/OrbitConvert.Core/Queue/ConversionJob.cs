using CommunityToolkit.Mvvm.ComponentModel;
using OrbitConvert.Core.Conversion;

namespace OrbitConvert.Core.Queue;

public enum JobStatus { Queued, Paused, Running, Succeeded, Failed, Canceled, Skipped }

/// <summary>
/// A queued conversion. Property changes may be raised from worker threads; WPF marshals
/// scalar property notifications to the UI thread automatically.
/// </summary>
public sealed partial class ConversionJob : ObservableObject
{
    internal CancellationTokenSource? Cancellation;

    public ConversionJob(ConversionPlan plan)
    {
        Plan = plan;
        Title = plan.Title;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public ConversionPlan Plan { get; }
    public string Title { get; }
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.Now;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFinished), nameof(IsActive), nameof(CanCancel), nameof(CanRetry), nameof(CanPause), nameof(CanResume))]
    private JobStatus _status = JobStatus.Queued;

    /// <summary>0..1, or a negative value when progress is indeterminate.</summary>
    [ObservableProperty]
    private double _progress = -1;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private string? _note;

    [ObservableProperty]
    private string? _technicalDetail;

    [ObservableProperty]
    private IReadOnlyList<string> _outputs = [];

    [ObservableProperty]
    private DateTimeOffset? _startedAt;

    [ObservableProperty]
    private DateTimeOffset? _completedAt;

    public int Attempts { get; internal set; }

    public bool IsFinished => Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Canceled or JobStatus.Skipped;
    public bool IsActive => Status is JobStatus.Queued or JobStatus.Running or JobStatus.Paused;
    public bool CanCancel => IsActive;
    public bool CanRetry => Status is JobStatus.Failed or JobStatus.Canceled;
    public bool CanPause => Status == JobStatus.Queued;
    public bool CanResume => Status == JobStatus.Paused;

    internal void Reset()
    {
        Progress = -1;
        Error = null;
        Note = null;
        TechnicalDetail = null;
        Outputs = [];
        StartedAt = null;
        CompletedAt = null;
    }
}
