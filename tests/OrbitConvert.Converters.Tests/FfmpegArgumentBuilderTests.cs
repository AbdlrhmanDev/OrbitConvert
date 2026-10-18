using OrbitConvert.Converters.Media;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;

namespace OrbitConvert.Converters.Tests;

public class FfmpegArgumentBuilderTests
{
    private static readonly HashSet<string> FullBuild =
        ["libx264", "libx265", "libvpx-vp9", "libaom-av1", "libmp3lame", "libopus", "libvorbis", "aac", "flac", "gif", "mpeg4", "ac3"];

    private static bool Has(string encoder) => FullBuild.Contains(encoder);

    [Fact]
    public void Paths_use_the_file_protocol_so_names_cannot_become_options()
    {
        var args = FfmpegArgumentBuilder.Build(@"C:\in\-y evil.mov", @"C:\out\concat:x.mp4", KnownFormats.Mp4,
            new EncoderSelection("libx264", "aac"), new ConversionOptions());

        var input = args[args.IndexOf("-i") + 1];
        Assert.Equal(@"file:C:\in\-y evil.mov", input);
        Assert.Equal(@"file:C:\out\concat:x.mp4", args[^1]);
        Assert.DoesNotContain(args, a => a == "-y");
        Assert.Contains("-n", args); // never overwrite
        Assert.Contains("-nostdin", args);
    }

    [Theory]
    [InlineData("mp4", "libx264", "aac")]
    [InlineData("mkv", "libx264", "aac")]
    [InlineData("mov", "libx264", "aac")]
    [InlineData("webm", "libvpx-vp9", "libopus")]
    [InlineData("avi", "mpeg4", "libmp3lame")]
    public void Picks_sensible_default_encoders_per_container(string container, string video, string audio)
    {
        var sel = FfmpegArgumentBuilder.SelectEncoders(KnownFormats.FindById(container)!, new VideoOptions(), Has);
        Assert.Equal(video, sel.Video);
        Assert.Equal(audio, sel.Audio);
    }

    [Fact]
    public void Falls_back_when_preferred_encoder_is_missing()
    {
        var sel = FfmpegArgumentBuilder.SelectEncoders(KnownFormats.WebM, new VideoOptions(), e => e is "libvpx-vp9" or "libvorbis");
        Assert.Equal("libvorbis", sel.Audio);

        var noX264 = FfmpegArgumentBuilder.SelectEncoders(KnownFormats.Mp4, new VideoOptions(), e => e is "h264_mf" or "aac");
        Assert.Equal("h264_mf", noX264.Video);
    }

    [Fact]
    public void Explicit_codec_never_silently_falls_back_to_a_different_codec()
    {
        var sel = FfmpegArgumentBuilder.SelectEncoders(KnownFormats.Mp4, new VideoOptions { Codec = VideoCodec.H264 }, e => e is "mpeg4" or "aac");
        Assert.Null(sel.Video);
    }

    [Theory]
    [InlineData("webm", VideoCodec.H264)]
    [InlineData("mov", VideoCodec.VP9)]
    [InlineData("avi", VideoCodec.AV1)]
    public void Rejects_codecs_the_container_cannot_hold(string container, VideoCodec codec)
    {
        var ex = Assert.Throws<ConversionException>(() =>
            FfmpegArgumentBuilder.SelectEncoders(KnownFormats.FindById(container)!, new VideoOptions { Codec = codec }, Has));
        Assert.Contains("cannot be stored", ex.Message);
    }

    [Fact]
    public void Resolution_preset_scales_down_only_and_keeps_even_dimensions()
    {
        var options = new ConversionOptions { Video = { Resolution = VideoResolution.P720 } };
        var args = FfmpegArgumentBuilder.Build("a.mov", "b.mp4", KnownFormats.Mp4, new("libx264", "aac"), options);

        Assert.Contains("scale=-2:'trunc(min(720,ih)/2)*2'", args);
        Assert.Contains("yuv420p", args);
        Assert.Contains("+faststart", args);
    }

    [Theory]
    [InlineData(VideoQuality.High, "18")]
    [InlineData(VideoQuality.Balanced, "23")]
    [InlineData(VideoQuality.Small, "28")]
    public void Quality_maps_to_crf(VideoQuality quality, string crf)
    {
        var args = FfmpegArgumentBuilder.VideoQualityArgs("libx264", quality).ToList();
        Assert.Equal(crf, args[args.IndexOf("-crf") + 1]);
    }

