using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrbitConvert.App.Services;
using OrbitConvert.Core.Queue;
using OrbitConvert.Core.Settings;

namespace OrbitConvert.App.ViewModels;

public enum Page { Home, Convert, Batch, PdfTools, ImageTools, Automation, Presets, History, Settings }

/// <summary>An entry in the Ctrl+K search: a page, a tool or a preset.</summary>
public sealed record SearchCommand(string Title, string Kind, Action Run);

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ConversionQueue _queue;
    private readonly ISettingsStore _settings;

    public MainViewModel(HomeViewModel home, ConvertViewModel convert, BatchViewModel batch, ToolsViewModel tools,
        AutomationViewModel automation, PresetsViewModel presets, HistoryViewModel history, SettingsViewModel settings,
        ConversionQueue queue, ISettingsStore settingsStore)
    {
        Home = home;
        Convert = convert;
        Batch = batch;
        Tools = tools;
        Automation = automation;
        Presets = presets;
        History = history;
        Settings = settings;
        _queue = queue;
        _settings = settingsStore;
        _currentPage = home;

        home.Navigate = Navigate;
        tools.Navigate = Navigate;
        presets.Navigate = Navigate;
        convert.Navigate = Navigate;
        queue.JobAdded += (_, _) => Ui.Post(UpdateBadges);
        queue.JobCompleted += (_, _) => Ui.Post(UpdateBadges);
        queue.JobRemoved += (_, _) => Ui.Post(UpdateBadges);
        settingsStore.Changed += (_, _) => Ui.Post(UpdateBadges);
        UpdateBadges();
    }

    public HomeViewModel Home { get; }
    public ConvertViewModel Convert { get; }
    public BatchViewModel Batch { get; }
    public ToolsViewModel Tools { get; }
    public AutomationViewModel Automation { get; }
    public PresetsViewModel Presets { get; }
    public HistoryViewModel History { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty] private object _currentPage;
    [ObservableProperty] private Page _selectedPage = Page.Home;
    [ObservableProperty] private string _batchBadge = "";
    [ObservableProperty] private string _automationBadge = "";

    // Ctrl+K search
    [ObservableProperty] private bool _isSearchOpen;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private SearchCommand? _selectedResult;
    public ObservableCollection<SearchCommand> SearchResults { get; } = [];

    partial void OnSelectedPageChanged(Page value)
    {
        CurrentPage = value switch
        {
            Page.Convert => Convert,
            Page.Batch => Batch,
            Page.PdfTools or Page.ImageTools => Tools,
            Page.Automation => Automation,
            Page.Presets => Presets,
            Page.History => History,
            Page.Settings => Settings,
            _ => Home,
        };
        if (value is Page.PdfTools or Page.ImageTools)
        {
            Tools.Mode = value == Page.PdfTools ? ToolsMode.Pdf : ToolsMode.Image;
        }
        if (value == Page.Settings)
        {
            Settings.Refresh();
        }
        if (value == Page.Automation)
        {
            Automation.Refresh();
        }
        if (value == Page.Presets)
        {
            Presets.Refresh();
        }
        if (value == Page.Home)
        {
            Home.Refresh();
        }
    }

    [RelayCommand]
    public void Navigate(Page page) => SelectedPage = page;

    public void ShowConvert(IReadOnlyList<string> files, string? targetKey)
    {
        SelectedPage = Page.Convert;
        _ = Convert.AddFilesAsync(files, targetKey);
    }

    private void UpdateBadges()
    {
        var active = _queue.Jobs.Count(j => j.IsActive);
        BatchBadge = active > 0 ? active.ToString() : "";
        var watching = _settings.Current.Watchers.Count(w => w.Enabled);
        AutomationBadge = watching > 0 ? watching.ToString() : "";
    }

    [RelayCommand]
    private void OpenSearch()
    {
        SearchText = "";
        IsSearchOpen = true;
        UpdateSearch();
    }

    [RelayCommand]
    private void CloseSearch() => IsSearchOpen = false;

    partial void OnSearchTextChanged(string value) => UpdateSearch();

    private IEnumerable<SearchCommand> AllCommands()
    {
        foreach (var (page, title) in new[]
                 {
                     (Page.Home, "Home"), (Page.Convert, "Convert"), (Page.Batch, "Batch queue"), (Page.PdfTools, "PDF tools"),
                     (Page.ImageTools, "Image tools"), (Page.Automation, "Automation · watch folders"), (Page.Presets, "Presets"),
                     (Page.History, "History"), (Page.Settings, "Settings"),
                 })
        {
            yield return new SearchCommand(title, "Page", () => Navigate(page));
        }
        foreach (var card in Tools.PdfTools.Concat(Tools.ImageTools).Where(c => c.Available))
        {
            yield return new SearchCommand(card.Title, card.Category == "Pdf" ? "PDF tool" : "Image tool", () => Tools.RunCommand.Execute(card));
        }
        foreach (var preset in _settings.Current.Presets)
        {
            var id = preset.Id;
            yield return new SearchCommand(preset.Name, "Preset · " + preset.Tag, () => Presets.RunById(id));
        }
        foreach (var entry in History.Entries.Take(30))
        {
            var e = entry;
            yield return new SearchCommand($"{e.InputSummary} → {e.TargetLabel}", "History", () =>
            {
                Navigate(Page.History);
                History.RevealCommand.Execute(e);
            });
        }
        yield return new SearchCommand("Select files to convert", "Action", () => Home.SelectFilesCommand.Execute(null));
    }

    private void UpdateSearch()
    {
        SearchResults.Clear();
        var terms = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var command in AllCommands()
                     .Where(c => terms.All(t => c.Title.Contains(t, StringComparison.OrdinalIgnoreCase)
                                                || c.Kind.Contains(t, StringComparison.OrdinalIgnoreCase)))
                     .Take(12))
        {
            SearchResults.Add(command);
        }
        SelectedResult = SearchResults.FirstOrDefault();
    }

    [RelayCommand]
    private void RunSearchResult(SearchCommand? command)
    {
        command ??= SelectedResult;
        if (command is null)
        {
            return;
        }
        IsSearchOpen = false;
        command.Run();
    }

    public void MoveSearchSelection(int delta)
    {
        if (SearchResults.Count == 0)
        {
            return;
        }
        var index = SelectedResult is null ? 0 : SearchResults.IndexOf(SelectedResult) + delta;
        SelectedResult = SearchResults[(index % SearchResults.Count + SearchResults.Count) % SearchResults.Count];
    }
}
