using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OrbitConvert.App.Converters;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OrbitConvert.App.Services;
using OrbitConvert.Converters.Images;
using OrbitConvert.Converters.Pdf;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;
using OrbitConvert.Core.Presets;
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

    /// <summary>How the type was identified: from the file content, or only from its extension.</summary>
    public string SignatureText => Input.Detection?.Method switch
    {
        DetectionMethod.Signature => "content ✓",
        DetectionMethod.TextHeuristic => "text · by extension",
        DetectionMethod.Extension => "extension only",
        _ => "unknown",
    };

    public bool SignatureVerified => Input.Detection?.Method == DetectionMethod.Signature;

    [ObservableProperty] private string _dimensions = "—";
    [ObservableProperty] private ImageSource? _preview;

    private bool _detailsLoaded;

    /// <summary>Loads pixel size and a small thumbnail in the background (images only).</summary>
    public async Task LoadDetailsAsync()
    {
        if (_detailsLoaded || Input.Format?.Category != FormatCategory.Image)
        {
            return;
        }
        _detailsLoaded = true;
        var size = await Task.Run(() => ImageMagickConverter.ReadDimensions(Input));
        Dimensions = size is { } s ? $"{s.Width} × {s.Height}" : "—";
        Preview = await Task.Run(() => LoadThumbnail(Input.Path));
    }

    private static ImageSource? LoadThumbnail(string path)
    {
        // WPF's own decoders cover JPG, PNG, BMP, GIF and TIFF; other formats keep the placeholder.
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.DecodePixelWidth = 176;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or ArgumentException or InvalidOperationException
                                       or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

public sealed class TargetViewModel(ConversionTarget target, FormatCategory? inputCategory, FileFormat? singleInput = null)
{
    public ConversionTarget Target { get; } = target;
    public string Key => Target.Key;
    public string Label => RadialMenuViewModel.ShortLabel(Target);
    public string Hint => Target.IsAvailable ? RadialMenuViewModel.HintFor(Target, singleInput) : "Unavailable";
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

    private readonly Lazy<PresetsViewModel> _presets;
    private CancellationTokenSource? _estimate;

    public ConvertViewModel(ConversionCoordinator coordinator, ISettingsStore settings, DialogService dialogs, Lazy<PresetsViewModel> presets,
        ILogger<ConvertViewModel> logger)
    {
        _coordinator = coordinator;
        _settings = settings;
        _dialogs = dialogs;
        _presets = presets;
        _logger = logger;
        _sharedOptions = settings.Current.Defaults.Clone();
        _saveBesideOriginal = settings.Current.OutputLocation == OutputLocationMode.BesideOriginal;
        _outputFolder = settings.Current.CustomOutputFolder;
        _collision = settings.Current.Collision;

        Files.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasFiles));
            OnPropertyChanged(nameof(HasSeveralFiles));
            OnPropertyChanged(nameof(PreviewFile));
            OnPropertyChanged(nameof(OutputName));
            OnPropertyChanged(nameof(LocationText));
            StartCommand.NotifyCanExecuteChanged();
            SaveAsPresetCommand.NotifyCanExecuteChanged();
            _ = PreviewFile?.LoadDetailsAsync();
        };
    }

    public Action<Page>? Navigate { get; set; }

    public ObservableCollection<StagedFileViewModel> Files { get; } = [];
    public ObservableCollection<TargetViewModel> Targets { get; } = [];

    public bool HasFiles => Files.Count > 0;
    public bool HasSeveralFiles => Files.Count > 1;

    /// <summary>The file shown in the preview card: the selected one, else the first.</summary>
    public StagedFileViewModel? PreviewFile => SelectedFile ?? Files.FirstOrDefault();

    /// <summary>The name the first output will get (before collision handling).</summary>
    public string OutputName
    {
        get
        {
            var file = PreviewFile;
            var target = SelectedTarget?.Target;
            if (file is null || target is null)
            {
                return "—";
            }
            if (target.Kind == RouteKind.Combine && Files.Count > 1)
            {
                return $"{Files.Count} files → one {target.Output?.Label ?? target.Label}";
            }
            var stem = Path.GetFileNameWithoutExtension(file.Name);
            return target.Output is { } output ? stem + output.PrimaryExtension : $"{stem} · {target.Label}";
        }
    }

    public string LocationText => SaveBesideOriginal
        ? "Beside original" + (PreviewFile is { } f ? " · " + f.Folder : "")
        : string.IsNullOrWhiteSpace(OutputFolder) ? "Choose a folder" : OutputFolder!;

    public string CollisionText => Collision switch
    {
        CollisionStrategy.Overwrite => "If the name exists: replace it",
        CollisionStrategy.Skip => "If the name exists: skip the file",
        _ => "If the name exists: add “(1)”",
    };

    public string StartLabel => SelectedTarget is { } t ? "Convert to " + t.Label : "Convert";

    // Measured output size (images): the first file is encoded in memory with the current options.
    [ObservableProperty] private string? _estimateText;

    // Progress of the last batch started from this page.
    [ObservableProperty] private ConversionBatch? _lastBatch;
    [ObservableProperty] private string _batchStatus = "Ready";
    [ObservableProperty] private bool _batchDone;
    [ObservableProperty] private bool _batchFailed;

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

    public string? PdfPageRange
    {
        get => EditingOptions.Pdf.PageRange;
        set
        {
            if (string.Equals(EditingOptions.Pdf.PageRange, value, StringComparison.Ordinal))
            {
                return;
            }
            EditingOptions.Pdf.PageRange = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPageRangeMissing));
            StartCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsPageRangeRequired => SelectedTarget?.Key == PdfToolsConverter.ExtractKey;
    public bool IsPageRangeMissing => IsPageRangeRequired && string.IsNullOrWhiteSpace(PdfPageRange);
    public string PdfPagesHint => IsPageRangeRequired
        ? "Pages to extract (required), for example 1-3, 5 or 8-."
        : "Pages, for example 1-3, 5 or 8-. Empty means all pages.";

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

    partial void OnSaveBesideOriginalChanged(bool value) => OnPropertyChanged(nameof(LocationText));
    partial void OnOutputFolderChanged(string? value) => OnPropertyChanged(nameof(LocationText));
    partial void OnCollisionChanged(CollisionStrategy value) => OnPropertyChanged(nameof(CollisionText));

    partial void OnSelectedTargetChanged(TargetViewModel? value)
    {
        OnPropertyChanged(nameof(OutputName));
        OnPropertyChanged(nameof(StartLabel));
        SaveAsPresetCommand.NotifyCanExecuteChanged();
        BatchStatus = "Ready";
        BatchDone = BatchFailed = false;
        LastBatch = null;
        ScheduleEstimate();
        UpdateOptionVisibility();
        OnPropertyChanged(nameof(IsPageRangeRequired));
        OnPropertyChanged(nameof(IsPageRangeMissing));
        OnPropertyChanged(nameof(PdfPagesHint));
        OnPropertyChanged(nameof(PdfPageRange));
        StartCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedFileChanged(StagedFileViewModel? value)
    {
        OnPropertyChanged(nameof(PreviewFile));
        OnPropertyChanged(nameof(OutputName));
        OnPropertyChanged(nameof(LocationText));
        _ = PreviewFile?.LoadDetailsAsync();
        ScheduleEstimate();
        OnPropertyChanged(nameof(OptionsScopeText));
        OnPropertyChanged(nameof(PdfPageRange));
        OnPropertyChanged(nameof(IsPageRangeMissing));
        StartCommand.NotifyCanExecuteChanged();
    }

    partial void OnSharedOptionsChanged(ConversionOptions value)
    {
        OnPropertyChanged(nameof(PdfPageRange));
        OnPropertyChanged(nameof(IsPageRangeMissing));
        StartCommand.NotifyCanExecuteChanged();
    }

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
        OnPropertyChanged(nameof(PdfPageRange));
        OnPropertyChanged(nameof(IsPageRangeMissing));
        StartCommand.NotifyCanExecuteChanged();
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
        var formats = inputs.Select(i => i.Format).Distinct().ToList();
        var singleInput = formats.Count == 1 ? formats[0] : null;
        foreach (var t in targets)
        {
            Targets.Add(new TargetViewModel(t, inputCategory, singleInput));
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

    private bool CanStart() => Files.Count > 0 && SelectedTarget is { IsAvailable: true } && !IsPageRangeMissing;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        var target = SelectedTarget!;
        if (IsPageRangeMissing)
        {
            StatusMessage = "Enter the pages to extract, for example 1-3, 5.";
            return;
        }
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

            var batch = _coordinator.Enqueue(plans, skipped, target.Label);
            StatusMessage = skipped > 0 ? $"{skipped} file(s) already in that format were skipped." : null;
            Track(batch);
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

    /// <summary>Follows a started batch so the output panel shows its real progress and result.</summary>
    private void Track(ConversionBatch batch)
    {
        LastBatch = batch;
        BatchDone = BatchFailed = false;
        BatchStatus = batch.Jobs.Count == 0 ? "Nothing to convert." : $"Converting {batch.Jobs.Count} file(s)…";
        batch.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConversionBatch.Progress) && !batch.IsCompleted)
            {
                Ui.Post(() =>
                {
                    if (ReferenceEquals(LastBatch, batch) && !batch.IsCompleted)
                    {
                        BatchStatus = $"Converting… {Math.Max(0, batch.Progress) * 100:0}%";
                    }
                });
            }
        };
        batch.Completed += (_, _) => Ui.Post(() => ShowResult(batch));
        if (batch.IsCompleted)
        {
            ShowResult(batch);
        }
    }

    private void ShowResult(ConversionBatch batch)
    {
        if (!ReferenceEquals(LastBatch, batch))
        {
            return;
        }
        BatchFailed = batch.FailedCount > 0;
        BatchDone = !BatchFailed;
        var size = batch.Outputs.Where(File.Exists).Sum(p => new FileInfo(p).Length);
        BatchStatus = BatchFailed
            ? $"{batch.FailedCount} failed: {batch.FirstError}"
            : batch.SucceededCount == 0 && batch.SkippedCount > 0
                ? "Skipped: the output name exists and the collision setting is Skip."
                : $"Saved {batch.SucceededCount} file(s) · output validated · {FileSizeConverter.Format(size)}";
    }

    [RelayCommand]
    private void CancelBatch()
    {
        if (LastBatch is { IsCompleted: false } batch)
        {
            foreach (var job in batch.Jobs.Where(j => j.IsActive))
            {
                _coordinator.Queue.Cancel(job);
            }
        }
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        var output = LastBatch?.Outputs.FirstOrDefault(p => File.Exists(p) || Directory.Exists(p));
        if (output is not null)
        {
            ShellLauncher.RevealInExplorer(output);
        }
        else if (!SaveBesideOriginal && Directory.Exists(OutputFolder))
        {
            ShellLauncher.OpenFolder(OutputFolder!);
        }
        else if (PreviewFile is { } file)
        {
            ShellLauncher.RevealInExplorer(file.Input.Path);
        }
    }

    [RelayCommand] private void ShowBatch() => Navigate?.Invoke(Page.Batch);

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(EditingOptions))
        {
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(ImageQuality)));
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(StripMetadata)));
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(ResizeText)));
            ScheduleEstimate();
        }
    }

    public string ResizeText => EditingOptions.Image is { MaxWidth: null, MaxHeight: null }
        ? "Original"
        : $"max {EditingOptions.Image.MaxWidth?.ToString() ?? "–"} × {EditingOptions.Image.MaxHeight?.ToString() ?? "–"}";

    /// <summary>Image quality, wrapped so that changing it refreshes the measured size estimate.</summary>
    public int ImageQuality
    {
        get => EditingOptions.Image.Quality;
        set
        {
            if (EditingOptions.Image.Quality == value)
            {
                return;
            }
            EditingOptions.Image.Quality = value;
            OnPropertyChanged();
            ScheduleEstimate();
        }
    }

    public bool StripMetadata
    {
        get => EditingOptions.Image.StripMetadata;
        set
        {
            EditingOptions.Image.StripMetadata = value;
            OnPropertyChanged();
            ScheduleEstimate();
        }
    }

    /// <summary>
    /// Encodes the first image in memory with the current options (debounced) and reports the
    /// measured byte count. Nothing is written to disk.
    /// </summary>
    private void ScheduleEstimate()
    {
        _estimate?.Cancel();
        var file = PreviewFile;
        var output = SelectedTarget?.Target is { IsAvailable: true, Kind: RouteKind.Convert } t ? t.Output : null;
        if (file is null || output is null || file.Input.Format?.Category != FormatCategory.Image
            || output.Category != FormatCategory.Image)
        {
            EstimateText = null;
            return;
        }
        var cts = _estimate = new CancellationTokenSource();
        var options = EditingOptions.Clone().Image;
        EstimateText = "Measuring output size…";
        _ = Task.Run(async () =>
        {
            await Task.Delay(350, cts.Token); // let the slider settle
            return ImageMagickConverter.EstimateOutputSize(file.Input, output, options, cts.Token);
        }, cts.Token).ContinueWith(task => Ui.Post(() =>
        {
            if (!ReferenceEquals(_estimate, cts) || cts.IsCancellationRequested)
            {
                return;
            }
            EstimateText = task.Status == TaskStatus.RanToCompletion && task.Result is { } bytes
                ? $"Output ≈ {FileSizeConverter.Format(bytes)} for {file.Name} (measured; now {FileSizeConverter.Format(file.Input.Length)})"
                : "The output size could not be measured for this file.";
        }), TaskScheduler.Default);
    }

    [ObservableProperty] private string _presetName = "";

    private bool CanSaveAsPreset() => SelectedTarget is not null && Files.Count > 0;

    [RelayCommand(CanExecute = nameof(CanSaveAsPreset))]
    private void SaveAsPreset()
    {
        var target = SelectedTarget!;
        var categories = Files.Select(f => f.Input.Format?.Category).OfType<FormatCategory>().Distinct().ToList();
        var options = PerFileOptions && SelectedFile is not null ? SelectedFile.Options : SharedOptions;
        var preset = new ConversionPreset
        {
            Name = PresetName.Trim() is { Length: > 0 } name ? name : $"{string.Join(" / ", categories)} to {target.Label}",
            TargetKey = target.Key,
            Tag = (categories.Count == 1 ? CategoryTag(categories[0]) + " → " : "") + target.Label,
            MenuLabel = target.Label.Length <= 8 ? target.Label : target.Label[..8],
            Category = target.Category,
            Summary = DescribeOptions(target, options),
            AppliesTo = categories,
            Options = options.Clone(),
        };
        _presets.Value.Add(preset);
        PresetName = "";
        StatusMessage = $"Saved preset \"{preset.Name}\". Star it on the Presets page to show it in the Orbit Menu.";
    }

    private static string CategoryTag(FormatCategory category) => category switch
    {
        FormatCategory.Image => "IMG",
        FormatCategory.Video => "VID",
        FormatCategory.Audio => "AUD",
        FormatCategory.Document => "DOC",
        FormatCategory.Pdf => "PDF",
        FormatCategory.Archive => "ARC",
        _ => "ANY",
    };

    /// <summary>A short summary of only the options that apply to this target.</summary>
    private string DescribeOptions(TargetViewModel target, ConversionOptions o)
    {
        var parts = new List<string>();
        if (ShowImageOptions)
        {
            parts.Add($"quality {o.Image.Quality}");
            if (o.Image.MaxWidth is not null || o.Image.MaxHeight is not null)
            {
                parts.Add($"max {o.Image.MaxWidth?.ToString() ?? "–"} × {o.Image.MaxHeight?.ToString() ?? "–"} px");
            }
            if (o.Image.StripMetadata)
            {
                parts.Add("strip EXIF");
            }
        }
        if (ShowVideoOptions)
        {
            parts.Add($"{o.Video.Codec} · {o.Video.Resolution} · {o.Video.Quality}");
        }
        if (ShowAudioOptions)
        {
            parts.Add($"{o.Audio.BitrateKbps} kbps");
        }
        if (ShowPdfDpi)
        {
            parts.Add($"{o.Pdf.ImageDpi} dpi");
        }
        return parts.Count == 0 ? target.Target.Description ?? target.Label : string.Join(" · ", parts);
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
