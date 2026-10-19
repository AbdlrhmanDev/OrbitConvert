using ImageMagick;
using OrbitConvert.Converters.Images;
using OrbitConvert.Core.Conversion;

namespace OrbitConvert.Converters.Tests;

public class ImageActionTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ImageMagickConverter _converter = new();

    public void Dispose() => _dir.Dispose();

    private string Photo(string name, MagickFormat format, int quality = 100, bool exif = true, OrientationType orientation = OrientationType.TopLeft)
    {
        using var image = new MagickImage("plasma:", 640, 480);
        if (exif)
        {
            var profile = new ExifProfile();
            profile.SetValue(ExifTag.Make, "TestCam");
            profile.SetValue(ExifTag.GPSLatitudeRef, "N");
            profile.SetValue(ExifTag.Orientation, (ushort)orientation);
            image.SetProfile(profile);
        }
        image.Orientation = orientation; // Magick writes this into the EXIF block on save
        image.Quality = (uint)quality;
        image.SetAttribute("comment", "private note");
        var path = _dir.Combine(name);
        image.Write(path, format);
        return path;
    }

    private static byte[] AfterStartOfScan(byte[] jpeg)
    {
        for (var i = 2; i < jpeg.Length - 1; i++)
        {
            if (jpeg[i] == 0xFF && jpeg[i + 1] == 0xDA)
            {
                return jpeg[i..];
            }
        }
        throw new InvalidOperationException("no SOS");
    }

    [Fact]
    public async Task Shrink_makes_a_high_quality_jpeg_smaller_in_the_same_format()
    {
        var input = Photo("big.jpg", MagickFormat.Jpeg);

        var result = (await Run.Convert(_converter, ImageActions.ShrinkKey, [input])).AssertSucceeded();

        var output = Assert.Single(result.Outputs);
        Assert.EndsWith("big-small.jpg", output);
        Assert.True(new FileInfo(output).Length < new FileInfo(input).Length);
        Assert.StartsWith("Reduced by", result.Note);
        using var shrunk = new MagickImage(output);
        Assert.Equal(640u, shrunk.Width);
        Assert.Null(shrunk.GetExifProfile());
    }

    [Fact]
    public async Task Shrink_fails_honestly_when_it_cannot_help()
    {
        // Already saved at a lower quality than Shrink uses, with no metadata to drop.
        var tiny = Photo("tiny.jpg", MagickFormat.Jpeg, quality: 30, exif: false);

        var result = await Run.Convert(_converter, ImageActions.ShrinkKey, [tiny]);

        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Contains("already compact", result.Error);
        Assert.Empty(Directory.EnumerateFiles(_dir.Path, "tiny-small*"));
    }

    [Fact]
    public async Task Strip_removes_jpeg_metadata_without_touching_image_data()
    {
        var input = Photo("gps.jpg", MagickFormat.Jpeg);

        var result = (await Run.Convert(_converter, ImageActions.StripKey, [input])).AssertSucceeded();

        var output = Assert.Single(result.Outputs);
        Assert.EndsWith("gps-clean.jpg", output);
        Assert.Equal(AfterStartOfScan(File.ReadAllBytes(input)), AfterStartOfScan(File.ReadAllBytes(output)));
        using var clean = new MagickImage(output);
        Assert.Null(clean.GetExifProfile());
        Assert.Contains("pixels unchanged", result.Note);
    }

    [Fact]
    public async Task Strip_keeps_rotated_photos_upright()
    {
        var input = Photo("rotated.jpg", MagickFormat.Jpeg, orientation: OrientationType.RightTop);

        var result = (await Run.Convert(_converter, ImageActions.StripKey, [input])).AssertSucceeded();

        using var clean = new MagickImage(result.Outputs[0]);
        Assert.Equal(480u, clean.Width); // physically rotated, so no orientation flag is needed
        Assert.Null(clean.GetExifProfile());
    }

    [Fact]
    public async Task Strip_removes_png_text_chunks_and_reports_when_nothing_is_left()
    {
        var input = Photo("notes.png", MagickFormat.Png, exif: false);
        Assert.Contains("private note", System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(input)));

        var result = (await Run.Convert(_converter, ImageActions.StripKey, [input])).AssertSucceeded();
        var output = result.Outputs[0];
        Assert.DoesNotContain("private note", System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(output)));

        using (var a = new MagickImage(input))
        using (var b = new MagickImage(output))
        {
            Assert.Equal(0.0, a.Compare(b, ErrorMetric.Absolute));
        }

        var again = await Run.Convert(_converter, ImageActions.StripKey, [output]);
        Assert.Equal(ConversionStatus.Failed, again.Status);
        Assert.Contains("no metadata", again.Error);
    }

    [Fact]
    public void Damaged_jpeg_structure_is_rejected()
    {
        Assert.Throws<ConversionException>(() => ImageActions.StripJpeg([0xFF, 0xD8, 0x00, 0x00, 0x01]));
    }
}
