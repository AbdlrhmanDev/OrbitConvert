using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using OrbitConvert.Core.Processes;
using OrbitConvert.Core.Tools;

namespace OrbitConvert.Converters.Media;

/// <summary>
/// Discovers which encoders the installed FFmpeg build includes ("ffmpeg -encoders"), so that
/// targets requiring a missing encoder (e.g. MP3 without libmp3lame) are not offered.
/// </summary>
public sealed partial class FfmpegCapabilities(IToolLocator tools, IProcessRunner runner, ILogger<FfmpegCapabilities> logger)
{
    private readonly object _gate = new();
    private string? _probedPath;
    private IReadOnlySet<string> _encoders = new HashSet<string>();

    public IReadOnlySet<string> Encoders
    {
        get
        {
            var ffmpeg = tools.Find(ExternalTool.FFmpeg);
            if (ffmpeg is null)
            {
                return new HashSet<string>();
            }
            lock (_gate)
            {
                if (!string.Equals(_probedPath, ffmpeg, StringComparison.OrdinalIgnoreCase))
                {
                    _encoders = Probe(ffmpeg);
                    _probedPath = ffmpeg;
                }
                return _encoders;
            }
        }
    }

    public bool Has(string encoder) => Encoders.Contains(encoder);

    /// <summary>Returns the first encoder from <paramref name="preference"/> that is available.</summary>
    public string? FirstAvailable(params string[] preference) => preference.FirstOrDefault(Has);

    private IReadOnlySet<string> Probe(string ffmpeg)
    {
        try
        {
            // The full listing exceeds the runner's diagnostic tail, so collect every line.
            var lines = new System.Collections.Concurrent.ConcurrentQueue<string>();
            runner.RunAsync(new ProcessSpec(ffmpeg, ["-hide_banner", "-encoders"])
            {
                Timeout = TimeSpan.FromSeconds(20),
                OnStandardOutputLine = lines.Enqueue,
            }, CancellationToken.None).GetAwaiter().GetResult();
            var set = Parse(string.Join('\n', lines));
            logger.LogInformation("FFmpeg reports {Count} encoders", set.Count);
            return set;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not query FFmpeg encoders");
            return new HashSet<string>();
        }
    }

    internal static HashSet<string> Parse(string output)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in EncoderLine().Matches(output))
        {
            set.Add(m.Groups["name"].Value);
        }
        return set;
    }

    // " V....D libx264   libx264 H.264 ..." — six capability flags then the encoder name.
    [GeneratedRegex(@"^\s[VAS][A-Z.]{5}\s+(?<name>[A-Za-z0-9_\-]+)\s", RegexOptions.Multiline)]
    private static partial Regex EncoderLine();
}
