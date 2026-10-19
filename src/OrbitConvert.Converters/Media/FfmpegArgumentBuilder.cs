using System.Globalization;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;

namespace OrbitConvert.Converters.Media;

/// <summary>Chosen encoders for a job; produced by <see cref="FfmpegArgumentBuilder.SelectEncoders"/>.</summary>
public sealed record EncoderSelection(string? Video, string? Audio);

/// <summary>
/// Builds FFmpeg argument lists. Pure and deterministic so it can be unit tested without FFmpeg.
/// Paths are prefixed with "file:" so a file name can never be parsed as an option or protocol.
/// </summary>
public static class FfmpegArgumentBuilder
{
    public static bool IsVideoContainer(FileFormat f) => f.Category == FormatCategory.Video;

    /// <summary>Picks encoders for the container, honoring the user's codec choice.</summary>
    public static EncoderSelection SelectEncoders(FileFormat container, VideoOptions options, Func<string, bool> has)
    {
        string? First(params string[] names) => names.FirstOrDefault(has);

        if (container == KnownFormats.Gif)
        {
            return new EncoderSelection(has("gif") ? "gif" : null, null);
        }

        if (container.Category == FormatCategory.Audio)
        {
            return new EncoderSelection(null, AudioEncoder(container, has));
        }

        var codec = options.Codec;
        if (codec == VideoCodec.Auto)
        {
            codec = container == KnownFormats.WebM ? VideoCodec.VP9 : VideoCodec.H264;
        }

        var allowed = container.Id switch
        {
            "webm" => new[] { VideoCodec.VP9, VideoCodec.AV1 },
            "mov" => [VideoCodec.H264, VideoCodec.H265],
            "avi" => [VideoCodec.H264],
            _ => [VideoCodec.H264, VideoCodec.H265, VideoCodec.VP9, VideoCodec.AV1],
        };
        if (options.Codec != VideoCodec.Auto && !allowed.Contains(codec))
        {
            throw new ConversionException($"{CodecName(codec)} video cannot be stored in a {container.Label} file.");
        }

        string? video = codec switch
        {
            VideoCodec.H264 when container == KnownFormats.Avi && options.Codec == VideoCodec.Auto => First("mpeg4"),
            VideoCodec.H264 => First("libx264", "h264_mf", "mpeg4"),
            VideoCodec.H265 => First("libx265", "hevc_mf"),
            VideoCodec.VP9 => First("libvpx-vp9"),
            VideoCodec.AV1 => First("libsvtav1", "libaom-av1"),
            _ => null,
        };
        // An explicit choice must not silently fall back to a different codec.
        if (video == "mpeg4" && options.Codec == VideoCodec.H264)
        {
            video = null;
        }

        string? audio = options.RemoveAudio ? null : container.Id switch
        {
            "webm" => First("libopus", "libvorbis"),
            "avi" => First("libmp3lame", "ac3"),
            _ => First("aac"),
        };
        return new EncoderSelection(video, audio);
    }

    public static string? AudioEncoder(FileFormat container, Func<string, bool> has) => container.Id switch
    {
        "mp3" => has("libmp3lame") ? "libmp3lame" : null,
        "wav" => "pcm_s16le",
        "flac" => has("flac") ? "flac" : null,
        "aac" or "m4a" => has("aac") ? "aac" : null,
        "ogg" => has("libvorbis") ? "libvorbis" : null,
        "opus" => has("libopus") ? "libopus" : null,
        _ => null,
    };

    public static List<string> Build(
        string input, string output, FileFormat container, EncoderSelection encoders,
        ConversionOptions options, bool sourceHasAudio = true)
    {
        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-n",
            "-i", FilePath(input),
        };

        if (container.Category == FormatCategory.Audio)
        {
            AddTrim(args, options.Audio.TrimStart, options.Audio.TrimEnd);
        }
        else
        {
            AddTrim(args, options.Video.TrimStart, options.Video.TrimEnd);
        }

        if (container == KnownFormats.Gif)
        {
            AddGif(args, options.Video);
        }
        else if (container.Category == FormatCategory.Audio)
        {
            AddAudioOnly(args, container, encoders, options.Audio);
        }
        else
        {
            AddVideo(args, container, encoders, options.Video, sourceHasAudio);
        }

