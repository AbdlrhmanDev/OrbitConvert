using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using OrbitConvert.Core.Tools;

namespace OrbitConvert.Infrastructure.Tools;

/// <summary>
/// Finds external tools. Search order:
/// 1. a path the user configured in Settings;
/// 2. tools OrbitConvert downloaded (with consent) into %LOCALAPPDATA%\OrbitConvert\tools;
/// 3. a "tools" folder next to the executable (portable bundles);
/// 4. directories listed in ORBITCONVERT_TOOLS_DIR;
/// 5. the PATH;
/// 6. well-known install locations and registry keys.
/// </summary>
public sealed class ToolLocator : IToolLocator
{
    private static readonly Dictionary<ExternalTool, string[]> ExecutableNames = new()
    {
        [ExternalTool.FFmpeg] = ["ffmpeg.exe"],
        [ExternalTool.FFprobe] = ["ffprobe.exe"],
        [ExternalTool.Pandoc] = ["pandoc.exe"],
        [ExternalTool.LibreOffice] = ["soffice.exe"],
        [ExternalTool.SevenZip] = ["7z.exe", "7za.exe", "7zr.exe"],
        [ExternalTool.Ghostscript] = ["gswin64c.exe", "gswin32c.exe"],
    };

    private readonly AppPaths _paths;
    private readonly Func<IReadOnlyDictionary<ExternalTool, string>> _overrides;
    private readonly ILogger<ToolLocator> _logger;
    private readonly ConcurrentDictionary<ExternalTool, string?> _cache = new();

    public ToolLocator(AppPaths paths, Func<IReadOnlyDictionary<ExternalTool, string>> overrides, ILogger<ToolLocator> logger)
    {
        _paths = paths;
        _overrides = overrides;
        _logger = logger;
    }

    public string? Find(ExternalTool tool) => _cache.GetOrAdd(tool, t =>
    {
        var found = Search(t);
        _logger.LogInformation("Tool {Tool}: {Path}", t, found ?? "not found");
        return found;
    });

    public void Refresh() => _cache.Clear();

    private string? Search(ExternalTool tool)
    {
        if (_overrides().TryGetValue(tool, out var configured) && !string.IsNullOrWhiteSpace(configured))
        {
            if (File.Exists(configured))
            {
                return Path.GetFullPath(configured);
            }
            _logger.LogWarning("Configured path for {Tool} does not exist: {Path}", tool, configured);
        }

        var names = ExecutableNames[tool];

        // FFprobe always ships beside FFmpeg; prefer the matching build.
        if (tool == ExternalTool.FFprobe && Find(ExternalTool.FFmpeg) is { } ffmpeg)
        {
            var sibling = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
            if (File.Exists(sibling))
            {
                return sibling;
            }
        }

        foreach (var root in SearchRoots())
        {
            if (FindBelow(root, names, maxDepth: 4) is { } hit)
            {
                return hit;
            }
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                var candidate = SafeCombine(dir.Trim('"'), name);
                if (candidate is not null && File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return WellKnownLocations(tool).FirstOrDefault(File.Exists);
    }

    private IEnumerable<string> SearchRoots()
    {
        yield return _paths.ToolsDirectory;
        yield return Path.Combine(AppPaths.ApplicationDirectory, "tools");
        var extra = Environment.GetEnvironmentVariable("ORBITCONVERT_TOOLS_DIR");
        if (!string.IsNullOrWhiteSpace(extra))
        {
            foreach (var dir in extra.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                yield return dir;
            }
        }
    }

    private static string? FindBelow(string root, string[] names, int maxDepth)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }
        foreach (var name in names)
        {
            var direct = Path.Combine(root, name);
            if (File.Exists(direct))
            {
                return direct;
            }
        }
        if (maxDepth == 0)
        {
            return null;
        }
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (FindBelow(sub, names, maxDepth - 1) is { } hit)
                {
                    return hit;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    private static string? SafeCombine(string dir, string name)
    {
        try
        {
            return Path.Combine(dir, name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static IEnumerable<string> WellKnownLocations(ExternalTool tool)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        switch (tool)
        {
            case ExternalTool.LibreOffice:
                if (ReadRegistry(@"SOFTWARE\LibreOffice\UNO\InstallPath", null) is { } loPath)
                {
                    yield return Path.Combine(loPath, "soffice.exe");
                }
                yield return Path.Combine(programFiles, "LibreOffice", "program", "soffice.exe");
                yield return Path.Combine(programFilesX86, "LibreOffice", "program", "soffice.exe");
                break;
            case ExternalTool.SevenZip:
                if (ReadRegistry(@"SOFTWARE\7-Zip", "Path") is { } szPath)
                {
                    yield return Path.Combine(szPath, "7z.exe");
                }
                yield return Path.Combine(programFiles, "7-Zip", "7z.exe");
                yield return Path.Combine(programFilesX86, "7-Zip", "7z.exe");
                break;
            case ExternalTool.Pandoc:
                yield return Path.Combine(localAppData, "Pandoc", "pandoc.exe");
                yield return Path.Combine(programFiles, "Pandoc", "pandoc.exe");
                break;
            case ExternalTool.Ghostscript:
                foreach (var root in new[] { programFiles, programFilesX86 })
                {
                    var gs = Path.Combine(root, "gs");
                    if (!Directory.Exists(gs))
                    {
                        continue;
                    }
                    foreach (var version in Directory.EnumerateDirectories(gs).OrderDescending(StringComparer.OrdinalIgnoreCase))
                    {
                        yield return Path.Combine(version, "bin", "gswin64c.exe");
                    }
                }
                break;
            case ExternalTool.FFmpeg:
                yield return Path.Combine(programFiles, "ffmpeg", "bin", "ffmpeg.exe");
                break;
        }
    }

    private static string? ReadRegistry(string subKey, string? valueName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = hive.OpenSubKey(subKey);
                if (key?.GetValue(valueName) is string value && value.Length > 0)
                {
                    return value;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
            }
        }
        return null;
    }
}
