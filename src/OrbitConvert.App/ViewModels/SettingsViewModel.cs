using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OrbitConvert.App.Services;
using OrbitConvert.Converters.Images;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.History;
using OrbitConvert.Core.Settings;
using OrbitConvert.Core.Tools;
using OrbitConvert.Infrastructure;
using OrbitConvert.Infrastructure.Tools;
using OrbitConvert.Infrastructure.Updates;
using OrbitConvert.Shell;

namespace OrbitConvert.App.ViewModels;

public sealed partial class ToolStatusViewModel : ObservableObject
{
    private readonly SettingsViewModel _owner;

    public ToolStatusViewModel(SettingsViewModel owner, ToolDescriptor descriptor)
    {
        _owner = owner;
        Descriptor = descriptor;
        Package = ToolPackages.For(descriptor.Tool);
    }

    public ToolDescriptor Descriptor { get; }
    public ToolPackage? Package { get; }
    public string Name => Descriptor.DisplayName;
    public string Purpose => Descriptor.Purpose;
    public string License => Descriptor.License;
    public bool CanDownload => Package is not null && Descriptor.Tool != ExternalTool.FFprobe;

    [ObservableProperty] private string? _path;
    [ObservableProperty] private bool _isFound;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private string? _progressText;
    [ObservableProperty] private bool _installedByOrbitConvert;
    [ObservableProperty] private bool _hasOverride;

    public string StatusText => IsFound ? "Installed" : "Not found";

    partial void OnIsFoundChanged(bool value) => OnPropertyChanged(nameof(StatusText));

    [RelayCommand] private Task Install() => _owner.InstallToolAsync(this);
    [RelayCommand] private void Browse() => _owner.BrowseTool(this);
    [RelayCommand] private void ClearOverride() => _owner.ClearToolOverride(this);
    [RelayCommand] private void Remove() => _owner.RemoveTool(this);
    [RelayCommand] private void OpenHomepage() => ShellLauncher.OpenUrl(Descriptor.Homepage);
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;
    private readonly IToolLocator _locator;
    private readonly DependencyInstaller _installer;
    private readonly UpdateChecker _updates;
    private readonly ThemeManager _theme;
    private readonly ActivationService _activation;
    private readonly IHistoryStore _history;
    private readonly DialogService _dialogs;
    private readonly AppPaths _paths;
    private readonly ILogger<SettingsViewModel> _logger;

    public SettingsViewModel(ISettingsStore store, IToolLocator locator, DependencyInstaller installer, UpdateChecker updates,
        ThemeManager theme, ActivationService activation, IHistoryStore history, DialogService dialogs, AppPaths paths,
        ILogger<SettingsViewModel> logger)
    {
        _store = store;
        _locator = locator;
        _installer = installer;
        _updates = updates;
        _theme = theme;
        _activation = activation;
        _history = history;
        _dialogs = dialogs;
        _paths = paths;
        _logger = logger;
        foreach (var tool in ToolCatalog.All)
        {
            Tools.Add(new ToolStatusViewModel(this, tool));
        }
    }

    private AppSettings S => _store.Current;

    private void Update(Action<AppSettings> change, params string[] properties)
    {
        var settings = S;
        change(settings);
        try
        {
            _store.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Error("Settings", "Your settings could not be saved: " + ex.Message);
        }
        foreach (var p in properties)
        {
            OnPropertyChanged(p);
        }
    }

    public ObservableCollection<ToolStatusViewModel> Tools { get; } = [];

    // Output
    public bool SaveBesideOriginal
    {
        get => S.OutputLocation == OutputLocationMode.BesideOriginal;
        set => Update(s => s.OutputLocation = value ? OutputLocationMode.BesideOriginal : OutputLocationMode.CustomFolder, nameof(SaveBesideOriginal));
    }

    public string? CustomOutputFolder
    {
        get => S.CustomOutputFolder;
        set => Update(s => s.CustomOutputFolder = value, nameof(CustomOutputFolder));
    }

    public CollisionStrategy Collision
    {
        get => S.Collision;
        set => Update(s => s.Collision = value, nameof(Collision));
    }

    public IReadOnlyList<Choice<CollisionStrategy>> CollisionChoices { get; } =
    [
        new("Keep both (add a number)", CollisionStrategy.KeepBoth),
        new("Replace existing output", CollisionStrategy.Overwrite),
        new("Skip the file", CollisionStrategy.Skip),
    ];

