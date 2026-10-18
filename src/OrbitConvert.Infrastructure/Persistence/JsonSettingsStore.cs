using System.Text.Json;
using Microsoft.Extensions.Logging;
using OrbitConvert.Core.Settings;

namespace OrbitConvert.Infrastructure.Persistence;

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly AppPaths _paths;
    private readonly ILogger<JsonSettingsStore> _logger;
    private readonly object _gate = new();
    private AppSettings _current;

    public JsonSettingsStore(AppPaths paths, ILogger<JsonSettingsStore> logger)
    {
        _paths = paths;
        _logger = logger;
        _current = Load();
    }

    public event EventHandler<AppSettings>? Changed;

    public AppSettings Current
    {
        get { lock (_gate) return _current; }
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            _current = settings;
            try
            {
                JsonFile.WriteAtomic(_paths.SettingsFile, settings);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Could not save settings");
                throw;
            }
        }
        Changed?.Invoke(this, settings);
    }

    private AppSettings Load()
    {
        try
        {
            return JsonFile.Read<AppSettings>(_paths.SettingsFile) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Keep the unreadable file for inspection instead of silently overwriting it.
            _logger.LogError(ex, "Settings file is unreadable; using defaults");
            TryBackup(_paths.SettingsFile);
            return new AppSettings();
        }
    }

    private static void TryBackup(string path)
    {
        try
        {
            File.Copy(path, path + ".corrupt", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
