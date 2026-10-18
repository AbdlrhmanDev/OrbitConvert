using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrbitConvert.App.Services;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;
using OrbitConvert.Core.Settings;

namespace OrbitConvert.App.ViewModels;

public enum RadialState { Loading, Choosing, Working, Succeeded, Failed, Empty }

public sealed partial class RadialItemViewModel : ObservableObject
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Category { get; init; }
    public string? Glyph { get; init; }
    public string? Description { get; init; }
    public string? Limitations { get; init; }
    public bool Experimental { get; init; }
    public bool IsMore { get; init; }
    public int Index { get; init; }

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    [ObservableProperty]
    private bool _isSelected;

    public string AccessibleName => IsMore ? "More options" : $"Convert to {Label}";

    public string ToolTip =>
        string.Join("\n", new[] { Description, Experimental ? "Experimental." : null, Limitations }.Where(s => !string.IsNullOrEmpty(s)));
}

/// <summary>
/// The floating radial menu. Shows only targets that are available and runnable with defaults,
/// lays them out in one or two rings, and runs the chosen conversion through the shared queue.
/// </summary>
public sealed partial class RadialMenuViewModel : ObservableObject
{
    public const double ItemSize = 66;
    private const double InnerRadius = 116;
    private const double OuterRadius = 196;
    private const int InnerCapacity = 8;
    private const double Margin = 18;

    private readonly IReadOnlyList<string> _paths;
    private readonly ConversionCoordinator _coordinator;
    private readonly ISettingsStore _settings;
    private IReadOnlyList<InputFile> _inputs = [];
    private ConversionBatch? _batch;

    public RadialMenuViewModel(IReadOnlyList<string> paths, ConversionCoordinator coordinator, ISettingsStore settings)
    {
        _paths = paths;
        _coordinator = coordinator;
        _settings = settings;
        Title = paths.Count == 1 ? Path.GetFileName(paths[0]) : $"{paths.Count} files";
        Diameter = 2 * (InnerRadius + ItemSize / 2 + Margin);
    }

    public ObservableCollection<RadialItemViewModel> Items { get; } = [];

    public IReadOnlyList<string> Paths => _paths;

    public string Title { get; }

    [ObservableProperty]
    private string _subtitle = "Reading…";

    [ObservableProperty]
    private string _centerLabel = "";

