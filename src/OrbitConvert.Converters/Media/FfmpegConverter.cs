using System.Globalization;
using System.Text.RegularExpressions;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;
using OrbitConvert.Core.Processes;
using OrbitConvert.Core.Tools;

namespace OrbitConvert.Converters.Media;

/// <summary>Video and audio conversion, audio extraction, video-to-GIF and video compression via FFmpeg.</summary>
public sealed partial class FfmpegConverter : IConverter
{
    public const string CompressKey = "compress-video";

    private static readonly FileFormat[] VideoInputs =
        [KnownFormats.Mp4, KnownFormats.Mkv, KnownFormats.Mov, KnownFormats.Avi, KnownFormats.WebM];

    private static readonly FileFormat[] AudioFormats =
        [KnownFormats.Mp3, KnownFormats.Wav, KnownFormats.Flac, KnownFormats.Aac, KnownFormats.Ogg, KnownFormats.M4a, KnownFormats.Opus];

    private readonly IToolLocator _tools;
    private readonly IProcessRunner _runner;
    private readonly FfmpegCapabilities _capabilities;

    public FfmpegConverter(IToolLocator tools, IProcessRunner runner, FfmpegCapabilities capabilities)
    {
        _tools = tools;
        _runner = runner;
        _capabilities = capabilities;
        Routes = BuildRoutes();
    }

    public string Id => "ffmpeg";
    public string DisplayName => "FFmpeg";
    public IReadOnlyList<ConversionRoute> Routes { get; }

    private static List<ConversionRoute> BuildRoutes()
    {
        var routes = new List<ConversionRoute>();
        var videoPriority = 20;
        foreach (var target in VideoInputs)
        {
            routes.Add(new ConversionRoute
            {
                TargetKey = target.Id,
                Label = target.Label,
                Kind = RouteKind.Convert,
                Inputs = VideoInputs.Append(KnownFormats.Gif).Where(f => f != target).Select(f => f.Id).ToHashSet(StringComparer.OrdinalIgnoreCase),
                Output = target,
                Priority = videoPriority++,
                Description = $"Convert to {target.DisplayName}",
            });
        }

        routes.Add(new ConversionRoute
        {
            TargetKey = KnownFormats.Gif.Id,
            Label = "GIF",
            Kind = RouteKind.Convert,
            Inputs = ConversionRoute.Formats(VideoInputs),
            Output = KnownFormats.Gif,
            Priority = 30,
            Description = "Animated GIF from video",
            Limitations = "GIFs are limited to 256 colors and have no sound. Long videos produce very large files.",
        });

        routes.Add(new ConversionRoute
        {
            TargetKey = CompressKey,
            Label = "Compress",
            Kind = RouteKind.Transform,
            Inputs = ConversionRoute.Formats(VideoInputs),
            Output = null,
            Priority = 31,
            Description = "Re-encode to a smaller file in the same format",
        });

        var audioPriority = 40;
        foreach (var target in AudioFormats)
        {
            routes.Add(new ConversionRoute
            {
                TargetKey = target.Id,
                Label = target.Label,
                Kind = RouteKind.Convert,
                Inputs = VideoInputs.Concat(AudioFormats).Where(f => f != target).Select(f => f.Id).ToHashSet(StringComparer.OrdinalIgnoreCase),
                Output = target,
                Priority = audioPriority++,
                Description = $"Convert or extract audio as {target.DisplayName}",
            });
        }
        return routes;
    }

    public RouteAvailability GetAvailability(ConversionRoute route)
    {
        if (_tools.Find(ExternalTool.FFmpeg) is null)
        {
            return RouteAvailability.Missing(ExternalTool.FFmpeg);
        }
        if (route.Output is null)
        {
            return RouteAvailability.Available; // compress: encoder checked per input at run time
        }
        try
        {
            var enc = FfmpegArgumentBuilder.SelectEncoders(route.Output, new VideoOptions(), _capabilities.Has);
            var needsVideo = route.Output.Category == FormatCategory.Video || route.Output == KnownFormats.Gif;
            if ((needsVideo && enc.Video is null) || (route.Output.Category == FormatCategory.Audio && enc.Audio is null))
            {
                return RouteAvailability.Unavailable($"Your FFmpeg build has no encoder for {route.Output.Label}.");
            }
        }
        catch (ConversionException ex)
        {
            return RouteAvailability.Unavailable(ex.Message);
        }
        return RouteAvailability.Available;
    }

