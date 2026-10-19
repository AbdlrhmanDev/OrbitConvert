using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrbitConvert.App.Converters;
using OrbitConvert.App.Services;
using OrbitConvert.Core.Automation;
using OrbitConvert.Core.Presets;
using OrbitConvert.Core.Settings;

namespace OrbitConvert.App.ViewModels;

public sealed partial class WatcherViewModel : ObservableObject
{
    private readonly AutomationViewModel _owner;

    public WatcherViewModel(AutomationViewModel owner, WatchFolder watcher, string presetName)
    {
        _owner = owner;
        Watcher = watcher;
        PresetName = presetName;
        _enabled = watcher.Enabled;
    }

    public WatchFolder Watcher { get; }
    public string PresetName { get; }
    public string Path => Watcher.Path;
    public string When => Watcher.Extensions.Count == 0 ? "Any new file" : "New " + string.Join(", ", Watcher.Extensions);
    public string Do => PresetName;
    public string SaveTo => Watcher.OutputFolder ?? "Beside original";

    public string Status
    {
        get
        {
            if (!Directory.Exists(Watcher.Path))
            {
                return "The folder does not exist.";
            }
            if (Watcher.LastError is { } error && Watcher.Enabled)
            {
                return error;
            }
            var processed = $"{Watcher.ProcessedCount} file(s) processed";
            if (Watcher.LastProcessed is { } last)
            {
                processed += " · last " + RelativeTimeConverter.Format(last);
            }
            return Watcher.Enabled ? processed + " · waits for writes to finish" : "Off · " + processed;
        }
    }

    public bool HasProblem => !Directory.Exists(Watcher.Path) || Watcher.Enabled && Watcher.LastError is not null;

    [ObservableProperty] private bool _enabled;

    partial void OnEnabledChanged(bool value) => _owner.SetEnabled(this, value);

    [RelayCommand] private void Delete() => _owner.Delete(this);
}

/// <summary>Watch folders: "when a new file appears here, run this preset". Off until the user turns one on.</summary>
public sealed partial class AutomationViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly DialogService _dialogs;

    public AutomationViewModel(ISettingsStore settings, DialogService dialogs, AutomationService automation)
    {
        _settings = settings;
        _dialogs = dialogs;
        automation.StatisticsChanged += (_, _) => Ui.Post(Refresh);
        Refresh();
    }

    public ObservableCollection<WatcherViewModel> Watchers { get; } = [];
    public ObservableCollection<ConversionPreset> Presets { get; } = [];

    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string? _message;

    // New watcher form
    [ObservableProperty] private string _newPath = "";
    [ObservableProperty] private string _newExtensions = "";
    [ObservableProperty] private ConversionPreset? _newPreset;
    [ObservableProperty] private bool _newBesideOriginal = true;
    [ObservableProperty] private string _newOutputFolder = "";

    public void Refresh()
    {
        var settings = _settings.Current;
        Presets.Clear();
        foreach (var preset in settings.Presets)
        {
            Presets.Add(preset);
        }
        Watchers.Clear();
        foreach (var watcher in settings.Watchers)
        {
            var preset = settings.Presets.FirstOrDefault(p => p.Id == watcher.PresetId);
            Watchers.Add(new WatcherViewModel(this, watcher, preset?.Name ?? "(deleted preset)"));
        }
        IsEmpty = Watchers.Count == 0;
    }

    [RelayCommand]
    private void NewWatcher()
    {
        NewPath = "";
        NewExtensions = "";
        NewPreset = Presets.FirstOrDefault();
        NewBesideOriginal = true;
        NewOutputFolder = "";
        Message = null;
        IsEditing = true;
    }

    [RelayCommand] private void CancelNew() => IsEditing = false;

    [RelayCommand]
    private void BrowseNewPath()
    {
        if (_dialogs.PickFolder("Folder to watch", NewPath) is { } folder)
        {
            NewPath = folder;
        }
    }

    [RelayCommand]
    private void BrowseNewOutput()
    {
        if (_dialogs.PickFolder("Save converted files to", NewOutputFolder) is { } folder)
        {
            NewOutputFolder = folder;
            NewBesideOriginal = false;
        }
    }

    [RelayCommand]
    private void SaveNew()
    {
        if (!Directory.Exists(NewPath))
        {
            Message = "Choose a folder that exists.";
            return;
        }
        if (NewPreset is null)
        {
            Message = "Choose a preset to run.";
            return;
        }
        if (!NewBesideOriginal && !Directory.Exists(NewOutputFolder))
        {
            Message = "Choose an output folder that exists, or save beside the original.";
            return;
        }
        var watcher = new WatchFolder
        {
            Path = Path.GetFullPath(NewPath),
            Extensions = WatchFolder.ParseExtensions(NewExtensions),
            PresetId = NewPreset.Id,
            OutputFolder = NewBesideOriginal ? null : Path.GetFullPath(NewOutputFolder),
            Enabled = false, // the user turns it on explicitly
        };
        Save(s => s.Watchers.Add(watcher));
        IsEditing = false;
        Message = "Watcher added. Turn it on with its switch.";
    }

    internal void SetEnabled(WatcherViewModel item, bool enabled)
    {
        if (item.Watcher.Enabled == enabled)
        {
            return;
        }
        Save(s =>
        {
            var watcher = s.Watchers.FirstOrDefault(w => w.Id == item.Watcher.Id);
            if (watcher is not null)
            {
                watcher.Enabled = enabled;
                watcher.LastError = null;
            }
        });
    }

    internal void Delete(WatcherViewModel item)
    {
        if (_dialogs.Confirm("Delete watcher", $"Stop watching {item.Path}? Files already converted are kept."))
        {
            Save(s => s.Watchers.RemoveAll(w => w.Id == item.Watcher.Id));
        }
    }

    private void Save(Action<AppSettings> change)
    {
        var settings = _settings.Current;
        change(settings);
        try
        {
            _settings.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Error("Automation", "Your settings could not be saved: " + ex.Message);
        }
        Refresh();
    }
}
