using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrbitConvert.App.Services;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.History;
using OrbitConvert.Core.Settings;
using OrbitConvert.Shell;

namespace OrbitConvert.App.ViewModels;

public enum HistoryFilter { All, Succeeded, Failed }

public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly IHistoryStore _history;
    private readonly ConversionCoordinator _coordinator;
    private readonly DialogService _dialogs;
    private readonly ISettingsStore _settings;

    public HistoryViewModel(IHistoryStore history, ConversionCoordinator coordinator, DialogService dialogs, ISettingsStore settings)
    {
        _history = history;
        _coordinator = coordinator;
        _dialogs = dialogs;
        _settings = settings;
        _history.Changed += (_, _) => Ui.Post(Refresh);
        Refresh();
    }

    public ObservableCollection<HistoryEntry> Entries { get; } = [];

    [ObservableProperty]
    private HistoryFilter _filter = HistoryFilter.All;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _isEmpty;

    public bool HistoryDisabled => !_settings.Current.KeepHistory;

    partial void OnFilterChanged(HistoryFilter value) => Refresh();

    public void Refresh()
    {
        Entries.Clear();
        foreach (var entry in _history.Entries.Where(e => Filter switch
                 {
                     HistoryFilter.Succeeded => e.Succeeded,
                     HistoryFilter.Failed => !e.Succeeded,
                     _ => true,
                 }))
        {
            Entries.Add(entry);
        }
        IsEmpty = Entries.Count == 0;
        OnPropertyChanged(nameof(HistoryDisabled));
    }

    [RelayCommand]
    private void Reveal(HistoryEntry entry)
    {
        var target = entry.Outputs.FirstOrDefault(p => File.Exists(p) || Directory.Exists(p));
        if (target is null)
        {
            Message = "The output no longer exists at its saved location.";
            target = entry.Inputs.FirstOrDefault();
        }
        if (target is not null)
        {
            ShellLauncher.RevealInExplorer(target);
        }
    }

    [RelayCommand]
    private void Retry(HistoryEntry entry)
    {
        var missing = entry.Inputs.Where(p => !File.Exists(p)).ToList();
        if (missing.Count > 0)
        {
            Message = $"Cannot retry: {Path.GetFileName(missing[0])} no longer exists.";
            return;
        }
        try
        {
            var inputs = _coordinator.Registry.Analyze(entry.Inputs);
            var batch = _coordinator.Start(inputs, entry.TargetKey);
            Message = $"Retrying {entry.InputSummary} → {entry.TargetLabel} ({batch.Jobs.Count} job(s) queued).";
        }
        catch (ConversionException ex)
        {
            Message = ex.Message;
        }
    }

    [RelayCommand]
    private void Remove(HistoryEntry entry) => _history.Remove(entry.Id);

    [RelayCommand]
    private void Clear()
    {
        if (_dialogs.Confirm("Clear history", "Remove all conversion history? Converted files are not deleted."))
        {
            _history.Clear();
            Message = "History cleared.";
        }
    }
}
