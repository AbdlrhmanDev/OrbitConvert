using System.Drawing;
using System.Windows.Forms;
using OrbitConvert.App.Views;
using OrbitConvert.Core.Settings;
using OrbitConvert.Shell;

namespace OrbitConvert.App.Services;

/// <summary>
/// System tray icon and notifications. Notifications are OrbitConvert's own top-most toast
/// (<see cref="ToastWindow"/>) rather than tray balloon tips: Windows often files balloon tips silently
/// in the notification center (Do Not Disturb, focus sessions, banners turned off), so they did not
/// reliably pop up.
/// </summary>
public sealed class NotificationService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ISettingsStore _settings;
    private ToastWindow? _toast;

    public NotificationService(ISettingsStore settings)
    {
        _settings = settings;
        _icon = new NotifyIcon
        {
            Text = "OrbitConvert",
            Icon = LoadIcon(),
            Visible = false,
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? OpenRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? ToggleDropZoneRequested;

    public void ShowTrayIcon(Func<bool> dropZoneVisible)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open OrbitConvert", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        var dropZone = new ToolStripMenuItem("Show drop zone") { CheckOnClick = false };
        dropZone.Click += (_, _) => ToggleDropZoneRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(dropZone);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        menu.Opening += (_, _) => dropZone.Checked = dropZoneVisible();
        _icon.ContextMenuStrip = menu;
        _icon.Visible = true;
    }

    /// <param name="revealPath">A file to select in Explorer when the notification is clicked.</param>
    /// <remarks>Safe to call from any thread. A new notification replaces the one on screen.</remarks>
    public void Show(string title, string message, string? revealPath, bool isError = false) =>
        Ui.Post(() =>
        {
            _toast?.FadeOutAndClose();
            var text = message.Length > 200 ? message[..200] + "…" : message;
            var duration = TimeSpan.FromSeconds(isError ? 10 : 6);
            var toast = new ToastWindow(title, text, revealPath is not null, isError, _settings.Current.ReduceMotion, duration);
            toast.OpenFolderRequested += (_, _) => ShellLauncher.RevealInExplorer(revealPath!);
            toast.Clicked += (_, _) =>
            {
                if (revealPath is not null)
                {
                    ShellLauncher.RevealInExplorer(revealPath);
                }
                else
                {
                    OpenRequested?.Invoke(this, EventArgs.Empty);
                }
            };
            toast.Closed += (_, _) =>
            {
                if (ReferenceEquals(_toast, toast))
                {
                    _toast = null;
                }
            };
            _toast = toast;
            toast.Show();
        });

    private static Icon LoadIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } exe && Icon.ExtractAssociatedIcon(exe) is { } icon)
            {
                return icon;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
        }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
