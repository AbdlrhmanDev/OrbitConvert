using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrbitConvert.Converters;
using OrbitConvert.Converters.Archives;
using OrbitConvert.Converters.Images;
using OrbitConvert.Converters.Pdf;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Queue;
using OrbitConvert.Core.Settings;
using OrbitConvert.Core.Tools;
using OrbitConvert.Infrastructure;
using OrbitConvert.Infrastructure.Logging;
using OrbitConvert.Infrastructure.Tools;

namespace OrbitConvert.Cli;

public static class Program
{
    private const int ExitOk = 0, ExitFailed = 1, ExitUsage = 2;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return args.Length == 0 ? ExitUsage : ExitOk;
        }
        if (args[0] is "--version" or "version")
        {
            Console.WriteLine($"OrbitConvert CLI {typeof(Program).Assembly.GetName().Version?.ToString(3)}");
            return ExitOk;
        }

        var paths = AppPaths.CreateDefault();
        paths.EnsureCreated();
        var verbose = args.Contains("--verbose");
        var services = new ServiceCollection()
            .AddLogging(b =>
            {
                b.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
                b.AddProvider(new FileLoggerProvider(paths.LogsDirectory));
                if (verbose)
                {
                    b.AddSimpleConsole(o => o.SingleLine = true);
                }
            })
            .AddOrbitConvert(paths)
            .BuildServiceProvider();

        try
        {
            var options = CliOptions.Parse(args.Skip(1).Where(a => a != "--verbose").ToArray());
            return args[0] switch
            {
                "detect" => Detect(services, options),
                "targets" => Targets(services, options),
                "convert" => await ConvertAsync(services, options),
                "pdf-info" => PdfInfo(options),
                "test-archive" => TestArchive(options),
                "tools" => Tools(services),
                "install-tool" => await InstallToolAsync(services, options),
                _ => Usage($"Unknown command '{args[0]}'."),
            };
        }
        catch (CliUsageException ex)
        {
            return Usage(ex.Message);
        }
        catch (ConversionException ex)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            return ExitFailed;
        }
        finally
        {
            await services.DisposeAsync();
        }
    }

    private static int Detect(IServiceProvider sp, CliOptions o)
    {
        var registry = sp.GetRequiredService<ConversionCapabilityRegistry>();
        foreach (var input in registry.Analyze(o.RequireFiles()))
        {
            var d = input.Detection!;
            Console.WriteLine($"{input.FileName}: {input.Format?.DisplayName ?? "unknown"} ({d.Method}){(d.Warning is null ? "" : " — " + d.Warning)}");
        }
        return ExitOk;
    }

    private static int Targets(IServiceProvider sp, CliOptions o)
    {
        var registry = sp.GetRequiredService<ConversionCapabilityRegistry>();
        var inputs = registry.Analyze(o.RequireFiles());
        var targets = registry.GetTargets(inputs);
        if (targets.Count == 0)
        {
            Console.WriteLine("No conversions are available for this selection.");
            return ExitFailed;
        }
        foreach (var t in targets)
        {
            var state = t.IsAvailable ? "available  " : "unavailable";
            var extra = t.IsAvailable ? (t.Experimental ? " [experimental]" : "") : " — " + t.UnavailableReason;
            Console.WriteLine($"  {t.Key,-20} {state} {t.Label,-16}{extra}");
        }
        return ExitOk;
    }

    private static async Task<int> ConvertAsync(IServiceProvider sp, CliOptions o)
    {
        var registry = sp.GetRequiredService<ConversionCapabilityRegistry>();
        var settings = sp.GetRequiredService<ISettingsStore>().Current;
        var target = o.Get("--to") ?? throw new CliUsageException("convert requires --to <target>.");
        var inputs = registry.Analyze(o.RequireFiles());
        foreach (var bad in inputs.Where(i => i.Detection?.Method == Core.Formats.DetectionMethod.Unknown && target is not ("zip" or "7z")))
        {
            Console.Error.WriteLine($"Warning: {bad.FileName}: {bad.Detection!.Warning}");
        }

        var conversion = settings.Defaults.Clone();
        o.ApplyTo(conversion);
        var collision = o.Has("--overwrite") ? CollisionStrategy.Overwrite : o.Has("--skip") ? CollisionStrategy.Skip : settings.Collision;
        var output = new OutputSettings(o.Get("--out"), collision);

        var plans = registry.CreatePlans(inputs, target, conversion, output, out var skipped);
        foreach (var s in skipped)
        {
            Console.WriteLine($"Skipped {s.FileName}: already {s.Format?.Label}.");
        }
        if (plans.Count == 0)
        {
            return ExitOk;
        }

        using var queue = sp.GetRequiredService<ConversionQueue>();
        queue.MaxConcurrency = o.GetInt("--jobs") ?? settings.MaxParallelJobs;
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.Error.WriteLine("Canceling...");
            queue.CancelAll();
        };

        var jobs = queue.Enqueue(plans);
        await queue.WaitForIdleAsync(cts.Token);

        var failed = 0;
        foreach (var job in jobs)
        {
            switch (job.Status)
            {
                case JobStatus.Succeeded:
                    foreach (var file in job.Outputs)
                    {
                        Console.WriteLine($"OK    {job.Title}: {file}{(job.Note is null ? "" : $" ({job.Note})")}");
                    }
                    break;
                case JobStatus.Skipped:
                    Console.WriteLine($"SKIP  {job.Title}: {job.Error}");
                    break;
                default:
                    failed++;
                    Console.Error.WriteLine($"FAIL  {job.Title}: {job.Error}");
                    if (o.Has("--details") && job.TechnicalDetail is { Length: > 0 } detail)
                    {
                        Console.Error.WriteLine(detail);
                    }
                    break;
            }
        }
        return failed == 0 ? ExitOk : ExitFailed;
    }

    private static int PdfInfo(CliOptions o)
    {
        var info = PdfInspector.Inspect(o.RequireFiles().Single());
        if (info.Error is not null)
        {
            Console.Error.WriteLine(info.Error);
            return ExitFailed;
        }
        Console.WriteLine($"Pages:     {info.PageCount}");
        Console.WriteLine($"Version:   PDF {info.Version}");
        Console.WriteLine($"Size:      {info.FileSize:N0} bytes");
        Console.WriteLine($"Title:     {info.Title}");
        Console.WriteLine($"Author:    {info.Author}");
        Console.WriteLine($"Subject:   {info.Subject}");
        Console.WriteLine($"Keywords:  {info.Keywords}");
        Console.WriteLine($"Creator:   {info.Creator}");
        Console.WriteLine($"Producer:  {info.Producer}");
        Console.WriteLine($"Created:   {info.Created}");
        Console.WriteLine($"Modified:  {info.Modified}");
        if (info.PageSizes.Count > 0)
        {
            var (w, h) = info.PageSizes[0];
            Console.WriteLine($"Page 1:    {w:0} × {h:0} pt ({w / 72 * 25.4:0} × {h / 72 * 25.4:0} mm)");
        }
        return ExitOk;
    }

    private static int TestArchive(CliOptions o)
    {
        var result = ArchiveConverter.Test(o.RequireFiles().Single());
        Console.WriteLine($"{result.Entries} entries, {result.UncompressedBytes:N0} bytes uncompressed");
        foreach (var problem in result.Problems)
        {
            Console.Error.WriteLine("  " + problem);
        }
        Console.WriteLine(result.IsValid ? "Archive OK" : "Archive has problems");
        return result.IsValid ? ExitOk : ExitFailed;
    }

    private static int Tools(IServiceProvider sp)
    {
        var locator = sp.GetRequiredService<IToolLocator>();
        Console.WriteLine($"  {"ImageMagick",-12} bundled    {ImageMagickConverter.EngineVersion}");
        Console.WriteLine($"  {"PDF tools",-12} bundled    PDFsharp + PDFium");
        foreach (var tool in ToolCatalog.All)
        {
            var path = locator.Find(tool.Tool);
            Console.WriteLine($"  {tool.DisplayName,-12} {(path is null ? "missing  " : "found    ")}  {path ?? tool.Homepage}");
        }
        return ExitOk;
    }

    private static async Task<int> InstallToolAsync(IServiceProvider sp, CliOptions o)
    {
        var name = o.Files.SingleOrDefault() ?? throw new CliUsageException("install-tool requires a tool name: ffmpeg, pandoc or 7zip.");
        var package = name.ToLowerInvariant() switch
        {
            "ffmpeg" => ToolPackages.FFmpeg,
            "pandoc" => ToolPackages.Pandoc,
            "7zip" or "7z" or "7-zip" => ToolPackages.SevenZip,
            _ => throw new CliUsageException($"Unknown tool '{name}'. Choose ffmpeg, pandoc or 7zip."),
        };
        var installer = sp.GetRequiredService<DependencyInstaller>();
        if (installer.IsInstalledByOrbitConvert(package) && !o.Has("--force"))
        {
            Console.WriteLine($"{package.Name} is already installed in {installer.InstallDirectory(package)} (use --force to reinstall).");
            return ExitOk;
        }
        Console.WriteLine($"{package.Name} {package.Version}");
        Console.WriteLine($"  Source:  {package.DownloadUrl}");
        Console.WriteLine($"  License: {package.License}");
        Console.WriteLine($"  SHA-256: {package.Sha256}");
        if (!o.Has("--yes"))
        {
            Console.Write("Download and install for OrbitConvert only? [y/N] ");
            if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Canceled.");
                return ExitFailed;
            }
        }
        var lastPct = -1;
        await installer.InstallAsync(package, new Progress<DownloadProgress>(p =>
        {
            if (p.TotalBytes is > 0)
            {
                var pct = (int)(100 * p.BytesReceived / p.TotalBytes.Value);
                if (pct / 10 != lastPct / 10)
                {
                    lastPct = pct;
                    Console.WriteLine($"  {p.Stage} {pct}%");
                }
            }
        }), CancellationToken.None);
        Console.WriteLine($"Installed into {installer.InstallDirectory(package)} (checksum verified).");
        return ExitOk;
    }

    private static int Usage(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("Run 'orbitconvert-cli --help' for usage.");
        return ExitUsage;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            OrbitConvert command line — local, private file conversion.

            Usage:
              orbitconvert-cli detect <files...>               Identify files by content
              orbitconvert-cli targets <files...>              List conversions available for the files
              orbitconvert-cli convert <files...> --to <target> [options]
              orbitconvert-cli pdf-info <file.pdf>             Show PDF metadata
              orbitconvert-cli test-archive <file.zip|.7z>     Verify archive integrity (CRC)
              orbitconvert-cli tools                           Show external tool status
              orbitconvert-cli install-tool <ffmpeg|pandoc|7zip> [--yes] [--force]

            Output options:
              --out <folder>        Destination folder (default: beside each original)
              --overwrite | --skip  Collision handling (default: keep both, "name (1).ext")
              --jobs <n>            Parallel conversions
              --details             Print tool diagnostics for failures

            Image:    --quality <1-100> --max-width <px> --max-height <px> --scale <percent>
                      --rotate <90|180|270> --crop <x,y,w,h> --strip-metadata --srgb
                      --background <#RRGGBB> --no-animation
            Video:    --resolution <2160|1440|1080|720|480|360> --video-quality <high|balanced|small>
                      --codec <auto|h264|h265|vp9|av1> --fps <n> --no-audio --gif-fps <n> --gif-width <px>
            Audio:    --bitrate <kbps> --sample-rate <hz> --channels <1|2>
            PDF:      --pages <range> --degrees <90|180|270> --dpi <n> --compression <lossless|balanced|strong>
            Document: --allow-local-resources
            Archive:  --name <archive name> --level <fastest|optimal|smallest>

            Examples:
              orbitconvert-cli convert photo.heic --to jpeg --quality 85 --strip-metadata
              orbitconvert-cli convert clip.mov --to mp4 --resolution 720
              orbitconvert-cli convert a.pdf b.pdf --to pdf-merge
              orbitconvert-cli convert notes.md --to docx
            """);
    }
}

internal sealed class CliUsageException(string message) : Exception(message);

/// <summary>Minimal argument parser: positional files plus "--name value" and "--flag" options.</summary>
internal sealed class CliOptions
{
    private static readonly HashSet<string> Flags =
    [
        "--overwrite", "--skip", "--details", "--yes", "--force", "--strip-metadata", "--srgb", "--no-animation", "--no-audio", "--allow-local-resources",
    ];

    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Files { get; } = [];

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--")
            {
                o.Files.AddRange(args.Skip(i + 1));
                break;
            }
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                if (Flags.Contains(a))
                {
                    o._flags.Add(a);
                }
                else if (i + 1 < args.Length)
                {
                    o._values[a] = args[++i];
                }
                else
                {
                    throw new CliUsageException($"Option {a} needs a value.");
                }
            }
            else
            {
                o.Files.Add(a);
            }
        }
        return o;
    }

    public bool Has(string flag) => _flags.Contains(flag);

    public string? Get(string name) => _values.GetValueOrDefault(name);

    public int? GetInt(string name) =>
        Get(name) is { } v ? int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : throw new CliUsageException($"{name} must be a number.") : null;

    public IReadOnlyList<string> RequireFiles()
    {
        if (Files.Count == 0)
        {
            throw new CliUsageException("No input files given.");
        }
        foreach (var f in Files.Where(f => !File.Exists(f)))
        {
            throw new CliUsageException($"File not found: {f}");
        }
        return Files;
    }

    public void ApplyTo(ConversionOptions c)
    {
        if (GetInt("--quality") is { } q) c.Image.Quality = q;
        if (GetInt("--max-width") is { } mw) c.Image.MaxWidth = mw;
        if (GetInt("--max-height") is { } mh) c.Image.MaxHeight = mh;
        if (GetInt("--scale") is { } sc) c.Image.ScalePercent = sc;
        if (GetInt("--rotate") is { } r) c.Image.Rotation = r;
        if (Get("--crop") is { } crop)
        {
            var parts = crop.Split(',').Select(p => int.TryParse(p, out var n) ? n : throw new CliUsageException("--crop expects x,y,width,height")).ToArray();
            if (parts.Length != 4) throw new CliUsageException("--crop expects x,y,width,height");
            (c.Image.CropX, c.Image.CropY, c.Image.CropWidth, c.Image.CropHeight) = (parts[0], parts[1], parts[2], parts[3]);
        }
        if (Has("--strip-metadata")) c.Image.StripMetadata = true;
        if (Has("--srgb")) c.Image.ConvertToSrgb = true;
        if (Get("--background") is { } bg) c.Image.BackgroundColor = bg;
        if (Has("--no-animation")) c.Image.KeepAnimation = false;

        if (GetInt("--resolution") is { } res)
        {
            c.Video.Resolution = res switch
            {
                2160 => VideoResolution.P2160, 1440 => VideoResolution.P1440, 1080 => VideoResolution.P1080,
                720 => VideoResolution.P720, 480 => VideoResolution.P480, 360 => VideoResolution.P360,
                _ => throw new CliUsageException("--resolution must be 2160, 1440, 1080, 720, 480 or 360."),
            };
        }
        if (Get("--video-quality") is { } vq) c.Video.Quality = ParseEnum<VideoQuality>(vq, "--video-quality");
        if (Get("--codec") is { } codec) c.Video.Codec = ParseEnum<VideoCodec>(codec, "--codec");
        if (GetInt("--fps") is { } fps) c.Video.FrameRate = fps;
        if (Has("--no-audio")) c.Video.RemoveAudio = true;
        if (GetInt("--gif-fps") is { } gf) c.Video.GifFps = gf;
        if (GetInt("--gif-width") is { } gw) c.Video.GifMaxWidth = gw;

        if (GetInt("--bitrate") is { } br)
        {
            c.Audio.BitrateKbps = br;
            c.Video.AudioBitrateKbps = br;
        }
        if (GetInt("--sample-rate") is { } sr) c.Audio.SampleRate = sr;
        if (GetInt("--channels") is { } ch) c.Audio.Channels = ch;

        if (Get("--pages") is { } pages) c.Pdf.PageRange = pages;
        if (GetInt("--degrees") is { } deg) c.Pdf.RotationDegrees = deg;
        if (GetInt("--dpi") is { } dpi) c.Pdf.ImageDpi = dpi;
        if (Get("--compression") is { } comp) c.Pdf.Compression = ParseEnum<PdfCompressionLevel>(comp, "--compression");

        if (Has("--allow-local-resources")) c.Document.AllowLocalResources = true;
        if (Get("--name") is { } name) c.Archive.ArchiveName = name;
        if (Get("--level") is { } level) c.Archive.Compression = ParseEnum<ArchiveCompression>(level, "--level");
    }

    private static T ParseEnum<T>(string value, string option) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var result) && Enum.IsDefined(result)
            ? result
            : throw new CliUsageException($"{option} must be one of: {string.Join(", ", Enum.GetNames<T>()).ToLowerInvariant()}.");
}