    [Fact]
    public void Remove_audio_and_frame_rate_are_applied()
    {
        var options = new ConversionOptions { Video = { RemoveAudio = true, FrameRate = 30 } };
        var sel = FfmpegArgumentBuilder.SelectEncoders(KnownFormats.Mp4, options.Video, Has);
        var args = FfmpegArgumentBuilder.Build("a.mov", "b.mp4", KnownFormats.Mp4, sel, options);

        Assert.Null(sel.Audio);
        Assert.Contains("-an", args);
        Assert.Equal("30", args[args.IndexOf("-r") + 1]);
    }

    [Fact]
    public void Gif_uses_a_generated_palette()
    {
        var args = FfmpegArgumentBuilder.Build("a.mp4", "b.gif", KnownFormats.Gif, new("gif", null),
            new ConversionOptions { Video = { GifFps = 10, GifMaxWidth = 320 } });
        var filter = args[args.IndexOf("-vf") + 1];

        Assert.Contains("fps=10", filter);
        Assert.Contains("min(320,iw)", filter);
        Assert.Contains("palettegen", filter);
        Assert.Contains("paletteuse", filter);
    }

    [Theory]
    [InlineData("mp3", "libmp3lame", null)]
    [InlineData("aac", "aac", "adts")]
    [InlineData("m4a", "aac", "ipod")]
    [InlineData("ogg", "libvorbis", "ogg")]
    [InlineData("opus", "libopus", null)]
    [InlineData("flac", "flac", null)]
    [InlineData("wav", "pcm_s16le", null)]
    public void Audio_targets_use_the_right_encoder_and_muxer(string target, string encoder, string? muxer)
    {
        var format = KnownFormats.FindById(target)!;
        var sel = FfmpegArgumentBuilder.SelectEncoders(format, new VideoOptions(), Has);
        var args = FfmpegArgumentBuilder.Build("in.mkv", "out" + format.PrimaryExtension, format, sel,
            new ConversionOptions { Audio = { BitrateKbps = 128, SampleRate = 48000, Channels = 1 } });

        Assert.Equal(encoder, sel.Audio);
        Assert.Equal(encoder, args[args.IndexOf("-c:a") + 1]);
        Assert.Contains("-vn", args);
        Assert.Equal("48000", args[args.IndexOf("-ar") + 1]);
        Assert.Equal("1", args[args.IndexOf("-ac") + 1]);
        if (muxer is not null)
        {
            Assert.Equal(muxer, args[args.IndexOf("-f") + 1]);
        }
        // Bitrate only matters for lossy codecs.
        Assert.Equal(target is not ("flac" or "wav"), args.Contains("-b:a"));
    }

    [Fact]
    public void Missing_mp3_encoder_makes_mp3_unavailable()
    {
        Assert.Null(FfmpegArgumentBuilder.AudioEncoder(KnownFormats.Mp3, e => e != "libmp3lame"));
    }

    [Fact]
    public void Parses_encoder_list()
    {
        const string output = """
            Encoders:
             V..... = Video
             ------
             V....D libx264              libx264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10 (codec h264)
             V.S..D mpeg4                MPEG-4 part 2
             A....D libmp3lame           libmp3lame MP3 (MPEG audio layer 3) (codec mp3)
             S..... srt                  SubRip subtitle
            """;
        var set = FfmpegCapabilities.Parse(output);

        Assert.Contains("libx264", set);
        Assert.Contains("mpeg4", set);
        Assert.Contains("libmp3lame", set);
        Assert.DoesNotContain("Video", set);
        Assert.DoesNotContain("=", set);
    }

    [Fact]
    public void Parses_progress_and_duration_lines()
    {
        Assert.True(FfmpegConverter.TryParseProgress("out_time_us=2500000", out var t));
        Assert.Equal(TimeSpan.FromSeconds(2.5), t);
        Assert.False(FfmpegConverter.TryParseProgress("out_time_us=N/A", out _));
        Assert.True(FfmpegConverter.TryParseDuration("  Duration: 00:01:02.50, start: 0.000000, bitrate: 1 kb/s", out var d));
        Assert.Equal(TimeSpan.FromSeconds(62.5), d);
    }

    [Fact]
    public void Describes_common_ffmpeg_failures()
    {
        Assert.Contains("no audio track", FfmpegConverter.DescribeFailure("Stream map '0:a:0' matches no streams.", 1));
        Assert.Contains("damaged", FfmpegConverter.DescribeFailure("moov atom not found", 1));
        Assert.Contains("exit code 42", FfmpegConverter.DescribeFailure("something odd", 42));
    }
}
