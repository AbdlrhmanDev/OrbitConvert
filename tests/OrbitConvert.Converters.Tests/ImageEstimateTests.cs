using ImageMagick;
using OrbitConvert.Converters.Images;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;

namespace OrbitConvert.Converters.Tests;

public sealed class ImageEstimateTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private InputFile Photo()
    {
        var path = _dir.Combine("photo.jpg");
        using (var image = new MagickImage("plasma:", 800, 600))
        {
            image.Quality = 95;
            image.Write(path, MagickFormat.Jpeg);
        }
        return new InputFile(path, KnownFormats.Jpeg, new FileInfo(path).Length);
    }

    [Fact]
    public void Reads_dimensions_from_the_header()
    {
        Assert.Equal((800u, 600u), ImageMagickConverter.ReadDimensions(Photo()));
    }

    [Fact]
    public void Estimate_matches_a_real_conversion_and_follows_quality()
    {
        var input = Photo();
        var options = new ConversionOptions();
        options.Image.Quality = 80;

        var estimate = ImageMagickConverter.EstimateOutputSize(input, KnownFormats.WebP, options.Image, CancellationToken.None);
        options.Image.Quality = 20;
        var lower = ImageMagickConverter.EstimateOutputSize(input, KnownFormats.WebP, options.Image, CancellationToken.None);

        Assert.NotNull(estimate);
        Assert.NotNull(lower);
        Assert.True(lower < estimate, $"q20 ({lower}) should be smaller than q80 ({estimate})");

        // The same pipeline written to disk gives the same size.
        using var image = new MagickImage(input.Path);
        image.AutoOrient();
        image.Quality = 80;
        var path = _dir.Combine("check.webp");
        image.Write(path, MagickFormat.WebP);
        Assert.InRange(estimate!.Value, new FileInfo(path).Length * 0.9, new FileInfo(path).Length * 1.1);
    }

    [Fact]
    public void Unreadable_files_give_no_estimate()
    {
        var path = _dir.Combine("broken.jpg");
        File.WriteAllBytes(path, [0xFF, 0xD8, 0xFF, 0x00, 0x01]);
        var input = new InputFile(path, KnownFormats.Jpeg, 5);

        Assert.Null(ImageMagickConverter.EstimateOutputSize(input, KnownFormats.Png, new ImageOptions(), CancellationToken.None));
        Assert.Null(ImageMagickConverter.ReadDimensions(input));
    }
}