    [ObservableProperty]
    private string _centerCategory = "Other";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChoosing), nameof(IsWorking), nameof(IsSucceeded), nameof(IsFailed), nameof(ShowCenterInfo))]
    private RadialState _state = RadialState.Loading;

    [ObservableProperty]
    private double _progress = -1;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private double _diameter;

    [ObservableProperty]
    private RadialItemViewModel? _selected;

    public double CenterSize => Math.Max(120, 2 * (InnerRadius - ItemSize / 2 - 10));

    public bool IsChoosing => State is RadialState.Choosing or RadialState.Empty or RadialState.Loading;
    public bool IsWorking => State == RadialState.Working;
    public bool IsSucceeded => State == RadialState.Succeeded;
    public bool IsFailed => State == RadialState.Failed;
    public bool ShowCenterInfo => State is RadialState.Choosing or RadialState.Loading or RadialState.Empty;

    /// <summary>The window should close (fade out).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Open the full app with these files (and optionally a preselected target).</summary>
    public event EventHandler<(IReadOnlyList<string> Files, string? TargetKey)>? OpenMainRequested;

    public async Task LoadAsync()
    {
        var registry = _coordinator.Registry;
        (IReadOnlyList<InputFile> inputs, IReadOnlyList<ConversionTarget> targets) = await Task.Run(() =>
        {
            var analyzed = registry.Analyze(_paths);
            return (analyzed, registry.GetTargets(analyzed));
        });
        _inputs = inputs;

        var formats = inputs.Select(i => i.Format).Distinct().ToList();
        var single = formats.Count == 1 ? formats[0] : null;
        CenterLabel = single?.Label ?? (inputs.Count > 1 ? "MIX" : "?");
        CenterCategory = single?.Category.ToString() ?? "Other";
        Subtitle = inputs.Count switch
        {
            1 when single is not null => single.DisplayName,
            1 => inputs[0].Detection?.Warning ?? "Unknown type",
            _ when single is not null => $"{single.Label} files",
            _ => "Mixed types",
        };

        var usable = targets.Where(t => t.IsAvailable && !t.RequiresOptions).ToList();
        var index = 0;
        foreach (var t in usable)
        {
            Items.Add(new RadialItemViewModel
            {
                Key = t.Key,
                Label = t.Label,
                Category = (t.Output?.Category ?? single?.Category)?.ToString() ?? "Other",
                Glyph = GlyphFor(t),
                Description = t.Description,
                Limitations = t.Limitations,
                Experimental = t.Experimental,
                Index = index++,
            });
        }
        Items.Add(new RadialItemViewModel
        {
            Key = "more",
            Label = "More",
            Category = "Other",
            Glyph = "",
            Description = targets.Count > usable.Count
                ? $"Open OrbitConvert for options and {targets.Count - usable.Count} more target(s)"
                : "Open OrbitConvert for options",
            IsMore = true,
            Index = index,
        });

        Layout();
        State = usable.Count == 0 ? RadialState.Empty : RadialState.Choosing;
        if (State == RadialState.Empty)
        {
            StatusText = targets.Count == 0 ? "No conversions for this file type" : "Needs tools or options — open OrbitConvert";
        }
        Selected = Items.FirstOrDefault();
        if (Selected is not null)
        {
            Selected.IsSelected = true;
        }
    }

    private void Layout()
    {
        var n = Items.Count;
        var inner = Math.Min(n, InnerCapacity);
        var outer = n - inner;
        var maxRadius = outer > 0 ? OuterRadius : InnerRadius;
        Diameter = 2 * (maxRadius + ItemSize / 2 + Margin);
        var c = Diameter / 2;

        for (var i = 0; i < n; i++)
        {
            var ring = i < inner ? 0 : 1;
            var countInRing = ring == 0 ? inner : outer;
            var position = ring == 0 ? i : i - inner;
            var radius = ring == 0 ? InnerRadius : OuterRadius;
            // Start at 12 o'clock and go clockwise. A sparse outer ring keeps a fixed spacing so a
            // few extra items sit together near the top instead of scattering around the circle.
            var step = ring == 0 ? 2 * Math.PI / countInRing : 2 * Math.PI / Math.Max(countInRing, 12);
            var angle = -Math.PI / 2 + position * step + (ring == 1 ? step / 2 : 0);
            Items[i].X = c + radius * Math.Cos(angle) - ItemSize / 2;
            Items[i].Y = c + radius * Math.Sin(angle) - ItemSize / 2;
        }
        OnPropertyChanged(nameof(CenterSize));
    }

    public void MoveSelection(int delta)
    {
        if (Items.Count == 0 || State is not (RadialState.Choosing or RadialState.Empty))
        {
            return;
        }
        var current = Selected?.Index ?? 0;
        var next = ((current + delta) % Items.Count + Items.Count) % Items.Count;
        SelectItem(Items[next]);
    }

    public void SelectItem(RadialItemViewModel item)
    {
        if (Selected is not null)
        {
            Selected.IsSelected = false;
        }
        Selected = item;
        item.IsSelected = true;
    }

    [RelayCommand]
    public void Activate(RadialItemViewModel? item)
    {
        item ??= Selected;
        if (item is null || State is not (RadialState.Choosing or RadialState.Empty))
        {
            return;
        }
        if (item.IsMore)
        {
            OpenMainRequested?.Invoke(this, (_paths, null));
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        SelectItem(item);
        try
        {
            _batch = _coordinator.Start(_inputs, item.Key);
        }
        catch (ConversionException ex)
        {
            Fail(ex.Message);
            return;
        }

        if (_batch.Jobs.Count == 0)
        {
            State = RadialState.Succeeded;
            StatusText = _batch.Skipped > 0 ? $"Already {item.Label}" : "Nothing to do";
            ScheduleClose();
            return;
        }

        State = RadialState.Working;
        StatusText = $"Converting to {item.Label}…";
        Progress = -1;
        _batch.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConversionBatch.Progress))
            {
                var p = _batch.Progress;
                Ui.Post(() => Progress = p);
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
        if (_batch.FailedCount == 0)
        {
            State = RadialState.Succeeded;
            var count = _batch.SucceededCount;
            StatusText = count == 1 ? $"Saved {Path.GetFileName(_batch.Outputs.FirstOrDefault() ?? "")}" : $"{count} files converted";
            ScheduleClose();
        }
        else
        {
            Fail(_batch.FirstError ?? "The conversion failed.");
        }
    }

    private void Fail(string message)
    {
        State = RadialState.Failed;
        StatusText = "Conversion failed";
        ErrorText = message;
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
        var first = _batch?.Outputs.FirstOrDefault();
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

    internal static string? GlyphFor(ConversionTarget t) => t.Key switch
    {
        "pdf-merge" or "pdf-combine-images" => "",
        "pdf-split" => "",
        "pdf-rotate" => "",
        "pdf-compress" or "compress-video" => "",
        "extract" => "",
        _ => null,
    };
}
