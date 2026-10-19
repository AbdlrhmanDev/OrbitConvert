using Microsoft.Win32;

namespace OrbitConvert.Shell;

/// <summary>
/// "Start with Windows" via the per-user Run key. OrbitConvert then starts in the notification
/// area (--background) so the shortcut, drop target and watch folders are ready. No admin rights.
/// </summary>
public static class StartupRegistration
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "OrbitConvert";

    public static string CommandFor(string executable) => $"\"{executable}\" --background";

    public static bool IsEnabled(string executable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value && value.Contains(executable, StringComparison.OrdinalIgnoreCase);
    }

    public static void Enable(string executable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, CommandFor(executable));
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
