using System.Drawing;
using System.Windows.Forms;
using OrbitConvert.Shell;

namespace OrbitConvert.App.Services;

/// <summary>
/// System tray icon and notifications. Balloon tips from a tray icon are shown by Windows 10/11 as
/// standard toast notifications and need no app identity or packaging.
/// </summary>
public sealed class NotificationService : IDisposable
{
    private readonly NotifyIcon _icon;
    private string? _lastRevealPath;

    public NotificationService()
    {
        _icon = new NotifyIcon
        {
            Text = "OrbitConvert",
            Icon = LoadIcon(),
            Visible = false,
        };
        _icon.BalloonTipClicked += (_, _) =>
        {
            if (_lastRevealPath is not null)
            {
                ShellLauncher.RevealInExplorer(_lastRevealPath);
            }
            else
            {
                OpenRequested?.Invoke(this, EventArgs.Empty);
            }
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
    public void Show(string title, string message, string? revealPath, bool isError = false)
    {
        _lastRevealPath = revealPath;
        _icon.Visible = true; // a notification needs an icon; it stays for the rest of the session
        _icon.ShowBalloonTip(4000, title, message.Length > 200 ? message[..200] + "…" : message, isError ? ToolTipIcon.Error : ToolTipIcon.Info);
    }

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
