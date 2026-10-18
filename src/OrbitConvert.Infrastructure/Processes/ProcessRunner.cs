using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using OrbitConvert.Core.Processes;

namespace OrbitConvert.Infrastructure.Processes;

/// <summary>
/// Starts external tools without a shell, using <see cref="ProcessStartInfo.ArgumentList"/> so every
/// argument is escaped by the runtime. Each child is placed in a Windows Job Object configured with
/// KILL_ON_JOB_CLOSE: if OrbitConvert exits or crashes, the tool and anything it spawned
/// (e.g. LibreOffice's soffice.bin) are terminated by the OS instead of being orphaned.
/// </summary>
public sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    private const int TailLength = 8 * 1024;

    public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var psi = new ProcessStartInfo
        {
            FileName = spec.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = spec.WorkingDirectory ?? Path.GetTempPath(),
        };
        foreach (var arg in spec.Arguments)
        {
            psi.ArgumentList.Add(arg);
        }
        if (spec.Environment is not null)
        {
            foreach (var (key, value) in spec.Environment)
            {
                psi.Environment[key] = value;
            }
        }

        var stdout = new TailBuffer(TailLength);
        var stderr = new TailBuffer(TailLength);
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdoutDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) { stdoutDone.TrySetResult(); return; }
            stdout.AppendLine(e.Data);
            spec.OnStandardOutputLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) { stderrDone.TrySetResult(); return; }
            stderr.AppendLine(e.Data);
            spec.OnStandardErrorLine?.Invoke(e.Data);
        };

        logger.LogDebug("Starting {File} with {Count} argument(s)", Path.GetFileName(spec.FileName), spec.Arguments.Count);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new Core.Conversion.ConversionException($"Could not start {Path.GetFileName(spec.FileName)}: {ex.Message}", ex);
        }

        using var job = JobObject.TryCreateKillOnClose(logger);
        job?.TryAssign(process);

        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = spec.Timeout is { } timeout ? new CancellationTokenSource(timeout) : null;
        using var linked = timeoutCts is null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            await Task.WhenAll(stdoutDone.Task, stderrDone.Task).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process, job);
            if (timeoutCts?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
            {
                throw new Core.Conversion.ConversionException(
                    $"{Path.GetFileName(spec.FileName)} did not finish within {spec.Timeout!.Value.TotalMinutes:0} minutes and was stopped.");
            }
            throw;
        }
        catch (TimeoutException)
        {
            // Output pipes stayed open (a grandchild inherited them); the main process has exited.
        }

        stopwatch.Stop();
        logger.LogDebug("{File} exited with {Code} after {Elapsed}", Path.GetFileName(spec.FileName), process.ExitCode, stopwatch.Elapsed);
        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString(), stopwatch.Elapsed);
    }

    private void Kill(Process process, JobObject? job)
    {
        try
        {
            job?.Terminate();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogDebug(ex, "Process already exited while canceling");
        }
    }

    /// <summary>Keeps only the last N characters of a stream for diagnostics.</summary>
    private sealed class TailBuffer(int capacity)
    {
        private readonly StringBuilder _sb = new();
        private readonly object _gate = new();

        public void AppendLine(string line)
        {
            lock (_gate)
            {
                _sb.AppendLine(line);
                if (_sb.Length > capacity * 2)
                {
                    _sb.Remove(0, _sb.Length - capacity);
                }
            }
        }

        public override string ToString()
        {
            lock (_gate)
            {
                return _sb.Length > capacity ? _sb.ToString(_sb.Length - capacity, capacity) : _sb.ToString();
            }
        }
    }
}
