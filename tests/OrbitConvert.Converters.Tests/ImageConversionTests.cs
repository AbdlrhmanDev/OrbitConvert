using ImageMagick;
using ImageMagick.Drawing;
using OrbitConvert.Converters.Images;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;

namespace OrbitConvert.Converters.Tests;

/// <summary>Real conversions through the bundled ImageMagick (no external tools needed).</summary>
public class ImageConversionTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ImageMagickConverter _converter = new();

    public void Dispose() => _dir.Dispose();

    /// <summary>A 400×200 image with a transparent right half and EXIF metadata.</summary>
    private string MakePng(string name = "sample.png")
    {
        using var image = new MagickImage(MagickColors.Transparent, 400, 200);
        image.Draw(new Drawables().FillColor(MagickColors.OrangeRed).Rectangle(0, 0, 199, 199));
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Make, "TestCam");
        image.SetProfile(exif);
        var path = _dir.Combine(name);
        image.Write(path, MagickFormat.Png);
        return path;
    }

    [Theory]
    [InlineData("jpeg")]
    [InlineData("webp")]
    [InlineData("bmp")]
    [InlineData("tiff")]
    [InlineData("gif")]
    [InlineData("avif")]
    [InlineData("ico")]
    [InlineData("pdf")]
    public async Task Converts_png_to_target_with_a_valid_signature(string target)
    {
        var result = (await Run.Convert(_converter, target, [MakePng()])).AssertSucceeded();

        var output = Assert.Single(result.Outputs);
        Assert.Equal(KnownFormats.FindById(target), Run.Detect(output));
    }

    [Fact]
    public void Offers_only_formats_the_bundled_engine_can_write()
    {
        foreach (var route in _converter.Routes)
        {
            var availability = _converter.GetAvailability(route);
            var format = ImageMagickConverter.ToMagick(route.Output!);
            var writable = MagickNET.SupportedFormats.Any(f => f.Format == format && f.SupportsWriting);
            Assert.Equal(writable, availability.IsAvailable);
        }
    }

    [Fact]
    public async Task Resizes_within_a_box_preserving_aspect_ratio_and_never_upscales()
    {
        var input = MakePng();
        var shrink = (await Run.Convert(_converter, "jpeg", [input], new ConversionOptions { Image = { MaxWidth = 100 } })).AssertSucceeded();
        using (var img = new MagickImage(shrink.Outputs[0]))
        {
            Assert.Equal(100u, img.Width);
            Assert.Equal(50u, img.Height);
        }

        var noUpscale = (await Run.Convert(_converter, "webp", [input], new ConversionOptions { Image = { MaxWidth = 4000, MaxHeight = 4000 } })).AssertSucceeded();
        using (var img = new MagickImage(noUpscale.Outputs[0]))
        {
            Assert.Equal(400u, img.Width);
        }
    }

    [Fact]
    public async Task Crops_then_rotates()
    {
        var options = new ConversionOptions { Image = { CropX = 0, CropY = 0, CropWidth = 200, CropHeight = 100, Rotation = 90 } };
        var result = (await Run.Convert(_converter, "png", [Path.ChangeExtension(MakePng(), null) is var p ? ToJpeg(p) : ""], options)).AssertSucceeded();

        using var img = new MagickImage(result.Outputs[0]);
        Assert.Equal(100u, img.Width);
        Assert.Equal(200u, img.Height);
    }

    [Fact]
    public async Task Flattens_transparency_onto_the_background_for_jpeg()
    {
        var result = (await Run.Convert(_converter, "jpeg", [MakePng()], new ConversionOptions { Image = { BackgroundColor = "#0000FF" } })).AssertSucceeded();

        using var img = new MagickImage(result.Outputs[0]);
        var pixel = img.GetPixels().GetPixel(350, 100).ToColor()!;
        Assert.True(pixel.B > 200 && pixel.R < 60, $"Expected blue background, got {pixel}");
    }

    [Fact]
    public async Task Strips_metadata_on_request()
    {
        var input = MakePng();
        var kept = (await Run.Convert(_converter, "jpeg", [input])).AssertSucceeded();
        var stripped = (await Run.Convert(_converter, "webp", [input], new ConversionOptions { Image = { StripMetadata = true } })).AssertSucceeded();

        using (var img = new MagickImage(kept.Outputs[0]))
        {
            Assert.Equal("TestCam", img.GetExifProfile()?.GetValue(ExifTag.Make)?.Value);
        }
        using (var img = new MagickImage(stripped.Outputs[0]))
        {
            Assert.Null(img.GetExifProfile());
        }
    }

    [Fact]
    public async Task Quality_setting_changes_jpeg_size()
    {
        using var noisy = new MagickImage("plasma:", 300, 300);
        var input = _dir.Combine("noisy.png");
        noisy.Write(input, MagickFormat.Png);

        var high = (await Run.Convert(_converter, "jpeg", [input], new ConversionOptions { Image = { Quality = 95 } }, _dir.Combine("hi"))).AssertSucceeded();
        var low = (await Run.Convert(_converter, "jpeg", [input], new ConversionOptions { Image = { Quality = 20 } }, _dir.Combine("lo"))).AssertSucceeded();

        Assert.True(new FileInfo(low.Outputs[0]).Length < new FileInfo(high.Outputs[0]).Length);
    }

    [Fact]
    public async Task Keeps_animation_between_gif_and_webp()
    {
        using var frames = new MagickImageCollection();
        foreach (var color in new[] { MagickColors.Red, MagickColors.Green, MagickColors.Blue })
        {
            frames.Add(new MagickImage(color, 40, 40) { AnimationDelay = 10 });
        }
        var gif = _dir.Combine("anim.gif");
        frames.Write(gif, MagickFormat.Gif);

        var result = (await Run.Convert(_converter, "webp", [gif])).AssertSucceeded();
        using var output = new MagickImageCollection(result.Outputs[0]);
        Assert.Equal(3, output.Count);

        var still = (await Run.Convert(_converter, "png", [gif])).AssertSucceeded();
        using var single = new MagickImageCollection(still.Outputs[0]);
        Assert.Single(single);
    }

    [Fact]
    public async Task Damaged_image_fails_with_a_readable_message()
    {
        var path = _dir.Combine("broken.png");
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 1, 2, 3]);

        var result = await Run.Convert(_converter, "jpeg", [path]);

        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Empty(Directory.EnumerateFiles(_dir.Path, "broken.jp*"));
    }

    private string ToJpeg(string pathWithoutExtension)
    {
        using var image = new MagickImage(pathWithoutExtension + ".png");
        var jpeg = pathWithoutExtension + ".jpg";
        image.Write(jpeg, MagickFormat.Jpeg);
        return jpeg;
    }
}
