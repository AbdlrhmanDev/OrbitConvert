using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrbitConvert.App.Converters;
using OrbitConvert.App.Services;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;
using OrbitConvert.Core.Settings;

namespace OrbitConvert.App.ViewModels;

public enum RadialState { Loading, Choosing, Working, Succeeded, Failed, Empty }

/// <summary>What the wheel offers: formats to convert to, or actions on a single image.</summary>
public enum OrbitMode { Convert, Actions }

public sealed record ImageActionRequest(string Path, string Action);

public sealed partial class RadialItemViewModel : ObservableObject
{
    public required string Key { get; init; }

    /// <summary>Short uppercase label drawn in the segment ("JPG", "CROP").</summary>
    public required string Label { get; init; }

    /// <summary>One- or two-word hint under the label ("Lossless", "Per page").</summary>
    public string Hint { get; init; } = "";

    public required string Category { get; init; }
    public string? Glyph { get; init; }
    public string? Description { get; init; }
    public string? Limitations { get; init; }
    public bool Experimental { get; init; }
    public bool IsMore { get; init; }
    public string? PresetId { get; init; }
    public int Index { get; init; }

    [ObservableProperty]
    private bool _isSelected;

    public string AccessibleName => IsMore ? "More options in OrbitConvert" : $"{Label}: {Hint}";

    public string ToolTip =>
        string.Join("\n", new[] { Description, Experimental ? "Experimental." : null, Limitations }.Where(s => !string.IsNullOrEmpty(s)));
}

/// <summary>
/// The Orbit Menu (Identity v1.0): up to eight annular segments around a core that always says
/// what will happen. Only conversions the installed engines can run with defaults are offered.
/// </summary>
public sealed partial class RadialMenuViewModel : ObservableObject
{
    /// <summary>Window size in DIPs; the wheel itself is 380 (186-unit halo on a 400-unit grid) plus room for its shadow.</summary>
    public const double WindowSize = 440;
    private const int MaxSegments = 8;

    private IReadOnlyList<string> _paths;
    private readonly ConversionCoordinator _coordinator;
    private readonly ISettingsStore _settings;
    private readonly bool _showPresets;
    private IReadOnlyList<InputFile> _inputs = [];
    private IReadOnlyList<ConversionTarget> _targets = [];
    private ConversionBatch? _batch;

    public RadialMenuViewModel(IReadOnlyList<string> paths, ConversionCoordinator coordinator, ISettingsStore settings, bool showPresets = true)
    {
        _paths = paths;
        _coordinator = coordinator;
        _settings = settings;
        _showPresets = showPresets;
    }

    /// <summary>Starred presets that can run on these files, as segments (labelled with the preset's menu label).</summary>
    private List<(string Label, string Hint, string Category, string Description, string Id)> StarredPresets() =>
        !_showPresets ? [] : _coordinator.Presets
            .Where(p => p.Starred && _coordinator.PresetApplies(p, _inputs, _targets))
            .Select(p => (string.IsNullOrWhiteSpace(p.MenuLabel) ? p.Name.ToUpperInvariant() : p.MenuLabel.ToUpperInvariant(),
                "Preset", p.Category, $"{p.Name}: {p.Summary.ReplaceLineEndings(" ")}", p.Id))
            .Take(2)
            .ToList();

    public ObservableCollection<RadialItemViewModel> Items { get; } = [];

    public IReadOnlyList<string> Paths => _paths;

