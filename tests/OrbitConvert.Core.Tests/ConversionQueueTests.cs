using Microsoft.Extensions.Logging.Abstractions;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;
using OrbitConvert.Core.Queue;

namespace OrbitConvert.Core.Tests;

public class ConversionQueueTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly TempDir _work = new();

    public void Dispose()
    {
        _dir.Dispose();
        _work.Dispose();
    }

    private ConversionQueue Queue(int concurrency) =>
        new(EngineFactory.Create(_work.Path), NullLogger<ConversionQueue>.Instance, concurrency);

    private List<ConversionPlan> Plans(FakeConverter converter, int count) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var input = _dir.File($"f{i}.jpg", Samples.Jpeg);
            return new ConversionPlan(converter, converter.Routes[0], [EngineFactory.Input(input, KnownFormats.Jpeg)],
                new ConversionOptions(), OutputSettings.BesideOriginal);
        }).ToList();

    private static FakeConverter Converter() => new FakeConverter().Route("png", KnownFormats.Png, RouteKind.Convert, KnownFormats.Jpeg);

    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(20);
        }
        Assert.True(condition());
    }

    [Fact]
    public async Task Runs_every_job_and_never_exceeds_the_concurrency_limit()
    {
        var running = 0;
        var peak = 0;
        var converter = Converter();
        converter.Behavior = async (ctx, ct) =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref peak, now);
            await Task.Delay(40, ct);
            Interlocked.Decrement(ref running);
            return await FakeConverter.WritePng(ctx);
        };
        using var queue = Queue(2);

        var jobs = queue.Enqueue(Plans(converter, 8));
        await queue.WaitForIdleAsync();

        Assert.All(jobs, j => Assert.Equal(JobStatus.Succeeded, j.Status));
        Assert.Equal(2, peak);
        Assert.Equal(8, Directory.EnumerateFiles(_dir.Path, "*.png").Count());
    }

    [Fact]
    public async Task Paused_jobs_wait_until_resumed()
    {
        var gate = new TaskCompletionSource();
        var converter = Converter();
        converter.Behavior = async (ctx, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            return await FakeConverter.WritePng(ctx);
        };
        using var queue = Queue(1);

        var jobs = queue.Enqueue(Plans(converter, 2));
        queue.Pause(jobs[1]);
        Assert.Equal(JobStatus.Paused, jobs[1].Status);

        gate.SetResult();
        await queue.WaitForIdleAsync();
        Assert.Equal(JobStatus.Succeeded, jobs[0].Status);
        Assert.Equal(JobStatus.Paused, jobs[1].Status);

        queue.Resume(jobs[1]);
        await queue.WaitForIdleAsync();
        Assert.Equal(JobStatus.Succeeded, jobs[1].Status);
    }

    [Fact]
    public async Task Cancel_works_for_queued_and_running_jobs()
    {
        var started = new TaskCompletionSource();
        var converter = Converter();
        converter.Behavior = async (_, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return [];
        };
        using var queue = Queue(1);
        var jobs = queue.Enqueue(Plans(converter, 2));
        await started.Task;

        queue.Cancel(jobs[1]); // still queued
        queue.Cancel(jobs[0]); // running
        await queue.WaitForIdleAsync();

        Assert.Equal(JobStatus.Canceled, jobs[0].Status);
        Assert.Equal(JobStatus.Canceled, jobs[1].Status);
        Assert.Empty(Directory.EnumerateFiles(_dir.Path, "*.png"));
    }

    [Fact]
    public async Task Failed_job_can_be_retried()
    {
        var attempts = 0;
        var converter = Converter();
        converter.Behavior = (ctx, _) =>
            Interlocked.Increment(ref attempts) == 1 ? throw new ConversionException("Transient failure.") : FakeConverter.WritePng(ctx);
        using var queue = Queue(1);

        var job = Assert.Single(queue.Enqueue(Plans(converter, 1)));
        await queue.WaitForIdleAsync();
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("Transient failure.", job.Error);
        Assert.True(job.CanRetry);

        queue.Retry(job);
        await queue.WaitForIdleAsync();
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Null(job.Error);
        Assert.Equal(2, job.Attempts);
    }

    [Fact]
    public async Task Completed_event_fires_once_per_job_and_finished_jobs_can_be_cleared()
    {
        using var queue = Queue(3);
        var completed = 0;
        queue.JobCompleted += (_, _) => Interlocked.Increment(ref completed);

        queue.Enqueue(Plans(Converter(), 5));
        await queue.WaitForIdleAsync();
        await Eventually(() => Volatile.Read(ref completed) == 5);

        queue.ClearFinished();
        Assert.Empty(queue.Jobs);
    }

    [Fact]
    public async Task Progress_is_reported_to_the_job()
    {
        var converter = Converter();
        converter.Behavior = (ctx, _) =>
        {
            ctx.ReportProgress(0.25);
            return FakeConverter.WritePng(ctx);
        };
        using var queue = Queue(1);
        var seen = new List<double>();
        var job = Assert.Single(queue.Enqueue(Plans(converter, 1)));
        job.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConversionJob.Progress))
            {
                lock (seen) seen.Add(job.Progress);
            }
        };

        await queue.WaitForIdleAsync();

        Assert.Equal(1.0, job.Progress);
        lock (seen) Assert.Contains(0.25, seen);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
