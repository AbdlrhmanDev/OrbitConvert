using System.Windows;
using Microsoft.Extensions.Logging;
using OrbitConvert.App.Views;
using OrbitConvert.Core.Settings;
using OrbitConvert.Shell;

namespace OrbitConvert.App.Services;

/// <summary>
/// Owns the ways the radial menu can be summoned outside the main window:
/// A) the floating drop zone, B) the global shortcut (selected Explorer files),
/// D) experimental shake-while-dragging. (C, the Explorer context menu, arrives as a
/// command-line request handled by the application shell.)
/// </summary>
public sealed class ActivationService : IDisposable
{
    private readonly ISettingsStore _settings;
    private readonly RadialMenuService _radial;
    private readonly ExplorerSelection _explorer;
    private readonly NotificationService _notifications;
    private readonly Lazy<IMainWindowHost> _host;
    private readonly ILogger<ActivationService> _logger;

    private GlobalHotkey? _hotkey;
    private DropZoneWindow? _dropZone;
    private DragShakeDetector? _shake;
    private ShakeCatcherWindow? _catcher;

    public ActivationService(ISettingsStore settings, RadialMenuService radial, ExplorerSelection explorer,
        NotificationService notifications, Lazy<IMainWindowHost> host, ILogger<ActivationService> logger)
    {
        _settings = settings;
        _radial = radial;
        _explorer = explorer;
        _notifications = notifications;
        _host = host;
        _logger = logger;
    }

    /// <summary>Last hotkey registration problem, shown in Settings.</summary>
    public string? HotkeyError { get; private set; }

    public bool IsDropZoneVisible => _dropZone is { IsVisible: true };

    public void Start()
    {
        Apply(_settings.Current);
        _settings.Changed += (_, s) => Ui.Post(() => Apply(s));
    }

    public void Apply(AppSettings s)
    {
        ApplyHotkey(s.Hotkey);
        SetDropZoneVisible(s.DropZoneEnabled);
        ApplyShake(s.ShakeToConvertEnabled);
    }

    private void ApplyHotkey(HotkeySettings hotkey)
    {
        if (!hotkey.Enabled)
        {
            _hotkey?.Unregister();
            HotkeyError = null;
            return;
        }
        if (_hotkey is null)
        {
            _hotkey = new GlobalHotkey();
            _hotkey.Pressed += async (_, _) => await OnHotkeyAsync();
        }
        HotkeyError = _hotkey.Register(hotkey) ? null : _hotkey.LastError;
        if (HotkeyError is not null)
        {
            _logger.LogWarning("Hotkey registration failed: {Error}", HotkeyError);
        }
    }

    private async Task OnHotkeyAsync()
    {
        var anchor = ScreenGeometry.CursorPosition();
        var result = await _explorer.GetSelectedFilesAsync(ScreenGeometry.ForegroundWindow(), TimeSpan.FromSeconds(3));
        if (result.Files.Count > 0)
        {
            await _radial.ShowAsync(result.Files, anchor);
        }
        else
        {
            _notifications.Show("OrbitConvert", result.Problem ?? "Select files in File Explorer first.", null);
        }
    }

    public void SetDropZoneVisible(bool visible)
    {
        if (visible)
        {
            if (_dropZone is null)
            {
                _dropZone = new DropZoneWindow();
                _dropZone.FilesDropped += async (_, files) => await _radial.ShowAsync(files);
                _dropZone.OpenRequested += (_, _) => _host.Value.ShowMain();
                _dropZone.HideRequested += (_, _) => UpdateSettings(s => s.DropZoneEnabled = false);
                _dropZone.PositionChanged += (_, p) => UpdateSettings(s => { s.DropZoneLeft = p.X; s.DropZoneTop = p.Y; });
                PositionDropZone(_dropZone);
            }
            _dropZone.Show();
        }
        else
        {
            _dropZone?.Hide();
        }
    }

    private void PositionDropZone(Window window)
    {
        var s = _settings.Current;
        var area = SystemParameters.WorkArea;
        var left = s.DropZoneLeft ?? area.Right - window.Width - 24;
        var top = s.DropZoneTop ?? area.Top + area.Height / 2 - window.Height / 2;
        // Keep it reachable if a monitor was disconnected since the position was saved.
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        window.Left = Math.Clamp(left, virtualLeft, virtualLeft + SystemParameters.VirtualScreenWidth - window.Width);
        window.Top = Math.Clamp(top, virtualTop, virtualTop + SystemParameters.VirtualScreenHeight - window.Height);
    }

    private void ApplyShake(bool enabled)
    {
        if (enabled)
        {
            if (_shake is null)
            {
                _shake = new DragShakeDetector(_logger);
                _shake.Shaken += (_, point) => Ui.Post(() => ShowCatcher(point));
            }
            if (!_shake.Start())
            {
                _logger.LogWarning("Shake detection could not start");
            }
        }
        else
        {
            _shake?.Stop();
        }
    }

    private void ShowCatcher(PixelPoint point)
    {
        if (_catcher is { IsVisible: true })
        {
            return;
        }
        _catcher = new ShakeCatcherWindow(point);
        _catcher.FilesDropped += async (_, files) => await _radial.ShowAsync(files);
        _catcher.Closed += (_, _) => _catcher = null;
        _catcher.Show();
    }

    private void UpdateSettings(Action<AppSettings> change)
    {
        var current = _settings.Current;
        change(current);
        _settings.Save(current);
    }

    public void Dispose()
    {
        _hotkey?.Dispose();
        _shake?.Dispose();
        _dropZone?.Close();
        _catcher?.Close();
    }
}
