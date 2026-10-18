using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OrbitConvert.App.Converters;
using OrbitConvert.App.Services;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;
using OrbitConvert.Core.Queue;
using OrbitConvert.Core.Settings;
using OrbitConvert.Shell;

namespace OrbitConvert.App.ViewModels;

public sealed record Choice<T>(string Label, T Value);

public sealed partial class StagedFileViewModel(InputFile input, ConversionOptions options) : ObservableObject
{
    public InputFile Input { get; } = input;
    public ConversionOptions Options { get; } = options;
    public string Name => Input.FileName;
    public string Folder => Path.GetDirectoryName(Input.Path) ?? "";
    public string SizeText => FileSizeConverter.Format(Input.Length);
    public string TypeLabel => Input.Format?.Label ?? "?";
    public string TypeName => Input.Format?.DisplayName ?? "Unknown type";
    public string Category => Input.Format?.Category.ToString() ?? "Other";
    public string? Warning => Input.Detection?.Warning;
}

public sealed class TargetViewModel(ConversionTarget target, FormatCategory? inputCategory)
{
    public ConversionTarget Target { get; } = target;
    public string Key => Target.Key;
    public string Label => Target.Label;
    public string Category => (Target.Output?.Category ?? inputCategory)?.ToString() ?? "Other";
    public string? Glyph => RadialMenuViewModel.GlyphFor(Target);
    public bool IsAvailable => Target.IsAvailable;
    public bool Experimental => Target.Experimental;

    public string ToolTip => string.Join("\n", new[]
    {
        Target.Description,
        Target.IsAvailable ? null : "Unavailable: " + Target.UnavailableReason,
        Target.Experimental ? "Experimental." : null,
    }.Where(s => !string.IsNullOrEmpty(s)));
}

public sealed partial class ConvertViewModel : ObservableObject
{
    private readonly ConversionCoordinator _coordinator;
    private readonly ISettingsStore _settings;
    private readonly DialogService _dialogs;
    private readonly ILogger<ConvertViewModel> _logger;

