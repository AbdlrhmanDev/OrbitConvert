using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrbitConvert.App.Services;
using OrbitConvert.Core.Queue;

namespace OrbitConvert.App.ViewModels;

public enum Page { Home, Convert, Tools, History, Settings }

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ConversionQueue _queue;

    public MainViewModel(HomeViewModel home, ConvertViewModel convert, ToolsViewModel tools, HistoryViewModel history,
        SettingsViewModel settings, ConversionQueue queue)
    {
        Home = home;
        Convert = convert;
        Tools = tools;
        History = history;
        Settings = settings;
        _queue = queue;
        _currentPage = home;

        home.Navigate = Navigate;
        tools.Navigate = Navigate;
        queue.JobAdded += (_, _) => Ui.Post(UpdateQueueSummary);
        queue.JobCompleted += (_, _) => Ui.Post(UpdateQueueSummary);
        queue.JobRemoved += (_, _) => Ui.Post(UpdateQueueSummary);
    }

    public HomeViewModel Home { get; }
    public ConvertViewModel Convert { get; }
    public ToolsViewModel Tools { get; }
    public HistoryViewModel History { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private object _currentPage;

    [ObservableProperty]
    private Page _selectedPage = Page.Home;

    [ObservableProperty]
    private string _queueSummary = "No active conversions";

    [ObservableProperty]
    private bool _hasActiveJobs;

    partial void OnSelectedPageChanged(Page value)
    {
        CurrentPage = value switch
        {
            Page.Convert => Convert,
            Page.Tools => Tools,
            Page.History => History,
            Page.Settings => Settings,
            _ => Home,
        };
        if (value == Page.Settings)
        {
            Settings.Refresh();
        }
    }

    [RelayCommand]
    public void Navigate(Page page) => SelectedPage = page;

    public void ShowConvert(IReadOnlyList<string> files, string? targetKey)
    {
        SelectedPage = Page.Convert;
        _ = Convert.AddFilesAsync(files, targetKey);
    }

    private void UpdateQueueSummary()
    {
        var jobs = _queue.Jobs;
        var running = jobs.Count(j => j.Status == JobStatus.Running);
        var waiting = jobs.Count(j => j.Status is JobStatus.Queued or JobStatus.Paused);
        HasActiveJobs = running + waiting > 0;
        QueueSummary = HasActiveJobs
            ? string.Join(" · ", new[] { running > 0 ? $"{running} converting" : null, waiting > 0 ? $"{waiting} waiting" : null }.Where(s => s is not null))
            : "No active conversions";
    }
}
