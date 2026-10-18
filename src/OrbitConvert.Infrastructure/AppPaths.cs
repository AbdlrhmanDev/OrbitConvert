namespace OrbitConvert.Infrastructure;

/// <summary>
/// Every location OrbitConvert writes to. All application-owned data lives under one folder
/// so it can be inspected and removed in one step.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string? dataRoot = null)
    {
        DataRoot = dataRoot
            ?? Environment.GetEnvironmentVariable("ORBITCONVERT_DATA")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrbitConvert");
    }

    /// <summary>%LOCALAPPDATA%\OrbitConvert unless overridden (portable mode, tests).</summary>
    public string DataRoot { get; }

    public string SettingsFile => Path.Combine(DataRoot, "settings.json");
    public string HistoryFile => Path.Combine(DataRoot, "history.json");
    public string LogsDirectory => Path.Combine(DataRoot, "logs");
    public string ToolsDirectory => Path.Combine(DataRoot, "tools");
    public string LibreOfficeProfile => Path.Combine(DataRoot, "libreoffice-profile");

    /// <summary>Directory containing the running executable.</summary>
    public static string ApplicationDirectory => AppContext.BaseDirectory;

    /// <summary>
    /// Portable mode: a file named "portable.txt" next to the executable stores data in ".\data".
    /// </summary>
    public static AppPaths CreateDefault()
    {
        var marker = Path.Combine(ApplicationDirectory, "portable.txt");
        return File.Exists(marker) ? new AppPaths(Path.Combine(ApplicationDirectory, "data")) : new AppPaths();
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(LogsDirectory);
    }
}
