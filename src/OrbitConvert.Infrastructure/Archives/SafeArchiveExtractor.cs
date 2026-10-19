using System.IO.Compression;
using System.IO.Hashing;
using OrbitConvert.Core.Conversion;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace OrbitConvert.Infrastructure.Archives;

public sealed record ExtractionLimits
{
    public int MaxEntries { get; init; } = 200_000;
    public long MaxTotalBytes { get; init; } = 64L * 1024 * 1024 * 1024;

    /// <summary>Abort when output exceeds the archive size by this factor (and is over 1 GiB).</summary>
    public double MaxCompressionRatio { get; init; } = 2000;

    public static ExtractionLimits Default { get; } = new();
}

public sealed record ArchiveTestResult(int Entries, long UncompressedBytes, IReadOnlyList<string> Problems)
{
    public bool IsValid => Problems.Count == 0;
}

/// <summary>
/// Extracts ZIP and 7z archives without trusting entry names. Rejects absolute paths, drive
/// letters, ".." traversal, alternate data streams and reserved device names; skips symbolic
/// links; enforces entry-count, size and compression-ratio limits on bytes actually written;
/// and verifies CRC-32 checksums.
/// </summary>
public static class SafeArchiveExtractor
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static void Extract(string archivePath, string destination, ExtractionLimits? limits = null,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        limits ??= ExtractionLimits.Default;
        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var archiveSize = new FileInfo(archivePath).Length;
        var budget = new Budget(limits, archiveSize);

        if (IsZip(archivePath))
        {
            ExtractZip(archivePath, root, budget, progress, cancellationToken);
        }
        else
        {
            ExtractWithSharpCompress(archivePath, root, budget, progress, cancellationToken);
        }
    }

    public static ArchiveTestResult Test(string archivePath, CancellationToken cancellationToken = default,
        ExtractionLimits? limits = null)
    {
        limits ??= ExtractionLimits.Default;
        var problems = new List<string>();
        var entries = 0;
        long total = 0;
        var budget = new Budget(limits, new FileInfo(archivePath).Length);
        try
        {
            if (IsZip(archivePath))
            {
                using var zip = ZipFile.OpenRead(archivePath);
                foreach (var entry in zip.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    entries++;
                    budget.CountEntry();
                    if (IsDirectoryEntry(entry.FullName))
                    {
                        continue;
                    }
                    try
                    {
                        if (entry.IsEncrypted)
                        {
                            problems.Add($"{entry.FullName}: encrypted (cannot verify without password)");
                            continue;
                        }
                        budget.CheckDeclaredBytes(entry.Length);
                        using var s = entry.Open();
                        var (bytes, crc) = Drain(s, budget, cancellationToken);
                        total += bytes;
                        if (crc != entry.Crc32)
                        {
                            problems.Add($"{entry.FullName}: CRC mismatch (data is corrupted)");
                        }
                    }
                    catch (InvalidDataException ex)
                    {
                        problems.Add($"{entry.FullName}: {ex.Message}");
                    }
                }
            }
            else
            {
                using var archive = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions());
                using var reader = archive.ExtractAllEntries();
                while (reader.MoveToNextEntry())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = reader.Entry;
                    entries++;
                    budget.CountEntry();
                    if (entry.IsDirectory)
                    {
                        continue;
                    }
                    if (entry.IsEncrypted)
                    {
                        problems.Add($"{entry.Key}: encrypted (cannot verify without password)");
                        continue;
                    }
                    budget.CheckDeclaredBytes(entry.Size);
                    using var s = reader.OpenEntryStream();
                    var (bytes, crc) = Drain(s, budget, cancellationToken);
                    total += bytes;
                    if (entry.Crc != 0 && (uint)entry.Crc != crc)
                    {
                        problems.Add($"{entry.Key}: CRC mismatch (data is corrupted)");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ConversionException or InvalidDataException or IOException or InvalidOperationException
                                       or NotSupportedException or SharpCompress.Common.ArchiveException)
        {
            problems.Add($"The archive could not be read: {ex.Message}");
        }
        return new ArchiveTestResult(entries, total, problems);
    }

    /// <summary>
    /// Maps an archive entry name to a safe path under <paramref name="root"/>, or throws.
    /// Returns null for entries that should be ignored (empty names).
    /// </summary>
    public static string? ResolveEntryPath(string root, string entryName)
    {
        var name = entryName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (name.Length == 0 || name.All(c => c == Path.DirectorySeparatorChar))
        {
            return null;
        }
        if (Path.IsPathRooted(name) || name.StartsWith(Path.DirectorySeparatorChar))
        {
            throw Unsafe(entryName, "absolute path");
        }
        if (name.Contains(':'))
        {
            throw Unsafe(entryName, "drive letter or alternate data stream");
        }

        var segments = name.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment == "..")
            {
                throw Unsafe(entryName, "parent-directory traversal");
            }
            var stem = segment.Split('.')[0].TrimEnd(' ');
            if (ReservedNames.Contains(stem))
            {
                throw Unsafe(entryName, "reserved device name");
            }
            if (segment.EndsWith('.') && segment != "." || segment.EndsWith(' '))
            {
                throw Unsafe(entryName, "name ends with a dot or space");
            }
            if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw Unsafe(entryName, "invalid characters");
            }
        }

        var full = Path.GetFullPath(Path.Combine(root, string.Join(Path.DirectorySeparatorChar, segments.Where(s => s != "."))));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw Unsafe(entryName, "path escapes the destination folder");
        }
        return full;
    }

    private static ConversionException Unsafe(string entry, string reason) =>
        new($"The archive contains an unsafe entry and was not extracted: \"{entry}\" ({reason}).");

    private static void ExtractZip(string archivePath, string root, Budget budget, IProgress<double>? progress, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        var entries = zip.Entries;
        var index = 0;
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            budget.CountEntry();
            index++;

            var target = ResolveEntryPath(root, entry.FullName);
            if (target is null)
            {
                continue;
            }
            if (IsDirectoryEntry(entry.FullName))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            if (IsUnixSymlink(entry))
            {
                continue;
            }
            if (entry.IsEncrypted)
            {
                throw new ConversionException("Password-protected archives are not supported.");
            }
            budget.CheckDeclaredBytes(entry.Length);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            uint crc;
            using (var input = entry.Open())
            using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                crc = CopyWithBudget(input, output, budget, ct);
            }
            if (crc != entry.Crc32)
            {
                throw new ConversionException($"The archive is corrupted: checksum mismatch in \"{entry.FullName}\".");
            }
            TrySetTime(target, entry.LastWriteTime.UtcDateTime);
            progress?.Report((double)index / Math.Max(1, entries.Count));
        }
    }

    private static void ExtractWithSharpCompress(string archivePath, string root, Budget budget, IProgress<double>? progress, CancellationToken ct)
    {
        using var archive = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions());
        if (archive.IsEncrypted)
        {
            throw new ConversionException("Password-protected archives are not supported.");
        }
        var totalSize = Math.Max(1, archive.TotalUncompressedSize);
        long done = 0;
        using var reader = archive.ExtractAllEntries();
        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();
            var entry = reader.Entry;
            budget.CountEntry();

            var target = ResolveEntryPath(root, entry.Key ?? string.Empty);
            if (target is null)
            {
                continue;
            }
            if (entry.IsDirectory)
            {
                Directory.CreateDirectory(target);
                continue;
            }
            if (!string.IsNullOrEmpty(entry.LinkTarget))
            {
                continue;
            }
            if (entry.IsEncrypted)
            {
                throw new ConversionException("Password-protected archives are not supported.");
            }
            budget.CheckDeclaredBytes(entry.Size);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            uint crc;
            using (var input = reader.OpenEntryStream())
            using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                crc = CopyWithBudget(input, output, budget, ct);
            }
            if (entry.Crc != 0 && (uint)entry.Crc != crc)
            {
                throw new ConversionException($"The archive is corrupted: checksum mismatch in \"{entry.Key}\".");
            }
            if (entry.LastModifiedTime is { } modified)
            {
                TrySetTime(target, modified.ToUniversalTime());
            }
            done += entry.Size;
            progress?.Report((double)done / totalSize);
        }
    }

    private static uint CopyWithBudget(Stream input, Stream output, Budget budget, CancellationToken ct)
    {
        var crc = new Crc32();
        var buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            budget.CountBytes(read);
            crc.Append(buffer.AsSpan(0, read));
            output.Write(buffer, 0, read);
        }
        return crc.GetCurrentHashAsUInt32();
    }

    private static (long Bytes, uint Crc) Drain(Stream input, Budget budget, CancellationToken ct)
    {
        var crc = new Crc32();
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            budget.CountBytes(read);
            crc.Append(buffer.AsSpan(0, read));
            total += read;
        }
        return (total, crc.GetCurrentHashAsUInt32());
    }

    private static bool IsZip(string path)
    {
        Span<byte> header = stackalloc byte[4];
        using var fs = File.OpenRead(path);
        return fs.Read(header) == 4 && header[0] == 'P' && header[1] == 'K' && header[2] is 3 or 5 && header[3] is 4 or 6;
    }

    private static bool IsDirectoryEntry(string name) => name.EndsWith('/') || name.EndsWith('\\');

    /// <summary>Info-ZIP stores Unix mode bits in the high word of the external attributes.</summary>
    private static bool IsUnixSymlink(ZipArchiveEntry entry) => ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    private static void TrySetTime(string path, DateTime utc)
    {
        try
        {
            if (utc.Year > 1980)
            {
                File.SetLastWriteTimeUtc(path, utc);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    private sealed class Budget(ExtractionLimits limits, long archiveSize)
    {
        private int _entries;
        private long _bytes;

        public void CountEntry()
        {
            if (++_entries > limits.MaxEntries)
            {
                throw new ConversionException($"The archive has more than {limits.MaxEntries:N0} entries and was not extracted.");
            }
        }

        public void CountBytes(int count)
        {
            _bytes += count;
            if (_bytes > limits.MaxTotalBytes)
            {
                throw new ConversionException("The archive expands beyond the size limit and was not extracted.");
            }
            if (_bytes > 1L << 30 && _bytes > archiveSize * limits.MaxCompressionRatio)
            {
                throw new ConversionException("The archive has a suspicious compression ratio (possible \"zip bomb\") and was not extracted.");
            }
        }

        public void CheckDeclaredBytes(long count)
        {
            if (count < 0 || count > limits.MaxTotalBytes - _bytes)
            {
                throw new ConversionException("The archive expands beyond the size limit and was not extracted.");
            }
            if (count > 1L << 30 && count > archiveSize * limits.MaxCompressionRatio)
            {
                throw new ConversionException("The archive has a suspicious compression ratio (possible \"zip bomb\") and was not extracted.");
            }
        }
    }
}