    public ConvertViewModel(ConversionCoordinator coordinator, ISettingsStore settings, DialogService dialogs, ILogger<ConvertViewModel> logger)
    {
        _coordinator = coordinator;
        _settings = settings;
        _dialogs = dialogs;
        _logger = logger;
        _sharedOptions = settings.Current.Defaults.Clone();
        _saveBesideOriginal = settings.Current.OutputLocation == OutputLocationMode.BesideOriginal;
        _outputFolder = settings.Current.CustomOutputFolder;
        _collision = settings.Current.Collision;

        foreach (var job in coordinator.Queue.Jobs.Reverse())
        {
            Jobs.Add(job);
        }
        coordinator.Queue.JobAdded += (_, job) => Ui.Post(() => Jobs.Insert(0, job));
        coordinator.Queue.JobRemoved += (_, job) => Ui.Post(() => Jobs.Remove(job));
        Files.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasFiles));
            StartCommand.NotifyCanExecuteChanged();
        };
        Jobs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasJobs));
    }

    public ObservableCollection<StagedFileViewModel> Files { get; } = [];
    public ObservableCollection<TargetViewModel> Targets { get; } = [];
    public ObservableCollection<ConversionJob> Jobs { get; } = [];

    public bool HasFiles => Files.Count > 0;
    public bool HasJobs => Jobs.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditingOptions))]
    private StagedFileViewModel? _selectedFile;

    [ObservableProperty]
    private TargetViewModel? _selectedTarget;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditingOptions))]
    private ConversionOptions _sharedOptions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditingOptions), nameof(OptionsScopeText))]
    private bool _perFileOptions;

    [ObservableProperty]
    private bool _saveBesideOriginal;

    [ObservableProperty]
    private string? _outputFolder;

    [ObservableProperty]
    private CollisionStrategy _collision;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _targetsMessage;

    // Option panel visibility
    [ObservableProperty] private bool _showImageOptions;
    [ObservableProperty] private bool _showVideoOptions;
    [ObservableProperty] private bool _showGifOptions;
    [ObservableProperty] private bool _showAudioOptions;
    [ObservableProperty] private bool _showPdfPages;
    [ObservableProperty] private bool _showPdfRotate;
    [ObservableProperty] private bool _showPdfDpi;
    [ObservableProperty] private bool _showPdfCompression;
    [ObservableProperty] private bool _showDocumentOptions;
    [ObservableProperty] private bool _showArchiveOptions;
    [ObservableProperty] private bool _hasOptions;

    public ConversionOptions EditingOptions => PerFileOptions && SelectedFile is not null ? SelectedFile.Options : SharedOptions;

    public string OptionsScopeText => PerFileOptions
        ? SelectedFile is null ? "Select a file to edit its options" : $"Options for {SelectedFile.Name}"
        : "Options for all files";

    // Choices for option pickers
    public IReadOnlyList<Choice<int>> Rotations { get; } = [new("None", 0), new("90° clockwise", 90), new("180°", 180), new("90° counter-clockwise", 270)];
    public IReadOnlyList<Choice<int>> PdfRotations { get; } = [new("90° clockwise", 90), new("180°", 180), new("90° counter-clockwise", 270)];
    public IReadOnlyList<Choice<VideoResolution>> Resolutions { get; } =
    [
        new("Original", VideoResolution.Original), new("2160p (4K)", VideoResolution.P2160), new("1440p", VideoResolution.P1440),
        new("1080p", VideoResolution.P1080), new("720p", VideoResolution.P720), new("480p", VideoResolution.P480), new("360p", VideoResolution.P360),
    ];
    public IReadOnlyList<Choice<VideoQuality>> VideoQualities { get; } =
        [new("High quality", VideoQuality.High), new("Balanced", VideoQuality.Balanced), new("Smaller file", VideoQuality.Small)];
    public IReadOnlyList<Choice<VideoCodec>> Codecs { get; } =
        [new("Automatic", VideoCodec.Auto), new("H.264", VideoCodec.H264), new("H.265 / HEVC", VideoCodec.H265), new("VP9", VideoCodec.VP9), new("AV1 (slow)", VideoCodec.AV1)];
    public IReadOnlyList<Choice<int?>> FrameRates { get; } =
        [new("Original", null), new("24 fps", 24), new("25 fps", 25), new("30 fps", 30), new("50 fps", 50), new("60 fps", 60)];
    public IReadOnlyList<Choice<int?>> SampleRates { get; } =
        [new("Original", null), new("22.05 kHz", 22050), new("44.1 kHz", 44100), new("48 kHz", 48000), new("96 kHz", 96000)];
    public IReadOnlyList<Choice<int?>> ChannelChoices { get; } = [new("Original", null), new("Mono", 1), new("Stereo", 2)];
    public IReadOnlyList<Choice<int>> Bitrates { get; } =
        [new("96 kbps", 96), new("128 kbps", 128), new("160 kbps", 160), new("192 kbps", 192), new("256 kbps", 256), new("320 kbps", 320)];
    public IReadOnlyList<Choice<int>> Dpis { get; } = [new("72 dpi", 72), new("96 dpi", 96), new("150 dpi", 150), new("200 dpi", 200), new("300 dpi", 300)];
    public IReadOnlyList<Choice<PdfCompressionLevel>> PdfCompressionLevels { get; } =
    [
        new("Lossless (built in)", PdfCompressionLevel.Lossless),
        new("Balanced — 150 dpi images (needs Ghostscript)", PdfCompressionLevel.Balanced),
        new("Strong — 72 dpi images (needs Ghostscript)", PdfCompressionLevel.Strong),
    ];
    public IReadOnlyList<Choice<ArchiveCompression>> ArchiveLevels { get; } =
        [new("Fastest", ArchiveCompression.Fastest), new("Balanced", ArchiveCompression.Optimal), new("Smallest", ArchiveCompression.Smallest)];
    public IReadOnlyList<Choice<CollisionStrategy>> CollisionChoices { get; } =
    [
        new("Keep both (add a number)", CollisionStrategy.KeepBoth),
        new("Replace existing output", CollisionStrategy.Overwrite),
        new("Skip the file", CollisionStrategy.Skip),
    ];

    partial void OnSelectedTargetChanged(TargetViewModel? value)
    {
        UpdateOptionVisibility();
        StartCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedFileChanged(StagedFileViewModel? value) => OnPropertyChanged(nameof(OptionsScopeText));

    partial void OnPerFileOptionsChanged(bool value)
    {
        if (value)
        {
            // Seed each file with the current shared options so switching modes loses nothing.
            foreach (var file in Files)
            {
                CopyOptions(SharedOptions, file.Options);
            }
            SelectedFile ??= Files.FirstOrDefault();
        }
    }

    public async Task AddFilesAsync(IReadOnlyList<string> paths, string? targetKey = null)
    {
        var existing = Files.Select(f => f.Input.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fresh = paths.Where(File.Exists).Select(Path.GetFullPath).Where(p => !existing.Contains(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var skippedFolders = paths.Count(Directory.Exists);
        if (fresh.Count > 0)
        {
            IsAnalyzing = true;
            try
            {
                var analyzed = await Task.Run(() => _coordinator.Registry.Analyze(fresh));
                foreach (var input in analyzed)
                {
                    Files.Add(new StagedFileViewModel(input, SharedOptions.Clone()));
                }
            }
            finally
            {
                IsAnalyzing = false;
            }
        }
        StatusMessage = skippedFolders > 0 ? "Folders are not converted directly; add the files inside them." : null;
        await RefreshTargetsAsync(targetKey);
    }

    private async Task RefreshTargetsAsync(string? preferredKey = null)
    {
        var previous = preferredKey ?? SelectedTarget?.Key;
        var inputs = Files.Select(f => f.Input).ToList();
        var targets = inputs.Count == 0 ? [] : await Task.Run(() => _coordinator.Registry.GetTargets(inputs));

        Targets.Clear();
        var categories = inputs.Select(i => i.Format?.Category).Distinct().ToList();
        var inputCategory = categories.Count == 1 ? categories[0] : null;
        foreach (var t in targets)
        {
            Targets.Add(new TargetViewModel(t, inputCategory));
        }
        SelectedTarget = Targets.FirstOrDefault(t => t.Key == previous && t.IsAvailable)
                         ?? Targets.FirstOrDefault(t => t.Key == previous)
                         ?? Targets.FirstOrDefault(t => t.IsAvailable);
        TargetsMessage = inputs.Count == 0 ? null
            : Targets.Count == 0 ? "These files have no conversion in common. Convert them in separate batches."
            : Targets.All(t => !t.IsAvailable) ? "Conversions for these files need a tool that is not installed. See Settings → Conversion tools."
            : null;
    }

    private void UpdateOptionVisibility()
    {
        var t = SelectedTarget?.Target;
        var formats = Files.Select(f => f.Input.Format).ToList();
        bool All(Func<FileFormat?, bool> predicate) => formats.Count > 0 && formats.All(predicate);
        var key = t?.Key ?? "";
        var output = t?.Output;
        var pdfInput = All(f => f == KnownFormats.Pdf);

        ShowImageOptions = t is not null && All(f => f?.Category == FormatCategory.Image) && t.Kind == RouteKind.Convert
                           && (output?.Category == FormatCategory.Image || output == KnownFormats.Pdf);
        ShowVideoOptions = output?.Category == FormatCategory.Video || key == "compress-video";
        ShowGifOptions = key == "gif" && formats.Any(f => f?.Category == FormatCategory.Video);
        ShowAudioOptions = output?.Category == FormatCategory.Audio;
        ShowPdfPages = pdfInput && key is "pdf-extract" or "pdf-rotate" or "png" or "jpeg" or "webp";
        ShowPdfRotate = key == "pdf-rotate";
        ShowPdfDpi = pdfInput && key is "png" or "jpeg" or "webp";
        ShowPdfCompression = key == "pdf-compress";
        ShowDocumentOptions = All(f => f is { Category: FormatCategory.Document }) && output?.Category == FormatCategory.Document;
        ShowArchiveOptions = key is "zip" or "7z";
        HasOptions = ShowImageOptions || ShowVideoOptions || ShowGifOptions || ShowAudioOptions || ShowPdfPages || ShowPdfRotate
                     || ShowPdfDpi || ShowPdfCompression || ShowDocumentOptions || ShowArchiveOptions;
    }

    [RelayCommand]
    private async Task AddFiles()
    {
        var files = _dialogs.PickFiles("Add files");
        if (files.Count > 0)
        {
            await AddFilesAsync(files);
        }
    }

    [RelayCommand]
    private async Task RemoveFile(StagedFileViewModel file)
    {
        Files.Remove(file);
        await RefreshTargetsAsync();
    }

    [RelayCommand]
    private async Task ClearFiles()
    {
        Files.Clear();
        await RefreshTargetsAsync();
    }

    [RelayCommand]
    private void MoveUp(StagedFileViewModel file)
    {
        var i = Files.IndexOf(file);
        if (i > 0)
        {
            Files.Move(i, i - 1);
        }
    }

    [RelayCommand]
    private void MoveDown(StagedFileViewModel file)
    {
        var i = Files.IndexOf(file);
        if (i >= 0 && i < Files.Count - 1)
        {
            Files.Move(i, i + 1);
        }
    }

    [RelayCommand]
    private void BrowseOutputFolder()
    {
        var folder = _dialogs.PickFolder("Choose an output folder", OutputFolder);
        if (folder is not null)
        {
            OutputFolder = folder;
            SaveBesideOriginal = false;
        }
    }

    private bool CanStart() => Files.Count > 0 && SelectedTarget is { IsAvailable: true };

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        var target = SelectedTarget!;
        if (!SaveBesideOriginal && string.IsNullOrWhiteSpace(OutputFolder))
        {
            StatusMessage = "Choose an output folder, or save beside the original files.";
            return;
        }
        var output = new OutputSettings(SaveBesideOriginal ? null : OutputFolder, Collision);
        var registry = _coordinator.Registry;
        try
        {
            var plans = new List<ConversionPlan>();
            var skipped = 0;
            if (PerFileOptions && target.Target.Kind != RouteKind.Combine)
            {
                foreach (var file in Files)
                {
                    plans.AddRange(registry.CreatePlans([file.Input], target.Key, file.Options, output, out var s));
                    skipped += s.Count;
                }
            }
            else
            {
                plans.AddRange(registry.CreatePlans(Files.Select(f => f.Input).ToList(), target.Key, SharedOptions, output, out var s));
                skipped = s.Count;
            }

            _coordinator.Enqueue(plans, skipped, target.Label);
            StatusMessage = $"Queued {plans.Count} conversion(s) to {target.Label}" + (skipped > 0 ? $"; {skipped} file(s) already in that format were skipped." : ".");
            Files.Clear();
            Targets.Clear();
            SelectedTarget = null;
        }
        catch (ConversionException ex)
        {
            StatusMessage = ex.Message;
            _logger.LogWarning(ex, "Could not start conversion");
        }
    }

    [RelayCommand]
    private void ResetOptions()
    {
        SharedOptions = _settings.Current.Defaults.Clone();
        foreach (var file in Files)
        {
            CopyOptions(SharedOptions, file.Options);
        }
        OnPropertyChanged(nameof(EditingOptions));
    }

    // Queue commands
    [RelayCommand] private void CancelJob(ConversionJob job) => _coordinator.Queue.Cancel(job);
    [RelayCommand] private void PauseJob(ConversionJob job) => _coordinator.Queue.Pause(job);
    [RelayCommand] private void ResumeJob(ConversionJob job) => _coordinator.Queue.Resume(job);
    [RelayCommand] private void RetryJob(ConversionJob job) => _coordinator.Queue.Retry(job);
    [RelayCommand] private void RemoveJob(ConversionJob job) => _coordinator.Queue.Remove(job);
    [RelayCommand] private void ClearFinished() => _coordinator.Queue.ClearFinished();
    [RelayCommand] private void CancelAll() => _coordinator.Queue.CancelAll();

    [RelayCommand]
    private void PauseAll()
    {
        foreach (var job in Jobs.Where(j => j.Status == JobStatus.Queued).ToList())
        {
            _coordinator.Queue.Pause(job);
        }
    }

    [RelayCommand]
    private void ResumeAll()
    {
        foreach (var job in Jobs.Where(j => j.Status == JobStatus.Paused).ToList())
        {
            _coordinator.Queue.Resume(job);
        }
    }

    [RelayCommand]
    private void RevealJob(ConversionJob job)
    {
        var path = job.Outputs.FirstOrDefault() ?? job.Plan.Inputs.FirstOrDefault()?.Path;
        if (path is not null)
        {
            ShellLauncher.RevealInExplorer(path);
        }
    }

    private static void CopyOptions(ConversionOptions source, ConversionOptions target)
    {
        var clone = source.Clone();
        target.Image = clone.Image;
        target.Video = clone.Video;
        target.Audio = clone.Audio;
        target.Pdf = clone.Pdf;
        target.Document = clone.Document;
        target.Archive = clone.Archive;
    }
}
