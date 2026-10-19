using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrbitConvert.App.Services;
using OrbitConvert.Core.Presets;
using OrbitConvert.Core.Settings;

namespace OrbitConvert.App.ViewModels;

public sealed partial class PresetCardViewModel(PresetsViewModel owner, ConversionPreset preset) : ObservableObject
{
    public ConversionPreset Preset { get; } = preset;
    public string Name => Preset.Name;
    public string Tag => Preset.Tag;
    public string Category => Preset.Category;
    public string Summary => Preset.Summary;
    public bool CanDelete => !Preset.BuiltIn;
    public bool Starred => Preset.Starred;
    public string StarToolTip => Starred ? "Remove from the Orbit Menu" : "Show in the Orbit Menu";

    [RelayCommand] private void ToggleStar() => owner.ToggleStar(this);
    [RelayCommand] private void Run() => owner.Run(this);
    [RelayCommand] private void Delete() => owner.Delete(this);
}

/// <summary>Saved conversions. Starred presets appear in the Orbit Menu for files they apply to.</summary>
public sealed partial class PresetsViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly DialogService _dialogs;
    private readonly PresetRunner _runner;

    public PresetsViewModel(ISettingsStore settings, DialogService dialogs, PresetRunner runner)
    {
        _settings = settings;
        _dialogs = dialogs;
        _runner = runner;
        Refresh();
    }

    public Action<Page>? Navigate { get; set; }

    public ObservableCollection<PresetCardViewModel> Cards { get; } = [];

    [ObservableProperty] private string? _message;

    public void Refresh()
    {
        Cards.Clear();
        foreach (var preset in _settings.Current.Presets)
        {
            Cards.Add(new PresetCardViewModel(this, preset));
        }
    }

    public void RunById(string id)
    {
        Navigate?.Invoke(Page.Presets);
        var card = Cards.FirstOrDefault(c => c.Preset.Id == id);
        if (card is not null)
        {
            Run(card);
        }
    }

    internal void Run(PresetCardViewModel card)
    {
        var run = _runner.PickAndRun(card.Preset);
        Message = run.Message;
        if (run.Started)
        {
            Navigate?.Invoke(Page.Batch);
        }
    }

    internal void ToggleStar(PresetCardViewModel card) =>
        Save(s =>
        {
            var preset = s.Presets.FirstOrDefault(p => p.Id == card.Preset.Id);
            if (preset is not null)
            {
                preset.Starred = !preset.Starred;
            }
        });

    internal void Delete(PresetCardViewModel card)
    {
        if (card.Preset.BuiltIn)
        {
            return;
        }
        var watchers = _settings.Current.Watchers.Count(w => w.PresetId == card.Preset.Id);
        var warning = watchers > 0 ? $"\n\n{watchers} watch folder(s) use it and will stop converting." : "";
        if (_dialogs.Confirm("Delete preset", $"Delete \"{card.Name}\"?{warning}"))
        {
            Save(s => s.Presets.RemoveAll(p => p.Id == card.Preset.Id));
        }
    }

    /// <summary>Adds a user preset saved from the Convert page.</summary>
    public void Add(ConversionPreset preset)
    {
        Save(s => s.Presets.Add(preset));
        Message = $"Saved preset \"{preset.Name}\".";
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
            _dialogs.Error("Presets", "Your settings could not be saved: " + ex.Message);
        }
        Refresh();
    }
}
