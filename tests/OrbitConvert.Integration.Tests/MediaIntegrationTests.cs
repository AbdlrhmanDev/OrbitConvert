using System.Diagnostics;
using OrbitConvert.Converters.Media;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;
using OrbitConvert.Core.Tools;

namespace OrbitConvert.Integration.Tests;

[Collection("integration")]
public class MediaIntegrationTests(IntegrationFixture fx)
{
    private string Clip(string dir, string name = "clip.mov", int seconds = 2, string size = "640x360") =>
        fx.MakeMedia(dir, name, "-f", "lavfi", "-i", $"testsrc2=size={size}:rate=25", "-f", "lavfi", "-i", "sine=frequency=440",
            "-t", seconds.ToString(), "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest");

    [SkippableFact]
    public async Task Video_to_mp4_with_resolution_preset()
    {
        Skip.IfNot(fx.Has(ExternalTool.FFmpeg), "FFmpeg not installed");
        var dir = fx.NewDir();
        var input = Clip(dir, "clip.mov", size: "1280x720");

        var result = (await fx.ConvertAsync(input, "mp4", new ConversionOptions { Video = { Resolution = VideoResolution.P360 } })).AssertSucceeded();

        var output = Assert.Single(result.Outputs);
        Assert.Equal(KnownFormats.Mp4, fx.Registry.Analyze(output).Format);
        Assert.Equal("360", fx.Probe(output, "v:0", "height"));
        Assert.Equal("h264", fx.Probe(output, "v:0", "codec_name"));
        Assert.Equal("aac", fx.Probe(output, "a:0", "codec_name"));
    }

    [SkippableTheory]
    [InlineData("webm", "vp9")]
    [InlineData("mkv", "h264")]
    [InlineData("avi", "mpeg4")]
    public async Task Video_container_conversions(string target, string expectedCodec)
    {
        Skip.IfNot(fx.Has(ExternalTool.FFmpeg), "FFmpeg not installed");
        var dir = fx.NewDir();

        var result = (await fx.ConvertAsync(Clip(dir), target)).AssertSucceeded();

        Assert.Equal(expectedCodec, fx.Probe(result.Outputs[0], "v:0", "codec_name"));
    }

    [SkippableFact]
    public async Task Video_to_gif_and_audio_extraction()
    {
        Skip.IfNot(fx.Has(ExternalTool.FFmpeg), "FFmpeg not installed");
        var dir = fx.NewDir();
        var clip = Clip(dir);

        var gif = (await fx.ConvertAsync(clip, "gif", new ConversionOptions { Video = { GifMaxWidth = 160 } })).AssertSucceeded();
        Assert.Equal(KnownFormats.Gif, fx.Registry.Analyze(gif.Outputs[0]).Format);
        Assert.Equal("160", fx.Probe(gif.Outputs[0], "v:0", "width"));

        var mp3 = (await fx.ConvertAsync(clip, "mp3")).AssertSucceeded();
        Assert.Equal("mp3", fx.Probe(mp3.Outputs[0], "a:0", "codec_name"));
        Assert.Equal("", fx.Probe(mp3.Outputs[0], "v:0", "codec_name")); // no video stream
    }

    [SkippableTheory]
    [InlineData("mp3", "mp3")]
    [InlineData("flac", "flac")]
    [InlineData("aac", "aac")]
    [InlineData("m4a", "aac")]
    [InlineData("ogg", "vorbis")]
    [InlineData("opus", "opus")]
    public async Task Audio_conversions_with_options(string target, string codec)
    {
        Skip.IfNot(fx.Has(ExternalTool.FFmpeg), "FFmpeg not installed");
        var dir = fx.NewDir();
        var wav = fx.MakeMedia(dir, "tone.wav", "-f", "lavfi", "-i", "sine=frequency=440:duration=2", "-ac", "2");

        var result = (await fx.ConvertAsync(wav, target, new ConversionOptions { Audio = { Channels = 1, BitrateKbps = 96 } })).AssertSucceeded();

        Assert.Equal(codec, fx.Probe(result.Outputs[0], "a:0", "codec_name"));
        Assert.Equal("1", fx.Probe(result.Outputs[0], "a:0", "channels"));
    }

    [SkippableFact]
    public async Task Video_without_audio_track_reports_it_when_extracting_audio()
    {
        Skip.IfNot(fx.Has(ExternalTool.FFmpeg), "FFmpeg not installed");
        var dir = fx.NewDir();
        var silent = fx.MakeMedia(dir, "silent.mp4", "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=10", "-t", "1", "-c:v", "libx264", "-pix_fmt", "yuv420p");

        var result = await fx.ConvertAsync(silent, "mp3");

        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Contains("no audio track", result.Error);
    }

    [SkippableFact]
    public async Task Corrupted_media_fails_cleanly()
    {
        Skip.IfNot(fx.Has(ExternalTool.FFmpeg), "FFmpeg not installed");
        var dir = fx.NewDir();
        var clip = Clip(dir);
        var bytes = File.ReadAllBytes(clip);
        var broken = Path.Combine(dir, "broken.mov");
        File.WriteAllBytes(broken, bytes[..(bytes.Length / 3)]); // truncated: index (moov) is lost

        var result = await fx.ConvertAsync(broken, "mp4");

        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Empty(Directory.EnumerateFiles(dir, "broken*.mp4"));
    }

    [SkippableFact]
    public async Task Compress_video_reports_the_saving()
    {
        Skip.IfNot(fx.Has(ExternalTool.FFmpeg), "FFmpeg not installed");
        var dir = fx.NewDir();
        var big = fx.MakeMedia(dir, "big.mp4", "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30", "-t", "3",
            "-c:v", "libx264", "-crf", "8", "-pix_fmt", "yuv420p");

        var result = (await fx.ConvertAsync(big, FfmpegConverter.CompressKey,
            new ConversionOptions { Video = { Quality = VideoQuality.Small, Resolution = VideoResolution.P480 } })).AssertSucceeded();

        Assert.EndsWith("big-compressed.mp4", result.Outputs[0]);
        Assert.StartsWith("Reduced by", result.Note);
    }

    [SkippableFact]
    public async Task Canceling_a_running_conversion_kills_ffmpeg_and_leaves_no_output()
    {
        Skip.IfNot(fx.Has(ExternalTool.FFmpeg), "FFmpeg not installed");
        var dir = fx.NewDir();
        var input = Clip(dir, "long.mp4", seconds: 20, size: "1280x720");
        var before = FfmpegProcessIds();
        using var cts = new CancellationTokenSource();

        // AV1 encoding is slow enough to still be running when we cancel.
        var run = fx.ConvertAsync(input, "webm", new ConversionOptions { Video = { Codec = VideoCodec.AV1 } }, cts.Token);
        for (var i = 0; i < 200 && !FfmpegProcessIds().Except(before).Any(); i++)
        {
            await Task.Delay(50);
        }
        Assert.NotEmpty(FfmpegProcessIds().Except(before)); // the encode is really running
        await Task.Delay(500);
        cts.Cancel();
        var result = await run;

        Assert.Equal(ConversionStatus.Canceled, result.Status);
        Assert.Empty(Directory.EnumerateFiles(dir, "long*.webm"));
        await Task.Delay(500);
        Assert.Empty(FfmpegProcessIds().Except(before));
    }

    private static HashSet<int> FfmpegProcessIds() => Process.GetProcessesByName("ffmpeg").Select(p => p.Id).ToHashSet();
}
