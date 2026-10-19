using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace OrbitConvert.Infrastructure.Logging;

/// <summary>
/// Minimal daily-rolling file logger. Logs stay on this computer; nothing is ever transmitted.
/// Files older than <see cref="RetentionDays"/> are deleted at startup.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const int RetentionDays = 14;

    private readonly string _directory;
    private readonly LogLevel _minimumLevel;
    private readonly BlockingCollection<string> _queue = new(boundedCapacity: 10_000);
    private readonly Thread _writer;

    public FileLoggerProvider(string directory, LogLevel minimumLevel = LogLevel.Information)
    {
        _directory = directory;
        _minimumLevel = minimumLevel;
        Directory.CreateDirectory(directory);
        DeleteOldLogs();
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "OrbitConvert log writer" };
        _writer.Start();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, ShortCategory(categoryName));

    public void Dispose()
    {
        _queue.CompleteAdding();
        _writer.Join(TimeSpan.FromSeconds(2));
    }

    private void Enqueue(string line)
    {
        if (!_queue.IsAddingCompleted)
        {
            _queue.TryAdd(line);
        }
    }

    private void WriteLoop()
    {
        StreamWriter? writer = null;
        DateOnly openDay = default;
        try
        {
            while (!_queue.IsCompleted)
            {
                if (!_queue.TryTake(out var line, millisecondsTimeout: 250))
                {
                    writer?.Flush();
                    continue;
                }

                try
                {
                    var today = DateOnly.FromDateTime(DateTime.Now);
                    if (writer is null || today != openDay)
                    {
                        writer?.Dispose();
                        var file = Path.Combine(_directory, $"orbitconvert-{today:yyyyMMdd}.log");
                        writer = new StreamWriter(new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read,
                            bufferSize: 64 * 1024, FileOptions.SequentialScan), new UTF8Encoding(false), bufferSize: 64 * 1024);
                        openDay = today;
                    }

                    writer.Write(line);
                    // Drain a burst with the same open file handle instead of opening the log once per message.
                    while (_queue.TryTake(out line))
                    {
                        writer.Write(line);
                    }
                    writer.Flush();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    writer?.Dispose();
                    writer = null;
                }
            }
        }
        finally
        {
            writer?.Dispose();
        }
    }

    private void DeleteOldLogs()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_directory, "orbitconvert-*.log"))
            {
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-RetentionDays))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string ShortCategory(string category)
    {
        var dot = category.LastIndexOf('.');
        return dot >= 0 ? category[(dot + 1)..] : category;
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= provider._minimumLevel && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
              .Append(" [").Append(Level(logLevel)).Append("] ")
              .Append(category).Append(": ")
              .AppendLine(formatter(state, exception));
            if (exception is not null)
            {
                sb.AppendLine(exception.ToString());
            }
            provider.Enqueue(sb.ToString());
        }

        private static string Level(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            _ => "CRT",
        };
    }
}
