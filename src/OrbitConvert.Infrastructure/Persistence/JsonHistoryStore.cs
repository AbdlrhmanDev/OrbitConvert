using System.Text.Json;
using Microsoft.Extensions.Logging;
using OrbitConvert.Core.History;
using OrbitConvert.Core.Settings;

namespace OrbitConvert.Infrastructure.Persistence;

/// <summary>
/// Conversion history stored locally as JSON. Respects the "Keep history" privacy setting:
/// when it is off, entries are neither kept in memory nor written to disk.
/// </summary>
public sealed class JsonHistoryStore : IHistoryStore
{
    private readonly AppPaths _paths;
    private readonly ISettingsStore _settings;
    private readonly ILogger<JsonHistoryStore> _logger;
    private readonly object _gate = new();
    private List<HistoryEntry> _entries;

    public JsonHistoryStore(AppPaths paths, ISettingsStore settings, ILogger<JsonHistoryStore> logger)
    {
        _paths = paths;
        _settings = settings;
        _logger = logger;
        _entries = Load();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<HistoryEntry> Entries
    {
        get { lock (_gate) return _entries.ToList(); }
    }

    public void Add(HistoryEntry entry)
    {
        var settings = _settings.Current;
        if (!settings.KeepHistory)
        {
            return;
        }
        lock (_gate)
        {
            _entries.Insert(0, entry);
            if (_entries.Count > settings.HistoryLimit)
            {
                _entries.RemoveRange(settings.HistoryLimit, _entries.Count - settings.HistoryLimit);
            }
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(Guid id)
    {
        lock (_gate)
        {
            _entries.RemoveAll(e => e.Id == id);
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries = [];
            try
            {
                if (File.Exists(_paths.HistoryFile))
                {
                    File.Delete(_paths.HistoryFile);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Could not delete history file");
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Persist()
    {
        try
        {
            JsonFile.WriteAtomic(_paths.HistoryFile, _entries);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not save history");
        }
    }

    private List<HistoryEntry> Load()
    {
        if (!_settings.Current.KeepHistory)
        {
            return [];
        }
        try
        {
            return JsonFile.Read<List<HistoryEntry>>(_paths.HistoryFile) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "History file is unreadable; starting empty");
            return [];
        }
    }
}