    [RelayCommand]
    private void BrowseOutputFolder()
    {
        var folder = _dialogs.PickFolder("Default output folder", CustomOutputFolder);
        if (folder is not null)
        {
            CustomOutputFolder = folder;
            SaveBesideOriginal = false;
        }
    }

    // Appearance
    public IReadOnlyList<Choice<AppTheme>> Themes { get; } = [new("Use Windows setting", AppTheme.System), new("Light", AppTheme.Light), new("Dark", AppTheme.Dark)];

    public AppTheme Theme
    {
        get => S.Theme;
        set
        {
            Update(s => s.Theme = value, nameof(Theme));
            _theme.Apply(value);
        }
    }

    public IReadOnlyList<Choice<string>> Languages { get; } = [new("English", "en")];

    public string Language
    {
        get => S.Language;
        set => Update(s => s.Language = value, nameof(Language));
    }

    // Activation
    public bool HotkeyEnabled
    {
        get => S.Hotkey.Enabled;
        set => Update(s => s.Hotkey.Enabled = value, nameof(HotkeyEnabled), nameof(HotkeyStatus));
    }

    public bool HotkeyCtrl { get => Has(HotkeyModifiers.Control); set => SetModifier(HotkeyModifiers.Control, value); }
    public bool HotkeyAlt { get => Has(HotkeyModifiers.Alt); set => SetModifier(HotkeyModifiers.Alt, value); }
    public bool HotkeyShift { get => Has(HotkeyModifiers.Shift); set => SetModifier(HotkeyModifiers.Shift, value); }
    public bool HotkeyWin { get => Has(HotkeyModifiers.Windows); set => SetModifier(HotkeyModifiers.Windows, value); }

