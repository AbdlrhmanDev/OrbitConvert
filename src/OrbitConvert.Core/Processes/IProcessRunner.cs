namespace OrbitConvert.Core.Processes;

/// <summary>
/// Describes a child process. Arguments are passed as a list and are never concatenated
/// into a shell command line, so file names cannot inject options or commands.
/// </summary>
public sealed record ProcessSpec(string FileName, IReadOnlyList<string> Arguments)
{
    public string? WorkingDirectory { get; init; }
    public Action<string>? OnStandardOutputLine { get; init; }
    public Action<string>? OnStandardErrorLine { get; init; }
    public TimeSpan? Timeout { get; init; }
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
}

public sealed record ProcessResult(int ExitCode, string StandardOutputTail, string StandardErrorTail, TimeSpan Duration)
{
    public bool Succeeded => ExitCode == 0;
}

public interface IProcessRunner
{
    /// <summary>
    /// Runs the process to completion. On cancellation the whole process tree is terminated
    /// and <see cref="OperationCanceledException"/> is thrown.
    /// </summary>
    Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken);
}