    public async Task<IReadOnlyList<string>> ExecuteAsync(ConversionContext context, CancellationToken cancellationToken)
    {
        var ffmpeg = _tools.Find(ExternalTool.FFmpeg) ?? throw new ConversionException("FFmpeg is not installed.");
        var input = context.Input;
        var container = context.Route.Output ?? input.Format ?? throw new ConversionException("Unknown media type.");
        var isCompress = context.Route.TargetKey == CompressKey;
        var output = isCompress ? context.DefaultOutputPath("-compressed") : context.DefaultOutputPath();

        var videoOptions = context.Options.Video;
        if (isCompress && videoOptions.Quality == VideoQuality.High)
        {
            videoOptions = videoOptions.Clone();
            videoOptions.Quality = VideoQuality.Balanced;
        }
        var encoders = FfmpegArgumentBuilder.SelectEncoders(container, videoOptions, _capabilities.Has);
        if ((container.Category == FormatCategory.Video || container == KnownFormats.Gif) && encoders.Video is null)
        {
            throw new ConversionException(videoOptions.Codec == VideoCodec.Auto
                ? $"Your FFmpeg build has no suitable video encoder for {container.Label}."
                : $"Your FFmpeg build does not include a {FfmpegArgumentBuilder.CodecName(videoOptions.Codec)} encoder.");
        }
        if (container.Category == FormatCategory.Audio && encoders.Audio is null)
        {
            throw new ConversionException($"Your FFmpeg build has no encoder for {container.Label}.");
        }

        var effective = context.Options.Clone();
        effective.Video = videoOptions;
        var args = FfmpegArgumentBuilder.Build(input.Path, output, container, encoders, effective);

        var duration = await ProbeDurationAsync(input.Path, cancellationToken).ConfigureAwait(false);
        var stderrDuration = (TimeSpan?)null;
        var result = await _runner.RunAsync(new ProcessSpec(ffmpeg, args)
        {
            WorkingDirectory = context.WorkDirectory,
            OnStandardOutputLine = line =>
            {
                var total = duration ?? stderrDuration;
                if (total is { TotalSeconds: > 0 } && TryParseProgress(line, out var position))
                {
                    context.ReportProgress(position.TotalSeconds / total.Value.TotalSeconds * 0.99);
                }
            },
            OnStandardErrorLine = line =>
            {
                if (stderrDuration is null && TryParseDuration(line, out var d))
                {
                    stderrDuration = d;
                }
            },
        }, cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new ConversionException(DescribeFailure(result.StandardErrorTail, result.ExitCode), technicalDetail: Tail(result.StandardErrorTail));
        }

        if (isCompress)
        {
            var before = input.Length;
            var after = new FileInfo(output).Length;
            if (after >= before)
            {
                throw new ConversionException("Compression did not make this video smaller. Try a lower resolution or the \"Small\" quality preset.");
            }
            context.ResultNote = $"Reduced by {100.0 * (before - after) / before:0}% ({FormatSize(before)} → {FormatSize(after)})";
        }
        return [output];
    }

    private async Task<TimeSpan?> ProbeDurationAsync(string input, CancellationToken ct)
    {
        var ffprobe = _tools.Find(ExternalTool.FFprobe);
        if (ffprobe is null)
        {
            return null;
        }
        var result = await _runner.RunAsync(new ProcessSpec(ffprobe,
        [
            "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1",
            FfmpegArgumentBuilder.FilePath(input),
        ])
        { Timeout = TimeSpan.FromSeconds(30) }, ct).ConfigureAwait(false);

        return result.Succeeded && double.TryParse(result.StandardOutputTail.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    internal static bool TryParseProgress(string line, out TimeSpan position)
    {
        position = default;
        // Both keys report microseconds (out_time_ms is misnamed in FFmpeg).
        if ((line.StartsWith("out_time_us=", StringComparison.Ordinal) || line.StartsWith("out_time_ms=", StringComparison.Ordinal))
            && long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var us) && us >= 0)
        {
            position = TimeSpan.FromMicroseconds(us);
            return true;
        }
        return false;
    }

    internal static bool TryParseDuration(string line, out TimeSpan duration)
    {
        duration = default;
        var m = DurationLine().Match(line);
        return m.Success && TimeSpan.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, out duration) && duration > TimeSpan.Zero;
    }

    internal static string DescribeFailure(string stderr, int exitCode)
    {
        if (stderr.Contains("matches no streams", StringComparison.OrdinalIgnoreCase))
        {
            return stderr.Contains("0:a", StringComparison.Ordinal)
                ? "This file has no audio track to convert."
                : "This file has no video track.";
        }
        return ErrorTranslator.FromToolOutput(stderr) ?? $"FFmpeg could not convert the file (exit code {exitCode}).";
    }

    private static string Tail(string s) => s.Length > 2000 ? s[^2000..] : s;

    internal static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B",
    };

    [GeneratedRegex(@"Duration:\s*(\d+:\d{2}:\d{2}(?:\.\d+)?)")]
    private static partial Regex DurationLine();
}