    public string HotkeyKey
    {
        get => S.Hotkey.Key;
        set
        {
            var key = (value ?? "").Trim().ToUpperInvariant();
            if (!GlobalHotkey.TryParseKey(key, out _))
            {
                HotkeyMessage = $"\"{value}\" is not a key name. Use a letter, digit or F1–F12.";
                OnPropertyChanged(nameof(HotkeyStatus));
                return;
            }
            HotkeyMessage = null;
            Update(s => s.Hotkey.Key = key, nameof(HotkeyKey), nameof(HotkeyStatus));
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HotkeyStatus))]
    private string? _hotkeyMessage;

    public string HotkeyStatus =>
        HotkeyMessage ?? (!S.Hotkey.Enabled ? "Shortcut is off."
            : _activation.HotkeyError ?? $"Press {S.Hotkey} in File Explorer to convert the selected files.");

    private bool Has(HotkeyModifiers m) => S.Hotkey.Modifiers.HasFlag(m);

    private void SetModifier(HotkeyModifiers m, bool on) =>
        Update(s => s.Hotkey.Modifiers = on ? s.Hotkey.Modifiers | m : s.Hotkey.Modifiers & ~m,
            nameof(HotkeyCtrl), nameof(HotkeyAlt), nameof(HotkeyShift), nameof(HotkeyWin), nameof(HotkeyStatus));

    public bool DropZoneEnabled
    {
        get => S.DropZoneEnabled;
        set => Update(s => s.DropZoneEnabled = value, nameof(DropZoneEnabled));
    }

    public bool ShakeToConvertEnabled
    {
        get => S.ShakeToConvertEnabled;
        set => Update(s => s.ShakeToConvertEnabled = value, nameof(ShakeToConvertEnabled));
    }

    [ObservableProperty]
    private bool _contextMenuEnabled;

    [ObservableProperty]
    private bool _sendToEnabled;

    [ObservableProperty]
    private string? _shellMessage;

    partial void OnContextMenuEnabledChanged(bool value)
    {
        if (_refreshing)
        {
            return;
        }
        try
        {
            if (value)
            {
                ShellRegistration.RegisterContextMenu(ExecutablePath);
                ShellMessage = "Added \"Convert with OrbitConvert\" to the File Explorer context menu. On Windows 11 it is under \"Show more options\".";
            }
            else
            {
                ShellRegistration.UnregisterContextMenu();
                ShellMessage = "Removed the context menu entry.";
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            ShellMessage = "Could not change the context menu: " + ex.Message;
        }
    }

    partial void OnSendToEnabledChanged(bool value)
    {
        if (_refreshing)
        {
            return;
        }
        try
        {
            if (value)
            {
                ShellRegistration.RegisterSendTo(ExecutablePath);
            }
            else
            {
                ShellRegistration.UnregisterSendTo();
            }
        }
        catch (Exception ex)
        {
            ShellMessage = "Could not change the Send To entry: " + ex.Message;
            _logger.LogWarning(ex, "Send To registration failed");
        }
    }

    private static string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "OrbitConvert.exe");

    // Notifications & behavior
    public bool NotificationsEnabled { get => S.NotificationsEnabled; set => Update(s => s.NotificationsEnabled = value, nameof(NotificationsEnabled)); }
    public bool CloseRadialAfterSuccess { get => S.CloseRadialAfterSuccess; set => Update(s => s.CloseRadialAfterSuccess = value, nameof(CloseRadialAfterSuccess)); }
    public bool RunInBackgroundWhenClosed { get => S.RunInBackgroundWhenClosed; set => Update(s => s.RunInBackgroundWhenClosed = value, nameof(RunInBackgroundWhenClosed)); }

    public int MaxParallelJobs
    {
        get => S.MaxParallelJobs;
        set => Update(s => s.MaxParallelJobs = Math.Clamp(value, 1, 8), nameof(MaxParallelJobs));
    }

    // Defaults
    public int DefaultImageQuality { get => S.Defaults.Image.Quality; set => Update(s => s.Defaults.Image.Quality = Math.Clamp(value, 1, 100), nameof(DefaultImageQuality)); }
    public bool DefaultStripMetadata { get => S.Defaults.Image.StripMetadata; set => Update(s => s.Defaults.Image.StripMetadata = value, nameof(DefaultStripMetadata)); }
    public int DefaultAudioBitrate { get => S.Defaults.Audio.BitrateKbps; set => Update(s => s.Defaults.Audio.BitrateKbps = value, nameof(DefaultAudioBitrate)); }
    public VideoQuality DefaultVideoQuality { get => S.Defaults.Video.Quality; set => Update(s => s.Defaults.Video.Quality = value, nameof(DefaultVideoQuality)); }
    public int DefaultPdfDpi { get => S.Defaults.Pdf.ImageDpi; set => Update(s => s.Defaults.Pdf.ImageDpi = value, nameof(DefaultPdfDpi)); }

    public IReadOnlyList<Choice<int>> Bitrates { get; } =
        [new("128 kbps", 128), new("160 kbps", 160), new("192 kbps", 192), new("256 kbps", 256), new("320 kbps", 320)];
    public IReadOnlyList<Choice<VideoQuality>> VideoQualities { get; } =
        [new("High quality", VideoQuality.High), new("Balanced", VideoQuality.Balanced), new("Smaller file", VideoQuality.Small)];
    public IReadOnlyList<Choice<int>> Dpis { get; } = [new("72 dpi", 72), new("96 dpi", 96), new("150 dpi", 150), new("200 dpi", 200), new("300 dpi", 300)];

    // Privacy
    public bool KeepHistory
    {
        get => S.KeepHistory;
        set
        {
            if (!value && _history.Entries.Count > 0 && _dialogs.Confirm("History", "Also delete the existing history?"))
            {
                _history.Clear();
            }
            Update(s => s.KeepHistory = value, nameof(KeepHistory));
        }
    }

    public string DataFolder => _paths.DataRoot;

    [RelayCommand] private void OpenDataFolder() => ShellLauncher.OpenFolder(_paths.DataRoot);
    [RelayCommand] private void OpenLogsFolder() => ShellLauncher.OpenFolder(_paths.LogsDirectory);

    // Updates (opt-in)
    public bool CheckForUpdates { get => S.CheckForUpdates; set => Update(s => s.CheckForUpdates = value, nameof(CheckForUpdates)); }

    [ObservableProperty]
    private string? _updateStatus;

    [ObservableProperty]
    private string? _updateUrl;

    [RelayCommand]
    private async Task CheckNow()
    {
        UpdateStatus = "Checking GitHub for a newer release…";
        UpdateUrl = null;
        try
        {
            var info = await _updates.CheckAsync(CancellationToken.None);
            Update(s => s.LastUpdateCheck = DateTimeOffset.Now);
            if (info is null)
            {
                UpdateStatus = "No published releases were found.";
            }
            else if (info.IsNewer)
            {
                UpdateStatus = $"Version {info.Latest} is available (you have {info.Current}).";
                UpdateUrl = info.ReleaseUrl;
            }
            else
            {
                UpdateStatus = $"You have the latest version ({info.Current}).";
            }
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            UpdateStatus = "Could not check for updates: " + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenUpdate()
    {
        if (UpdateUrl is not null)
        {
            ShellLauncher.OpenUrl(UpdateUrl);
        }
    }

    // About
    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "?";
    public string EngineVersions => $"{ImageMagickConverter.EngineVersion} · PDFsharp · PDFium (PDFtoImage)";

    [RelayCommand]
    private void OpenNotices()
    {
        var notice = Path.Combine(AppContext.BaseDirectory, "NOTICE.md");
        if (File.Exists(notice))
        {
            Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { notice }, UseShellExecute = false });
        }
    }

    [RelayCommand] private void OpenProjectPage() => ShellLauncher.OpenUrl($"https://github.com/{UpdateChecker.Repository}");

    // Tools
    private bool _refreshing;

    public void Refresh()
    {
        _refreshing = true;
        try
        {
            _locator.Refresh();
            foreach (var tool in Tools)
            {
                tool.Path = _locator.Find(tool.Descriptor.Tool);
                tool.IsFound = tool.Path is not null;
                tool.InstalledByOrbitConvert = tool.Package is not null && _installer.IsInstalledByOrbitConvert(tool.Package)
                                               && tool.Path?.StartsWith(_installer.InstallDirectory(tool.Package), StringComparison.OrdinalIgnoreCase) == true;
                tool.HasOverride = S.ToolPaths.ContainsKey(tool.Descriptor.Tool);
            }
            ContextMenuEnabled = ShellRegistration.IsContextMenuRegistered(ExecutablePath);
            SendToEnabled = ShellRegistration.IsSendToRegistered();
            OnPropertyChanged(nameof(HotkeyStatus));
        }
        finally
        {
            _refreshing = false;
        }
    }

    internal async Task InstallToolAsync(ToolStatusViewModel tool)
    {
        var package = tool.Package;
        if (package is null)
        {
            return;
        }
        var consent = _dialogs.Confirm($"Download {package.Name}?",
            $"OrbitConvert will download {package.Name} {package.Version} from:\n{package.DownloadUrl}\n\n" +
            $"License: {package.License}\n\n" +
            $"The download is verified against a pinned SHA-256 checksum and stored only for OrbitConvert in:\n{_installer.InstallDirectory(package)}\n\n" +
            "No information about you or your files is sent. Continue?");
        if (!consent)
        {
            return;
        }

        tool.IsInstalling = true;
        try
        {
            var progress = new Progress<DownloadProgress>(p => tool.ProgressText = p.TotalBytes is > 0
                ? $"{p.Stage} {100 * p.BytesReceived / p.TotalBytes.Value}%"
                : p.Stage + "…");
            await _installer.InstallAsync(package, progress, CancellationToken.None);
            tool.ProgressText = "Installed and verified.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Installing {Package} failed", package.Name);
            tool.ProgressText = null;
            _dialogs.Error("Download failed", ex is ConversionException ? ex.Message : $"{package.Name} could not be installed: {ex.Message}");
        }
        finally
        {
            tool.IsInstalling = false;
            Refresh();
        }
    }

    internal void BrowseTool(ToolStatusViewModel tool)
    {
        var exe = tool.Descriptor.Tool switch
        {
            ExternalTool.FFmpeg => "ffmpeg.exe",
            ExternalTool.FFprobe => "ffprobe.exe",
            ExternalTool.Pandoc => "pandoc.exe",
            ExternalTool.LibreOffice => "soffice.exe",
            ExternalTool.SevenZip => "7z.exe",
            _ => "gswin64c.exe",
        };
        var path = _dialogs.PickExecutable($"Locate {tool.Name}", exe);
        if (path is not null)
        {
            Update(s => s.ToolPaths[tool.Descriptor.Tool] = path);
            Refresh();
        }
    }

    internal void ClearToolOverride(ToolStatusViewModel tool)
    {
        Update(s => s.ToolPaths.Remove(tool.Descriptor.Tool));
        Refresh();
    }

    internal void RemoveTool(ToolStatusViewModel tool)
    {
        if (tool.Package is not null && _dialogs.Confirm("Remove tool", $"Delete the copy of {tool.Name} that OrbitConvert downloaded?"))
        {
            try
            {
                _installer.Uninstall(tool.Package);
            }
            catch (IOException ex)
            {
                _dialogs.Error("Remove tool", ex.Message);
            }
            Refresh();
        }
    }
}
