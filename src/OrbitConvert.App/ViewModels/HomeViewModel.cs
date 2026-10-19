using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrbitConvert.App.Services;
using OrbitConvert.Core.Presets;
using OrbitConvert.Core.Queue;
using OrbitConvert.Core.Settings;

namespace OrbitConvert.App.ViewModels;

public sealed partial class HomeViewModel : ObservableObject
{
    private readonly DialogService _dialogs;
    private readonly ISettingsStore _settings;
    private readonly ConversionQueue _queue;
    private readonly PresetRunner _presets;
    private readonly Lazy<IMainWindowHost> _host;
    private readonly Lazy<RadialMenuService> _radial;

    public HomeViewModel(DialogService dialogs, ISettingsStore settings, ConversionQueue queue, PresetRunner presets,
        Lazy<IMainWindowHost> host, Lazy<RadialMenuService> radial)
    {
        _dialogs = dialogs;
        _settings = settings;
        _queue = queue;
        _presets = presets;
        _host = host;
        _radial = radial;
        queue.JobAdded += (_, _) => Ui.Post(RefreshQueue);
        queue.JobRemoved += (_, _) => Ui.Post(RefreshQueue);
        Refresh();
    }

    public Action<Page>? Navigate { get; set; }

    public ObservableCollection<ConversionPreset> QuickPresets { get; } = [];
    public ObservableCollection<ConversionJob> TopJobs { get; } = [];

    [ObservableProperty] private string _greeting = "";
    [ObservableProperty] private string _hotkeyText = "";
    [ObservableProperty] private bool _hasJobs;
    [ObservableProperty] private string? _message;

    public void Refresh()
    {
        var hour = DateTime.Now.Hour;
        Greeting = hour < 5 ? "Good evening" : hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
        var hotkey = _settings.Current.Hotkey;
        HotkeyText = hotkey.Enabled ? FormatHotkey(hotkey) : "Shortcut off";

        QuickPresets.Clear();
        foreach (var preset in _settings.Current.Presets.OrderByDescending(p => p.Starred).Take(4))
        {
            QuickPresets.Add(preset);
        }
        RefreshQueue();
    }

    /// <summary>"Win Shift C": the order Windows uses for its own shortcuts.</summary>
    private static string FormatHotkey(HotkeySettings hotkey)
    {
        var parts = new List<string>();
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        parts.Add(hotkey.Key);
        return string.Join(" ", parts);
    }

    private void RefreshQueue()
    {
        TopJobs.Clear();
        foreach (var job in _queue.Jobs.Reverse().Take(3))
        {
            TopJobs.Add(job);
        }
        HasJobs = TopJobs.Count > 0;
    }

    public void AcceptDrop(IReadOnlyList<string> files) => _host.Value.ShowConvert(files);

    [RelayCommand]
    private void SelectFiles()
    {
        var files = _dialogs.PickFiles("Choose files to convert");
        if (files.Count > 0)
        {
            _host.Value.ShowConvert(files);
        }
    }

    [RelayCommand]
    private void AddFolder()
    {
        var folder = _dialogs.PickFolder("Choose a folder; its files are added (not subfolders)");
        if (folder is null)
        {
            return;
        }
        var files = Directory.EnumerateFiles(folder).Where(f => !Core.Automation.WatchFolder.IsTransient(f)).ToList();
        if (files.Count == 0)
        {
            Message = "That folder has no files.";
            return;
        }
        _host.Value.ShowConvert(files);
    }

    /// <summary>Picks files and shows the Orbit Menu for them, as the Explorer shortcut would.</summary>
    [RelayCommand]
    private async Task OpenOrbit()
    {
        var files = _dialogs.PickFiles("Choose files for the Orbit Menu");
        if (files.Count > 0)
        {
            await _radial.Value.ShowAsync(files);
        }
    }

    [RelayCommand]
    private void RunPreset(ConversionPreset preset)
    {
        var run = _presets.PickAndRun(preset);
        Message = run.Started ? null : run.Message;
        if (run.Started)
        {
            Navigate?.Invoke(Page.Batch);
        }
    }

    [RelayCommand]
    private void ShowBatch() => Navigate?.Invoke(Page.Batch);
}
