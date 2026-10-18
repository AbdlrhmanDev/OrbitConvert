namespace OrbitConvert.Core.Conversion;

public enum CollisionStrategy
{
    /// <summary>Keep the existing file and name the new one "name (1).ext".</summary>
    KeepBoth,
    /// <summary>Replace an existing output file. Input files are never overwritten.</summary>
    Overwrite,
    /// <summary>Leave the existing file and skip writing this output.</summary>
    Skip,
}

/// <param name="Directory">Destination folder; null means beside the first input file.</param>
public sealed record OutputSettings(string? Directory = null, CollisionStrategy Collision = CollisionStrategy.KeepBoth)
{
    public static OutputSettings BesideOriginal { get; } = new();

    public string ResolveDirectory(InputFile firstInput) =>
        Directory is { Length: > 0 } dir ? Path.GetFullPath(dir) : Path.GetDirectoryName(firstInput.Path)!;
}

/// <summary>
/// Chooses final output names and moves finished files out of the work directory without
/// ever exposing a partially written file at the destination.
/// </summary>
public static class OutputPlanner
{
    private const int MaxNumberedAttempts = 10_000;

    /// <summary>
    /// Returns the destination for <paramref name="fileName"/>, or null when it must be skipped.
    /// </summary>
    public static string? ChooseDestination(
        string directory, string fileName, CollisionStrategy strategy, IReadOnlyCollection<string> protectedPaths)
    {
        var candidate = Path.Combine(directory, fileName);
        var exists = File.Exists(candidate) || Directory.Exists(candidate);
        var isProtected = protectedPaths.Contains(Path.GetFullPath(candidate), StringComparer.OrdinalIgnoreCase);

        if (!exists && !isProtected)
        {
            return candidate;
        }

        switch (strategy)
        {
            case CollisionStrategy.Skip when !isProtected:
                return null;
            case CollisionStrategy.Overwrite when !isProtected && !Directory.Exists(candidate):
                return candidate;
            default:
                return NextFreeName(directory, fileName, protectedPaths);
        }
    }

    public static string NextFreeName(string directory, string fileName, IReadOnlyCollection<string>? protectedPaths = null)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 1; i < MaxNumberedAttempts; i++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)
                && protectedPaths?.Contains(Path.GetFullPath(candidate), StringComparer.OrdinalIgnoreCase) != true)
            {
                return candidate;
            }
        }
        throw new ConversionException($"Could not find a free file name for {fileName} in {directory}.");
    }

    /// <summary>
    /// Moves a finished file or folder to its destination. Same-volume moves are atomic renames;
    /// cross-volume moves copy to a hidden temporary name first and then rename.
    /// Returns the path actually used, which differs from <paramref name="destination"/> if another
    /// process created that name in the meantime (KeepBoth semantics).
    /// </summary>
    public static string Commit(string source, string destination, bool overwrite)
    {
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(source))
                {
                    CommitDirectory(source, destination);
                }
                else
                {
                    CommitFile(source, destination, overwrite);
                }
                return destination;
            }
            catch (IOException) when (!overwrite && (File.Exists(destination) || Directory.Exists(destination)))
            {
                // Lost a race with another writer; pick the next free name.
                destination = NextFreeName(directory, Path.GetFileName(destination));
            }
        }
        throw new ConversionException($"Could not save {Path.GetFileName(destination)}: the name kept being taken.");
    }

    private static void CommitFile(string source, string destination, bool overwrite)
    {
        if (SameVolume(source, destination))
        {
            File.Move(source, destination, overwrite);
            return;
        }

        var partial = Path.Combine(Path.GetDirectoryName(destination)!, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.partial");
        try
        {
            File.Copy(source, partial, overwrite: false);
            File.SetAttributes(partial, File.GetAttributes(partial) | FileAttributes.Hidden);
            File.Move(partial, destination, overwrite);
            File.SetAttributes(destination, File.GetAttributes(destination) & ~FileAttributes.Hidden);
        }
        finally
        {
            TryDelete(partial);
        }
    }

    private static void CommitDirectory(string source, string destination)
    {
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException($"{destination} already exists.");
        }

        if (SameVolume(source, destination))
        {
            Directory.Move(source, destination);
            return;
        }

        var partial = Path.Combine(Path.GetDirectoryName(destination)!, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.partial");
        try
        {
            CopyDirectory(source, partial);
            Directory.Move(partial, destination);
        }
        catch
        {
            if (Directory.Exists(partial))
            {
                Directory.Delete(partial, recursive: true);
            }
            throw;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }

    private static bool SameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
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
