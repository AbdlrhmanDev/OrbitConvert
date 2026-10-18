using Microsoft.Extensions.Logging;
using OrbitConvert.Core.Conversion;

namespace OrbitConvert.Core.Queue;

/// <summary>
/// Runs conversion jobs in the background with bounded parallelism. Queued jobs can be paused,
/// resumed and canceled individually; running jobs can be canceled; failed jobs can be retried.
/// </summary>
public sealed class ConversionQueue : IDisposable
{
    private readonly ConversionEngine _engine;
    private readonly ILogger<ConversionQueue> _logger;
    private readonly object _gate = new();
    private readonly List<ConversionJob> _jobs = [];
    private readonly HashSet<Task> _running = [];
    private int _maxConcurrency;
    private bool _disposed;

    public ConversionQueue(ConversionEngine engine, ILogger<ConversionQueue> logger, int maxConcurrency = 2)
    {
        _engine = engine;
        _logger = logger;
        _maxConcurrency = Math.Max(1, maxConcurrency);
    }

    public event EventHandler<ConversionJob>? JobAdded;
    public event EventHandler<ConversionJob>? JobRemoved;
    public event EventHandler<ConversionJob>? JobCompleted;

    public int MaxConcurrency
    {
        get { lock (_gate) return _maxConcurrency; }
        set
        {
            lock (_gate) _maxConcurrency = Math.Clamp(value, 1, 16);
            Pump();
        }
    }

    public IReadOnlyList<ConversionJob> Jobs
    {
        get { lock (_gate) return _jobs.ToList(); }
    }

    public int ActiveCount
    {
        get { lock (_gate) return _jobs.Count(j => j.IsActive); }
    }

    public IReadOnlyList<ConversionJob> Enqueue(IEnumerable<ConversionPlan> plans)
    {
        var added = plans.Select(p => new ConversionJob(p)).ToList();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _jobs.AddRange(added);
        }
        foreach (var job in added)
        {
            JobAdded?.Invoke(this, job);
        }
        Pump();
        return added;
    }

    public void Pause(ConversionJob job)
    {
        lock (_gate)
        {
            if (job.Status == JobStatus.Queued)
            {
                job.Status = JobStatus.Paused;
            }
        }
    }

    public void Resume(ConversionJob job)
    {
        lock (_gate)
        {
            if (job.Status == JobStatus.Paused)
            {
                job.Status = JobStatus.Queued;
            }
        }
        Pump();
    }

    public void Cancel(ConversionJob job)
    {
        ConversionJob? completed = null;
        lock (_gate)
        {
            switch (job.Status)
            {
                case JobStatus.Queued or JobStatus.Paused:
                    job.Status = JobStatus.Canceled;
                    job.Error = "Canceled before it started.";
                    job.CompletedAt = DateTimeOffset.Now;
                    completed = job;
                    break;
                case JobStatus.Running:
                    job.Cancellation?.Cancel();
                    break;
            }
        }
        if (completed is not null)
        {
            JobCompleted?.Invoke(this, completed);
        }
    }

    public void CancelAll()
    {
        foreach (var job in Jobs.Where(j => j.IsActive))
        {
            Cancel(job);
        }
    }

    public void Retry(ConversionJob job)
    {
        lock (_gate)
        {
            if (!job.CanRetry)
            {
                return;
            }
            job.Reset();
            job.Status = JobStatus.Queued;
        }
        Pump();
    }

    public void Remove(ConversionJob job)
    {
        bool removed;
        lock (_gate)
        {
            removed = job.IsFinished && _jobs.Remove(job);
        }
        if (removed)
        {
            JobRemoved?.Invoke(this, job);
        }
    }

    public void ClearFinished()
    {
        List<ConversionJob> removed;
        lock (_gate)
        {
            removed = _jobs.Where(j => j.IsFinished).ToList();
            _jobs.RemoveAll(j => j.IsFinished);
        }
        foreach (var job in removed)
        {
            JobRemoved?.Invoke(this, job);
        }
    }

    /// <summary>Completes when no job is queued or running. Paused jobs do not block.</summary>
    public async Task WaitForIdleAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task[] running;
            bool anyQueued;
            lock (_gate)
            {
                running = _running.ToArray();
                anyQueued = _jobs.Any(j => j.Status == JobStatus.Queued);
            }
            if (running.Length == 0 && !anyQueued)
            {
                return;
            }
            if (running.Length > 0)
            {
                await Task.WhenAny(running).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void Pump()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            while (_running.Count < _maxConcurrency)
            {
                var next = _jobs.FirstOrDefault(j => j.Status == JobStatus.Queued);
                if (next is null)
                {
                    break;
                }
                next.Status = JobStatus.Running;
                next.Attempts++;
                next.StartedAt = DateTimeOffset.Now;
                next.Cancellation = new CancellationTokenSource();
                var task = Task.Run(() => RunJobAsync(next));
                _running.Add(task);
                _ = task.ContinueWith(t =>
                {
                    lock (_gate) _running.Remove(t);
                    Pump();
                }, TaskScheduler.Default);
            }
        }
    }

    private async Task RunJobAsync(ConversionJob job)
    {
        var token = job.Cancellation!.Token;
        var progress = new SynchronousProgress(p => job.Progress = p);
        ConversionResult result;
        try
        {
            result = await _engine.RunAsync(job.Plan, progress, token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The engine converts failures into results; this guards against bugs so a job never hangs as Running.
            _logger.LogError(ex, "Unhandled error in job {Title}", job.Title);
            result = new ConversionResult(ConversionStatus.Failed, [], ErrorTranslator.ToUserMessage(ex), TimeSpan.Zero);
        }

        lock (_gate)
        {
            job.Outputs = result.Outputs;
            job.Error = result.Error;
            job.Note = result.Note;
            job.TechnicalDetail = result.TechnicalDetail;
            job.CompletedAt = DateTimeOffset.Now;
            if (result.Status == ConversionStatus.Succeeded)
            {
                job.Progress = 1;
            }
            job.Status = result.Status switch
            {
                ConversionStatus.Succeeded => JobStatus.Succeeded,
                ConversionStatus.Canceled => JobStatus.Canceled,
                ConversionStatus.Skipped => JobStatus.Skipped,
                _ => JobStatus.Failed,
            };
            job.Cancellation.Dispose();
            job.Cancellation = null;
        }
        JobCompleted?.Invoke(this, job);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            foreach (var job in _jobs.Where(j => j.Status == JobStatus.Running))
            {
                job.Cancellation?.Cancel();
            }
        }
    }

    /// <summary>Progress&lt;T&gt; posts to a captured context; jobs run on the pool so report inline.</summary>
    private sealed class SynchronousProgress(Action<double> report) : IProgress<double>
    {
        private double _last = -2;

        public void Report(double value)
        {
            // Throttle to 0.5% steps to avoid flooding the UI.
            if (Math.Abs(value - _last) >= 0.005 || value is 0 or 1)
            {
                _last = value;
                report(value);
            }
        }
    }
}