        args.AddRange(["-progress", "pipe:1", "-nostats", FilePath(output)]);
        return args;
    }

    private static void AddGif(List<string> args, VideoOptions v)
    {
        var fps = Math.Clamp(v.GifFps, 1, 50);
        var width = Math.Clamp(v.GifMaxWidth, 16, 3840);
        // Two-pass palette in a single graph gives far better colors than the default GIF palette.
        var filter = $"fps={fps},scale='min({width},iw)':-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5";
        args.AddRange(["-map", "0:v:0", "-vf", filter, "-an", "-loop", "0", "-c:v", "gif"]);
    }

    private static void AddAudioOnly(List<string> args, FileFormat container, EncoderSelection enc, AudioOptions a)
    {
        args.AddRange(["-map", "0:a:0", "-vn", "-sn", "-dn", "-c:a", enc.Audio!]);
        if (IsLossy(container))
        {
            args.AddRange(["-b:a", Kbps(a.BitrateKbps)]);
        }
        if (a.SampleRate is > 0)
        {
            args.AddRange(["-ar", a.SampleRate.Value.ToString(CultureInfo.InvariantCulture)]);
        }
        if (a.Channels is 1 or 2)
        {
            args.AddRange(["-ac", a.Channels.Value.ToString(CultureInfo.InvariantCulture)]);
        }
        switch (container.Id)
        {
            case "aac":
                args.AddRange(["-f", "adts"]);
                break;
            case "m4a":
                args.AddRange(["-f", "ipod", "-movflags", "+faststart"]);
                break;
            case "ogg":
                args.AddRange(["-f", "ogg"]);
                break;
        }
    }

    private static void AddVideo(List<string> args, FileFormat container, EncoderSelection enc, VideoOptions v, bool sourceHasAudio)
    {
        args.AddRange(["-map", "0:v:0"]);
        if (enc.Audio is not null && sourceHasAudio)
        {
            args.AddRange(["-map", "0:a:0?"]);
        }
        AddSubtitles(args, container, v);
        args.Add("-dn");

        var height = VideoOptions.ResolutionHeight(v.Resolution);
        // Encoders using 4:2:0 chroma need even dimensions; never upscale.
        var scale = height is { } h
            ? $"scale=-2:'trunc(min({h},ih)/2)*2'"
            : "scale='trunc(iw/2)*2':'trunc(ih/2)*2'";
        args.AddRange(["-vf", scale]);

        if (v.FrameRate is > 0)
        {
            args.AddRange(["-r", v.FrameRate.Value.ToString(CultureInfo.InvariantCulture)]);
        }

        args.AddRange(["-c:v", enc.Video!]);
        args.AddRange(VideoQualityArgs(enc.Video!, v.Quality));
        if (enc.Video != "mpeg4")
        {
            args.AddRange(["-pix_fmt", "yuv420p"]);
        }
        if (enc.Video == "libx265" && container != KnownFormats.Mkv)
        {
            args.AddRange(["-tag:v", "hvc1"]); // plays in Apple and Windows players
        }

        if (enc.Audio is null || !sourceHasAudio)
        {
            args.Add("-an");
        }
        else
        {
            args.AddRange(["-c:a", enc.Audio, "-b:a", Kbps(v.AudioBitrateKbps)]);
        }

        if (container == KnownFormats.Mp4 || container == KnownFormats.Mov)
        {
            args.AddRange(["-movflags", "+faststart"]);
        }
    }

    private static void AddTrim(List<string> args, TimeSpan? start, TimeSpan? end)
    {
        if (start < TimeSpan.Zero)
        {
            throw new ConversionException("Trim start cannot be negative.");
        }
        if (end <= TimeSpan.Zero)
        {
            throw new ConversionException("Trim end must be later than zero.");
        }

        var effectiveStart = start ?? TimeSpan.Zero;
        if (end is { } trimEnd && trimEnd <= effectiveStart)
        {
            throw new ConversionException("Trim end must be later than trim start.");
        }

        if (start is { } trimStart)
        {
            args.AddRange(["-ss", Timestamp(trimStart)]);
        }
        if (end is { } finish)
        {
            args.AddRange(["-t", Timestamp(finish - effectiveStart)]);
        }
    }

    private static void AddSubtitles(List<string> args, FileFormat container, VideoOptions options)
    {
        if (options.Subtitles == SubtitleMode.DropAll)
        {
            args.Add("-sn");
            return;
        }

        if (container != KnownFormats.Mkv)
        {
            throw new ConversionException("Subtitle track copying is currently supported only for MKV output.");
        }

        if (options.Subtitles == SubtitleMode.Select)
        {
            if (options.SubtitleStreamIndex is not >= 0)
            {
                throw new ConversionException("Choose a subtitle track before starting the conversion.");
            }
            args.AddRange(["-map", $"0:s:{options.SubtitleStreamIndex.Value}?", "-c:s", "copy"]);
            return;
        }

        args.AddRange(["-map", "0:s?", "-c:s", "copy"]);
    }

    internal static IEnumerable<string> VideoQualityArgs(string encoder, VideoQuality q)
    {
        int Pick(int high, int balanced, int small) => q switch
        {
            VideoQuality.High => high,
            VideoQuality.Small => small,
            _ => balanced,
        };
        string N(int n) => n.ToString(CultureInfo.InvariantCulture);

        return encoder switch
        {
            "libx264" => ["-preset", "medium", "-crf", N(Pick(18, 23, 28))],
            "libx265" => ["-preset", "medium", "-crf", N(Pick(20, 26, 30))],
            "libvpx-vp9" => ["-crf", N(Pick(24, 32, 40)), "-b:v", "0", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4"],
            "libsvtav1" => ["-preset", "8", "-crf", N(Pick(26, 35, 45))],
            "libaom-av1" => ["-crf", N(Pick(26, 34, 42)), "-b:v", "0", "-cpu-used", "6", "-row-mt", "1"],
            "mpeg4" => ["-q:v", N(Pick(2, 4, 8))],
            "h264_mf" or "hevc_mf" => ["-b:v", Pick(8000, 4000, 1500) + "k"],
            _ => [],
        };
    }

    private static bool IsLossy(FileFormat f) => f.Id is "mp3" or "aac" or "m4a" or "ogg" or "opus";

    private static string Kbps(int kbps) => Math.Clamp(kbps, 8, 512).ToString(CultureInfo.InvariantCulture) + "k";

    private static string Timestamp(TimeSpan value) => value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

    /// <summary>The "file:" protocol prefix stops FFmpeg from interpreting names like "-x" or "concat:...".</summary>
    public static string FilePath(string path) => "file:" + Path.GetFullPath(path);

    public static string CodecName(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => "H.264",
        VideoCodec.H265 => "H.265/HEVC",
        VideoCodec.VP9 => "VP9",
        VideoCodec.AV1 => "AV1",
        _ => "Automatic",
    };
}