    public double Diameter => WindowSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChoosing), nameof(IsWorking), nameof(IsSucceeded), nameof(IsFailed))]
    private RadialState _state = RadialState.Loading;

    [ObservableProperty]
    private OrbitMode _mode = OrbitMode.Convert;

    /// <summary>0..1 while converting (drawn as the lime ring around the core), -1 when idle.</summary>
    [ObservableProperty]
    private double _progress = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Hovered))]
    private RadialItemViewModel? _selected;

    /// <summary>True while the pointer or keyboard is on a segment; the core then previews that choice.</summary>
    [ObservableProperty]
    private bool _isHovering;

    [ObservableProperty] private string _coreTop = "";
    [ObservableProperty] private string _coreMain = "";
    [ObservableProperty] private string _coreSub = "";
    [ObservableProperty] private string? _errorText;

    public RadialItemViewModel? Hovered => IsHovering ? Selected : null;

    public bool IsChoosing => State is RadialState.Choosing or RadialState.Empty or RadialState.Loading;
    public bool IsWorking => State == RadialState.Working;
    public bool IsSucceeded => State == RadialState.Succeeded;
    public bool IsFailed => State == RadialState.Failed;

    public event EventHandler? CloseRequested;
    public event EventHandler<(IReadOnlyList<string> Files, string? TargetKey)>? OpenMainRequested;
    public event EventHandler<ImageActionRequest>? ImageActionRequested;

    private bool IsSingleImage => _inputs.Count == 1 && _inputs[0].Format?.Category == FormatCategory.Image;

    public async Task LoadAsync()
    {
        var registry = _coordinator.Registry;
        var paths = _paths;
        (_inputs, _targets) = await Task.Run(() =>
        {
            var analyzed = registry.Analyze(paths);
            return (analyzed, registry.GetTargets(analyzed));
        });
        Mode = IsSingleImage ? OrbitMode.Actions : OrbitMode.Convert;
        BuildItems();
    }

    private void BuildItems()
    {
        Items.Clear();
        Selected = null;
        IsHovering = false;
        var usable = _targets.Where(t => t.IsAvailable && !t.RequiresOptions).ToList();
        var single = _inputs.Count == 1 ? _inputs[0].Format : _inputs.Select(i => i.Format).Distinct().Count() == 1 ? _inputs[0].Format : null;
        var list = new List<RadialItemViewModel>();
        var index = 0;

        RadialItemViewModel Item(string key, string label, string hint, string category, string? description = null,
            string? limitations = null, bool experimental = false, bool more = false, string? presetId = null) =>
            new()
            {
                Key = key, Label = label, Hint = hint, Category = category, Description = description,
                Limitations = limitations, Experimental = experimental, IsMore = more, PresetId = presetId, Index = index++,
            };

        if (Mode == OrbitMode.Actions)
        {
            list.Add(Item("action-crop", "CROP", "Ratio", "Image", "Crop with handles and aspect ratios; the original is kept"));
            if (usable.FirstOrDefault(t => t.Key == "image-shrink") is { } shrink)
            {
                list.Add(Item(shrink.Key, "SHRINK", "Compress", "Image", shrink.Description, shrink.Limitations));
            }
            if (usable.FirstOrDefault(t => t.Key == "image-strip") is { } strip)
            {
                list.Add(Item(strip.Key, "META", "EXIF", "Image", strip.Description, strip.Limitations));
            }
            list.Add(Item("action-background", "BG", "Background", "Image", "Gradient or color, padding, corners and shadow"));
            list.AddRange(StarredPresets().Select(p => Item("preset:" + p.Id, p.Label, p.Hint, p.Category, p.Description, presetId: p.Id)));
            list.Add(Item("mode-convert", "CONVERT", "Formats", "Other", "Convert this image to another format"));
        }
        else
        {
            var formats = usable.Where(t => t.Key is not ("image-shrink" or "image-strip")).ToList();
            var presets = StarredPresets();
            var reserved = (IsSingleImage ? 1 : 0) + presets.Count;
            var room = MaxSegments - reserved;
            var needsMore = formats.Count > room || _targets.Count > usable.Count;
            var take = needsMore ? room - 1 : room;
            foreach (var t in formats.Take(Math.Max(0, take)))
            {
                list.Add(Item(t.Key, ShortLabel(t), HintFor(t, single), (t.Output?.Category ?? single?.Category)?.ToString() ?? "Other",
                    t.Description, t.Limitations, t.Experimental));
            }
            list.AddRange(presets.Select(p => Item("preset:" + p.Id, p.Label, p.Hint, p.Category, p.Description, presetId: p.Id)));
            if (IsSingleImage)
            {
                list.Add(Item("mode-actions", "ACTIONS", "Edit image", "Other", "Crop, shrink, strip metadata, add a background"));
            }
            if (needsMore)
            {
                list.Add(Item("more", "MORE", $"{_targets.Count - Math.Max(0, take)} in app", "Other",
                    "Open OrbitConvert for every format and option", more: true));
            }
        }

        foreach (var item in list)
        {
            Items.Add(item);
        }
        State = Items.Count == 0 || (Mode == OrbitMode.Convert && usable.Count == 0) ? RadialState.Empty : RadialState.Choosing;
        UpdateCore();
    }

    partial void OnSelectedChanged(RadialItemViewModel? value) => UpdateCore();

    partial void OnIsHoveringChanged(bool value)
    {
        OnPropertyChanged(nameof(Hovered));
        UpdateCore();
    }

    /// <summary>The core always tells the user what will happen.</summary>
    private void UpdateCore()
    {
        if (State is RadialState.Working or RadialState.Succeeded or RadialState.Failed)
        {
            return;
        }
        if (Hovered is { } h)
        {
            (CoreTop, CoreMain, CoreSub) = h.IsMore ? ("OPEN", "More", h.Hint)
                : h.Key.StartsWith("mode-", StringComparison.Ordinal) ? ("SWITCH TO", h.Label == "CONVERT" ? "Formats" : "Actions", h.Hint)
                : Mode == OrbitMode.Actions ? ("OPEN", h.Label, h.Hint)
                : ("CONVERT TO", h.Label, h.Hint);
            return;
        }
        var top = _paths.Count == 1 ? Path.GetFileName(_paths[0]).ToUpperInvariant() : $"{_paths.Count} FILES";
        if (State == RadialState.Loading)
        {
            (CoreTop, CoreMain, CoreSub) = (top, "Reading…", "");
            return;
        }
        if (State == RadialState.Empty)
        {
            (CoreTop, CoreMain, CoreSub) = (top, "Nothing here", _targets.Count == 0 ? "no conversions" : "needs a tool");
            return;
        }
        var size = Converters.FileSizeConverter.Format(_inputs.Sum(i => i.Length));
        (CoreTop, CoreMain, CoreSub) = (top, Mode == OrbitMode.Actions ? "Actions" : "Convert to", size);
    }

    public void MoveSelection(int delta)
    {
        if (Items.Count == 0 || !IsChoosing)
        {
            return;
        }
        var current = Selected?.Index ?? (delta > 0 ? -1 : 0);
        var next = ((current + delta) % Items.Count + Items.Count) % Items.Count;
        SelectItem(Items[next]);
        IsHovering = true;
    }

    public void SelectItem(RadialItemViewModel? item)
    {
        if (Selected is not null)
        {
            Selected.IsSelected = false;
        }
        Selected = item;
        if (item is not null)
        {
            item.IsSelected = true;
        }
    }

    [RelayCommand]
    public void Activate(RadialItemViewModel? item)
    {
        item ??= Selected;
        if (item is null || !IsChoosing)
        {
            return;
        }
        switch (item.Key)
        {
            case "more":
                OpenMainRequested?.Invoke(this, (_paths, null));
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return;
            case "mode-convert":
                Mode = OrbitMode.Convert;
                BuildItems();
                return;
            case "mode-actions":
                Mode = OrbitMode.Actions;
                BuildItems();
                return;
            case "action-crop" or "action-background" when _paths.Count == 1:
                ImageActionRequested?.Invoke(this, new ImageActionRequest(_paths[0], item.Key[7..]));
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return;
        }
        if (item.PresetId is { } presetId)
        {
            StartBatch(null, item, presetId);
            return;
        }
        StartBatch(item.Key, item, null);
    }

    private void StartBatch(string? targetKey, RadialItemViewModel item, string? presetId)
    {
        SelectItem(item);
        try
        {
            _batch = presetId is null ? _coordinator.Start(_inputs, targetKey!) : _coordinator.StartPreset(_inputs, presetId);
        }
        catch (ConversionException ex)
        {
            Fail(ex.Message);
            return;
        }

        if (_batch.Jobs.Count == 0)
        {
            State = RadialState.Succeeded;
            (CoreTop, CoreMain, CoreSub) = ("NOTHING TO DO", _batch.Skipped > 0 ? $"Already {item.Label}" : "—", "");
            ScheduleClose();
            return;
        }

        State = RadialState.Working;
        Progress = 0;
        (CoreTop, CoreMain, CoreSub) = (Mode == OrbitMode.Actions ? "WORKING" : "CONVERTING", "0%", "→ " + item.Label);
        _batch.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConversionBatch.Progress))
            {
                var p = _batch.Progress;
                Ui.Post(() =>
                {
                    if (State != RadialState.Working)
                    {
                        return;
                    }
                    Progress = Math.Max(0, p);
                    CoreMain = p < 0 ? "…" : $"{p * 100:0}%";
                });
            }
        };
        _batch.Completed += (_, _) => Ui.Post(OnBatchCompleted);
        if (_batch.IsCompleted)
        {
            OnBatchCompleted();
        }
    }

    private void OnBatchCompleted()
    {
        if (_batch is null || State != RadialState.Working)
        {
            return;
        }
        Progress = 1;
        if (_batch.FailedCount > 0)
        {
            Fail(_batch.FirstError ?? "The conversion failed.");
            return;
        }

        State = RadialState.Succeeded;
        if (_batch.SucceededCount == 0 && _batch.SkippedCount > 0)
        {
            // Collision setting is "Skip" and the output name already exists: say so plainly.
            (CoreTop, CoreMain, CoreSub) = ("SKIPPED", "Already exists", "nothing overwritten");
            ScheduleClose();
            return;
        }
        var outputs = _batch.Outputs.ToList();
        var first = outputs.FirstOrDefault();
        (CoreTop, CoreMain, CoreSub) = outputs.Count == 1 && first is not null
            ? ("SAVED", Path.GetFileName(first), _batch.Jobs[0].Note is { } note ? note : "beside original")
            : ("SAVED", $"{_batch.SucceededCount} files", "beside originals");

        // A single image result chains straight into image actions, so a PDF can become a JPG and
        // then be cropped and given a background without leaving the menu.
        if (outputs.Count == 1 && File.Exists(first) && KnownFormats.FindByPath(first)?.Category == FormatCategory.Image)
        {
            _ = ContinueWithAsync(first);
        }
        else
        {
            ScheduleClose();
        }
    }

    private async Task ContinueWithAsync(string output)
    {
        await Task.Delay(900);
        if (State != RadialState.Succeeded)
        {
            return;
        }
        _paths = [output];
        _batch = null;
        Progress = -1;
        State = RadialState.Loading;
        Mode = OrbitMode.Actions;
        await LoadAsync();
        OnPropertyChanged(nameof(Paths));
    }

    private void Fail(string message)
    {
        State = RadialState.Failed;
        Progress = -1;
        ErrorText = message;
        (CoreTop, CoreMain, CoreSub) = ("FAILED", "Not converted", "see details");
    }

    private async void ScheduleClose()
    {
        if (!_settings.Current.CloseRadialAfterSuccess)
        {
            return;
        }
        await Task.Delay(1400);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RevealOutput()
    {
        var first = _batch?.Outputs.FirstOrDefault() ?? (_paths.Count == 1 ? _paths[0] : null);
        if (first is not null)
        {
            Shell.ShellLauncher.RevealInExplorer(first);
        }
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ShowDetails()
    {
        OpenMainRequested?.Invoke(this, ([], "history"));
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Segment labels are short, uppercase and set in the mono voice.</summary>
    internal static string ShortLabel(ConversionTarget t) => t.Key switch
    {
        "pdf-merge" => "MERGE",
        "pdf-combine-images" => "PDF",
        "pdf-split" => "SPLIT",
        "pdf-rotate" => "ROTATE",
        "pdf-compress" or "compress-video" or "image-shrink" => "SHRINK",
        "image-strip" => "META",
        "extract" => "EXTRACT",
        _ => t.Label.ToUpperInvariant(),
    };

    internal static string HintFor(ConversionTarget t, FileFormat? input)
    {
        var fromPdf = input == KnownFormats.Pdf;
        return t.Key switch
        {
            "png" => fromPdf ? "Per page" : input?.Category == FormatCategory.Image ? "Lossless" : "PNG",
            "jpeg" => fromPdf ? "Per page" : "Universal",
            "webp" => fromPdf ? "Per page" : "Web",
            "avif" => "Smallest",
            "tiff" => "Print",
            "bmp" => "Raw",
            "ico" => "Icon",
            "gif" => input?.Category == FormatCategory.Video ? "Loop" : "Animated",
            "pdf" => "Document",
            "docx" => fromPdf ? "Layout may shift" : "Word",
            "odt" => "OpenDocument",
            "html" => "Web page",
            "md" => "Markdown",
            "epub" => "E-book",
            "txt" => "Text only",
            "mp4" => "H.264",
            "mkv" => "Matroska",
            "mov" => "QuickTime",
            "avi" => "Legacy",
            "webm" => "VP9",
            "mp3" => "Audio",
            "wav" => "Uncompressed",
            "flac" => "Lossless",
            "aac" => "ADTS",
            "ogg" => "Vorbis",
            "m4a" => "AAC",
            "opus" => "Smallest",
            "zip" => "Archive",
            "7z" => "Archive",
            "extract" => "To folder",
            "pdf-merge" => "Combine",
            "pdf-combine-images" => "One document",
            "pdf-split" => "Pages",
            "pdf-rotate" => "90°",
            "pdf-compress" or "compress-video" or "image-shrink" => "Compress",
            "image-strip" => "EXIF",
            _ => t.Output?.Label ?? "",
        };
    }

    internal static string? GlyphFor(ConversionTarget t) => t.Key switch
    {
        "pdf-merge" or "pdf-combine-images" => "",
        "pdf-split" => "",
        "pdf-extract" => "",
        "pdf-rotate" => "",
        "pdf-compress" or "compress-video" or "image-shrink" => "",
        "image-strip" => "",
        "extract" => "",
        "zip" or "7z" => "",
        _ => null,
    };
}
