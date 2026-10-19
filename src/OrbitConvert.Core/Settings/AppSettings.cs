using OrbitConvert.Core.Automation;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Presets;
using OrbitConvert.Core.Tools;

namespace OrbitConvert.Core.Settings;

public enum OutputLocationMode { BesideOriginal, CustomFolder }

public enum AppTheme { System, Light, Dark }

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

public sealed class HotkeySettings
{
    public bool Enabled { get; set; } = true;
    public HotkeyModifiers Modifiers { get; set; } = HotkeyModifiers.Windows | HotkeyModifiers.Shift;

    /// <summary>A virtual-key name such as "C" or "F9".</summary>
    public string Key { get; set; } = "C";

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        parts.Add(Key);
        return string.Join("+", parts);
    }
}

/// <summary>User preferences, persisted as JSON in %LOCALAPPDATA%\OrbitConvert\settings.json.</summary>
public sealed class AppSettings
{
    public OutputLocationMode OutputLocation { get; set; } = OutputLocationMode.BesideOriginal;
    public string? CustomOutputFolder { get; set; }
    public CollisionStrategy Collision { get; set; } = CollisionStrategy.KeepBoth;

    public AppTheme Theme { get; set; } = AppTheme.System;
    public string Language { get; set; } = "en";

    public HotkeySettings Hotkey { get; set; } = new();
    public bool DropZoneEnabled { get; set; }
    public double? DropZoneLeft { get; set; }
    public double? DropZoneTop { get; set; }

    /// <summary>Experimental: show a drop target when the mouse is shaken during a drag.</summary>
    public bool ShakeToConvertEnabled { get; set; }

    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>Accessibility: animations fade instead of scaling.</summary>
    public bool ReduceMotion { get; set; }
    public bool CloseRadialAfterSuccess { get; set; } = true;
    public bool StartMinimizedToTray { get; set; }
    public bool RunInBackgroundWhenClosed { get; set; } = true;

    /// <summary>Privacy: when false, nothing about conversions is written to disk.</summary>
    public bool KeepHistory { get; set; } = true;
    public int HistoryLimit { get; set; } = 500;

    /// <summary>Opt-in only. When enabled, OrbitConvert asks GitHub for the latest release once per day.</summary>
    public bool CheckForUpdates { get; set; }
    public DateTimeOffset? LastUpdateCheck { get; set; }

    public int MaxParallelJobs { get; set; } = Math.Clamp(Environment.ProcessorCount / 4, 1, 4);

    public ConversionOptions Defaults { get; set; } = new();

    /// <summary>Saved conversions. Starred ones appear in the Orbit Menu.</summary>
    public List<ConversionPreset> Presets { get; set; } = BuiltInPresets.Create();

    /// <summary>Folder automation; every watcher starts disabled.</summary>
    public List<WatchFolder> Watchers { get; set; } = [];

    /// <summary>User-specified executable paths that override automatic discovery.</summary>
    public Dictionary<ExternalTool, string> ToolPaths { get; set; } = [];

    public OutputSettings ToOutputSettings() => new(
        OutputLocation == OutputLocationMode.CustomFolder && !string.IsNullOrWhiteSpace(CustomOutputFolder) ? CustomOutputFolder : null,
        Collision);
}

public interface ISettingsStore
{
    AppSettings Current { get; }
    event EventHandler<AppSettings>? Changed;
    void Save(AppSettings settings);
}
