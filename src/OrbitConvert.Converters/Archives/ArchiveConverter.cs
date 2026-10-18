using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;
using OrbitConvert.Core.Processes;
using OrbitConvert.Core.Tools;
using OrbitConvert.Infrastructure.Archives;

namespace OrbitConvert.Converters.Archives;

/// <summary>Create ZIP (built in) or 7z (via 7-Zip) archives, and safely extract ZIP and 7z archives.</summary>
public sealed partial class ArchiveConverter(IToolLocator tools, IProcessRunner runner) : IConverter
{
    public const string ExtractKey = "extract";

    public string Id => "archives";
    public string DisplayName => "Archives";

    public IReadOnlyList<ConversionRoute> Routes { get; } =
    [
        new()
        {
            TargetKey = KnownFormats.Zip.Id, Label = "ZIP", Kind = RouteKind.Combine,
            Inputs = new HashSet<string> { ConversionRoute.AnyFormat }, Output = KnownFormats.Zip, Priority = 90,
            Description = "Compress the selected files into a ZIP archive",
        },
        new()
        {
            TargetKey = KnownFormats.SevenZip.Id, Label = "7Z", Kind = RouteKind.Combine,
            Inputs = new HashSet<string> { ConversionRoute.AnyFormat }, Output = KnownFormats.SevenZip, Priority = 91,
            Description = "Compress the selected files into a 7z archive",
        },
        new()
        {
            TargetKey = ExtractKey, Label = "Extract", Kind = RouteKind.Extract,
            Inputs = ConversionRoute.Formats(KnownFormats.Zip, KnownFormats.SevenZip), Output = null, Priority = 1,
            Description = "Extract into a new folder next to the archive",
            Limitations = "Password-protected archives are not supported. Unsafe entries (absolute paths, \"..\") stop the extraction.",
        },
    ];

    public RouteAvailability GetAvailability(ConversionRoute route) =>
        route.Output == KnownFormats.SevenZip && tools.Find(ExternalTool.SevenZip) is null
            ? RouteAvailability.Missing(ExternalTool.SevenZip)
            : RouteAvailability.Available;

    public Task<IReadOnlyList<string>> ExecuteAsync(ConversionContext context, CancellationToken cancellationToken) =>
        context.Route.TargetKey switch
        {
            ExtractKey => Task.Run(() => Extract(context, cancellationToken), cancellationToken),
            "zip" => Task.Run(() => CreateZip(context, cancellationToken), cancellationToken),
            _ => CreateSevenZipAsync(context, cancellationToken),
        };

    public static ArchiveTestResult Test(string path, CancellationToken cancellationToken = default) =>
        SafeArchiveExtractor.Test(path, cancellationToken);

    internal static string ArchiveBaseName(ConversionContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.Options.Archive.ArchiveName))
        {
            return context.Options.Archive.ArchiveName!;
        }
        if (context.Inputs.Count == 1)
        {
            return context.Input.BaseName;
        }
        var parent = Path.GetFileName(Path.GetDirectoryName(context.Input.Path)?.TrimEnd(Path.DirectorySeparatorChar));
        return string.IsNullOrEmpty(parent) ? "Archive" : parent;
    }

    private static IReadOnlyList<string> Extract(ConversionContext context, CancellationToken ct)
    {
        var folder = Path.Combine(context.WorkDirectory, ConversionContext.SanitizeFileName(context.Input.BaseName));
        SafeArchiveExtractor.Extract(context.Input.Path, folder, progress: context.Progress, cancellationToken: ct);
        var count = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Count();
        context.ResultNote = $"{count} file(s) extracted";
        return [folder];
    }

    private static IReadOnlyList<string> CreateZip(ConversionContext context, CancellationToken ct)
    {
        var path = context.WorkPath(ArchiveBaseName(context), ".zip");
        var level = context.Options.Archive.Compression switch
        {
            ArchiveCompression.Fastest => CompressionLevel.Fastest,
            ArchiveCompression.Smallest => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Optimal,
        };
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = context.Inputs.Sum(i => i.Length), done = 0;
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var input in context.Inputs)
            {
                ct.ThrowIfCancellationRequested();
                var name = UniqueName(names, input.FileName);
                var entry = zip.CreateEntry(name, level);
                entry.LastWriteTime = File.GetLastWriteTime(input.Path);
                using var source = new FileStream(input.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var target = entry.Open();
                var buffer = new byte[81920];
                int read;
                while ((read = source.Read(buffer)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    target.Write(buffer, 0, read);
                    done += read;
                    context.ReportProgress(total == 0 ? 0 : (double)done / total);
                }
            }
        }
        context.ResultNote = $"{context.Inputs.Count} file(s), {Media.FfmpegConverter.FormatSize(new FileInfo(path).Length)}";
        return [path];
    }

    private async Task<IReadOnlyList<string>> CreateSevenZipAsync(ConversionContext context, CancellationToken ct)
    {
        var sevenZip = tools.Find(ExternalTool.SevenZip) ?? throw new ConversionException("7-Zip is not installed.");
        var path = context.WorkPath(ArchiveBaseName(context), ".7z");
        var level = context.Options.Archive.Compression switch
        {
            ArchiveCompression.Fastest => "-mx=1",
            ArchiveCompression.Smallest => "-mx=9",
            _ => "-mx=5",
        };
        // "--" stops switch parsing; absolute paths can never start with '-' or '@' (list-file syntax).
        var args = new List<string> { "a", "-t7z", level, "-bsp1", "-bso0", "-bse1", "-y", "--", path };
        args.AddRange(context.Inputs.Select(i => Path.GetFullPath(i.Path)));

        var result = await runner.RunAsync(new ProcessSpec(sevenZip, args)
        {
            WorkingDirectory = context.WorkDirectory,
            OnStandardOutputLine = line =>
            {
                var m = PercentLine().Match(line);
                if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pct))
                {
                    context.ReportProgress(pct / 100.0);
                }
            },
        }, ct).ConfigureAwait(false);

        if (!result.Succeeded || !File.Exists(path))
        {
            throw new ConversionException($"7-Zip could not create the archive (exit code {result.ExitCode}).",
                technicalDetail: result.StandardOutputTail + result.StandardErrorTail);
        }
        return [path];
    }

    private static string UniqueName(HashSet<string> used, string name)
    {
        var candidate = name;
        for (var i = 1; !used.Add(candidate); i++)
        {
            candidate = $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}";
        }
        return candidate;
    }

    [GeneratedRegex(@"^\s*(\d{1,3})%")]
    private static partial Regex PercentLine();
}
