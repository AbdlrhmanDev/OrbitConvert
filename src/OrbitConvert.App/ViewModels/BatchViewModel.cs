using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrbitConvert.App.Converters;
using OrbitConvert.App.Services;
using OrbitConvert.Core.Queue;
using OrbitConvert.Shell;

namespace OrbitConvert.App.ViewModels;

/// <summary>The conversion queue: every job, with pause, resume, cancel, retry and reveal.</summary>
public sealed partial class BatchViewModel : ObservableObject
{
    private readonly ConversionQueue _queue;
    private readonly DialogService _dialogs;
    private readonly Lazy<IMainWindowHost> _host;

    public BatchViewModel(ConversionQueue queue, DialogService dialogs, Lazy<IMainWindowHost> host)
    {
        _queue = queue;
        _dialogs = dialogs;
        _host = host;
        foreach (var job in queue.Jobs.Reverse())
        {
            Add(job, append: true);
        }
        queue.JobAdded += (_, job) => Ui.Post(() => Add(job, append: false));
        queue.JobRemoved += (_, job) => Ui.Post(() =>
        {
            job.PropertyChanged -= OnJobChanged;
            Jobs.Remove(job);
            UpdateSummary();
        });
        UpdateSummary();
    }

    public ObservableCollection<ConversionJob> Jobs { get; } = [];

    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _hasJobs;
    [ObservableProperty] private bool _hasWaiting;
    [ObservableProperty] private bool _hasPaused;

    private void Add(ConversionJob job, bool append)
    {
        job.PropertyChanged += OnJobChanged;
        if (append)
        {
            Jobs.Add(job);
        }
        else
        {
            Jobs.Insert(0, job);
        }
        UpdateSummary();
    }

    private void OnJobChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConversionJob.Status))
        {
            Ui.Post(UpdateSummary);
        }
    }

    private void UpdateSummary()
    {
        HasJobs = Jobs.Count > 0;
        HasWaiting = Jobs.Any(j => j.Status == JobStatus.Queued);
        HasPaused = Jobs.Any(j => j.Status == JobStatus.Paused);
        if (Jobs.Count == 0)
        {
            Summary = "Nothing in the queue. Conversions you start appear here.";
            return;
        }
        var bytes = Jobs.SelectMany(j => j.Plan.Inputs).Sum(i => i.Length);
        var active = Jobs.Count(j => j.IsActive);
        Summary = $"{Jobs.Count} job(s) · {active} active · {_queue.MaxConcurrency} at a time · {FileSizeConverter.Format(bytes)} total";
    }

    [RelayCommand]
    private void AddFiles()
    {
        var files = _dialogs.PickFiles("Add files");
        if (files.Count > 0)
        {
            _host.Value.ShowConvert(files);
        }
    }

    [RelayCommand]
    private void PauseQueue()
    {
        foreach (var job in Jobs.Where(j => j.Status == JobStatus.Queued).ToList())
        {
            _queue.Pause(job);
        }
    }

    [RelayCommand]
    private void ResumeQueue()
    {
        foreach (var job in Jobs.Where(j => j.Status == JobStatus.Paused).Reverse().ToList())
        {
            _queue.Resume(job);
        }
    }

    [RelayCommand] private void Cancel(ConversionJob job) => _queue.Cancel(job);
    [RelayCommand] private void Pause(ConversionJob job) => _queue.Pause(job);
    [RelayCommand] private void Resume(ConversionJob job) => _queue.Resume(job);
    [RelayCommand] private void Retry(ConversionJob job) => _queue.Retry(job);
    [RelayCommand] private void Remove(ConversionJob job) => _queue.Remove(job);
    [RelayCommand] private void ClearFinished() => _queue.ClearFinished();
    [RelayCommand] private void CancelAll() => _queue.CancelAll();

    [RelayCommand]
    private void Reveal(ConversionJob job)
    {
        var path = job.Outputs.FirstOrDefault(p => File.Exists(p) || Directory.Exists(p)) ?? job.Plan.Inputs.FirstOrDefault()?.Path;
        if (path is not null)
        {
            ShellLauncher.RevealInExplorer(path);
        }
    }
}
