using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace OrbitConvert.Core.Conversion;

public enum ConversionStatus { Succeeded, Failed, Canceled, Skipped }

public sealed record ConversionResult(
    ConversionStatus Status,
    IReadOnlyList<string> Outputs,
    string? Error,
    TimeSpan Duration,
    string? Note = null,
    string? TechnicalDetail = null)
{
    public bool Succeeded => Status == ConversionStatus.Succeeded;
}

/// <summary>Provides private scratch directories for conversions.</summary>
public interface IWorkspaceProvider
{
    /// <summary>Creates a new, empty, private directory. The caller deletes it.</summary>
    string CreateWorkDirectory();
}

public sealed class TempWorkspaceProvider : IWorkspaceProvider
{
    private readonly string _root;

    public TempWorkspaceProvider(string? root = null)
    {
        // Short root keeps external tools well clear of MAX_PATH even for long input names.
        _root = root ?? Path.Combine(Path.GetTempPath(), "OrbitConvert", "w");
    }

    public string Root => _root;

    public string CreateWorkDirectory()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Removes work directories left behind by a crash.</summary>
    public void CleanupStale(TimeSpan olderThan)
    {
        if (!Directory.Exists(_root))
        {
            return;
        }
        foreach (var dir in Directory.EnumerateDirectories(_root))
        {
            try
            {
                if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(dir) > olderThan)
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

/// <summary>
/// Runs a single <see cref="ConversionPlan"/>: checks inputs, executes the converter in a private
/// work directory, validates every output, then commits outputs to the destination. On any failure
/// or cancellation nothing partial is left at the destination and the work directory is removed.
/// </summary>
public sealed class ConversionEngine(IWorkspaceProvider workspace, OutputValidator validator, ILogger<ConversionEngine> logger)
{
    private const long MinimumFreeBytes = 50L * 1024 * 1024;

    public async Task<ConversionResult> RunAsync(ConversionPlan plan, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        string? workDir = null;
        try
        {
            CheckInputs(plan);
            var outputDir = plan.Output.ResolveDirectory(plan.Inputs[0]);
            CheckOutputDirectory(outputDir);

            workDir = workspace.CreateWorkDirectory();
            var context = new ConversionContext
            {
                Route = plan.Route,
                Inputs = plan.Inputs,
                Options = plan.Options,
                WorkDirectory = workDir,
                Logger = logger,
                Progress = progress,
            };

            logger.LogInformation("Starting {Converter}:{Target} for {Count} input(s)", plan.Converter.Id, plan.Route.TargetKey, plan.Inputs.Count);
            var produced = await plan.Converter.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (produced.Count == 0)
            {
                throw new ConversionException("The conversion finished without producing any output.");
            }
            foreach (var file in produced)
            {
                EnsureInside(workDir, file);
                validator.Validate(file, plan.Route.Output);
            }

            var committed = CommitOutputs(produced, outputDir, plan);
            progress?.Report(1);
            stopwatch.Stop();

            if (committed.Count == 0)
            {
                return new ConversionResult(ConversionStatus.Skipped, [], "An output with the same name already exists.", stopwatch.Elapsed, context.ResultNote);
            }
            logger.LogInformation("Finished {Title} in {Elapsed}", plan.Title, stopwatch.Elapsed);
            return new ConversionResult(ConversionStatus.Succeeded, committed, null, stopwatch.Elapsed, context.ResultNote);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Canceled {Title}", plan.Title);
            return new ConversionResult(ConversionStatus.Canceled, [], "Canceled.", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            var detail = (ex as ConversionException)?.TechnicalDetail;
            logger.LogWarning(ex, "Conversion failed: {Title}. {Detail}", plan.Title, detail);
            return new ConversionResult(ConversionStatus.Failed, [], ErrorTranslator.ToUserMessage(ex), stopwatch.Elapsed, null, detail);
        }
        finally
        {
            if (workDir is not null)
            {
                TryDeleteDirectory(workDir);
            }
        }
    }

    private static void CheckInputs(ConversionPlan plan)
    {
        foreach (var input in plan.Inputs)
        {
            if (!File.Exists(input.Path))
            {
                throw new ConversionException($"File not found: {input.FileName}");
            }
            // Opening with permissive sharing distinguishes "locked for reading" from "open in an editor".
            using var _ = new FileStream(input.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
    }

    private static void CheckOutputDirectory(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        try
        {
            var root = Path.GetPathRoot(outputDir);
            if (!string.IsNullOrEmpty(root) && !root.StartsWith(@"\\", StringComparison.Ordinal))
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady && drive.AvailableFreeSpace < MinimumFreeBytes)
                {
                    throw new ConversionException($"The destination drive {root} is almost full.");
                }
            }
        }
        catch (ArgumentException)
        {
            // Not a regular drive (e.g. a mapped path we cannot query); proceed.
        }
    }

    private static void EnsureInside(string workDir, string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(workDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConversionException("Internal error: a converter wrote outside its work directory.");
        }
    }

    private static List<string> CommitOutputs(IReadOnlyList<string> produced, string outputDir, ConversionPlan plan)
    {
        var protectedPaths = plan.Inputs.Select(i => Path.GetFullPath(i.Path)).ToArray();
        var committed = new List<string>();
        foreach (var file in produced)
        {
            var name = Path.GetFileName(file.TrimEnd(Path.DirectorySeparatorChar));
            var destination = OutputPlanner.ChooseDestination(outputDir, name, plan.Output.Collision, protectedPaths);
            if (destination is null)
            {
                continue;
            }
            var overwrite = plan.Output.Collision == CollisionStrategy.Overwrite && !Directory.Exists(file);
            committed.Add(OutputPlanner.Commit(file, destination, overwrite));
        }
        return committed;
    }

    private void TryDeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A just-killed child process may still hold a handle for a moment.
                Thread.Sleep(200);
                if (attempt == 2)
                {
                    logger.LogWarning(ex, "Could not delete work directory {Path}", path);
                }
            }
        }
    }
}
