using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OrbitConvert.Core.Automation;

namespace OrbitConvert.Infrastructure.Automation;

/// <summary>
/// Watches the enabled <see cref="WatchFolder"/>s and hands each new, fully written file to a
/// callback exactly once. Files that are still being written, temporary files, and outputs that
/// OrbitConvert itself produced are ignored, so a watcher whose output lands in the watched folder
/// cannot convert its own results in a loop.
/// </summary>
public sealed class FolderAutomation : IDisposable
{
    private readonly Func<IReadOnlyList<WatchFolder>> _watchers;
    private readonly Func<WatchFolder, string, Task> _process;
    private readonly ILogger _logger;
    private readonly TimeSpan _settle;
    private readonly List<FileSystemWatcher> _active = [];
    private readonly ConcurrentDictionary<string, DateTime> _ignored = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _recent = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _disposed;

    /// <param name="settle">How long a file's size must stay unchanged before it is processed.</param>
    public FolderAutomation(Func<IReadOnlyList<WatchFolder>> watchers, Func<WatchFolder, string, Task> process, ILogger logger, TimeSpan? settle = null)
    {
        _watchers = watchers;
        _process = process;
        _logger = logger;
        _settle = settle ?? TimeSpan.FromSeconds(2);
    }

    /// <summary>Number of folders currently being watched.</summary>
    public int ActiveCount
    {
        get { lock (_gate) return _active.Count; }
    }

    /// <summary>(Re)creates file system watchers for every enabled watcher whose folder exists.</summary>
    public void Reload()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var w in _active)
            {
                w.Dispose();
            }
            _active.Clear();
            foreach (var watcher in _watchers().Where(w => w.Enabled))
            {
                if (!Directory.Exists(watcher.Path))
                {
                    watcher.LastError = "The folder does not exist.";
                    continue;
                }
                var fsw = new FileSystemWatcher(watcher.Path)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                    InternalBufferSize = 64 * 1024,
                };
                var id = watcher.Id;
                fsw.Created += (_, e) => OnFile(id, e.FullPath);
                fsw.Renamed += (_, e) => OnFile(id, e.FullPath);
                fsw.Error += (_, e) => _logger.LogWarning(e.GetException(), "Folder watcher error for {Path}", watcher.Path);
                fsw.EnableRaisingEvents = true;
                _active.Add(fsw);
                _logger.LogInformation("Watching {Path}", watcher.Path);
            }
        }
    }

    /// <summary>Marks a path OrbitConvert wrote, so a watcher never processes its own output.</summary>
    public void Ignore(string path) => _ignored[Path.GetFullPath(path)] = DateTime.UtcNow;

    private void OnFile(string watcherId, string path)
    {
        var full = Path.GetFullPath(path);
        if (WatchFolder.IsTransient(full) || _ignored.ContainsKey(full) || IsOwnWorkFile(full))
        {
            return;
        }
        var watcher = _watchers().FirstOrDefault(w => w.Id == watcherId);
        if (watcher is null || !watcher.Enabled || !watcher.Matches(full))
        {
            return;
        }
        // A rename after a download, or Created + Renamed for the same save, must not run twice.
        if (_recent.TryGetValue(full, out var last) && DateTime.UtcNow - last < TimeSpan.FromSeconds(10))
        {
            return;
        }
        if (!_inFlight.TryAdd(full, 0))
        {
            return;
        }
        _ = Task.Run(() => SettleAndProcessAsync(watcher, full));
    }

    private static bool IsOwnWorkFile(string path) =>
        Path.GetFileName(path).StartsWith('.') || path.EndsWith(".partial", StringComparison.OrdinalIgnoreCase);

    private async Task SettleAndProcessAsync(WatchFolder watcher, string path)
    {
        try
        {
            if (!await WaitUntilCompleteAsync(path).ConfigureAwait(false))
            {
                return;
            }
            if (_ignored.ContainsKey(path))
            {
                return;
            }
            _recent[path] = DateTime.UtcNow;
            await _process(watcher, path).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Automation failed for {Path}", path);
            watcher.LastError = ex.Message;
        }
        finally
        {
            _inFlight.TryRemove(path, out _);
        }
    }

    /// <summary>Waits until the file has stopped growing and no other program has it open for writing.</summary>
    internal async Task<bool> WaitUntilCompleteAsync(string path, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(10));
        long lastSize = -1;
        var stableSince = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline && !_disposed)
        {
            if (!File.Exists(path))
            {
                return false;
            }
            long size;
            try
            {
                size = new FileInfo(path).Length;
            }
            catch (IOException)
            {
                size = -1;
            }
            if (size != lastSize)
            {
                lastSize = size;
                stableSince = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - stableSince >= _settle && CanOpenExclusively(path))
            {
                return true;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(_settle.TotalMilliseconds / 4, 50, 500))).ConfigureAwait(false);
        }
        return false;
    }

    private static bool CanOpenExclusively(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var w in _active)
            {
                w.Dispose();
            }
            _active.Clear();
        }
    }
}
