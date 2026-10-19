using ImageMagick;
using OrbitConvert.Converters.Images;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Images;

namespace OrbitConvert.Converters.Tests;

public sealed class ImageEditorTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ImageEditor _editor = new();

    public void Dispose() => _dir.Dispose();

    private string MakeImage()
    {
        var path = _dir.Combine("source.png");
        using var image = new MagickImage(MagickColors.Transparent, 320, 180);
        image.Draw(new ImageMagick.Drawing.Drawables().FillColor(MagickColors.OrangeRed).Rectangle(40, 30, 279, 149));
        image.Write(path, MagickFormat.Png);
        return path;
    }

    [Fact]
    public async Task Crop_writes_real_pixels_to_a_new_file_without_touching_source()
    {
        var input = MakeImage();
        var original = File.ReadAllBytes(input);

        var output = await _editor.CropAsync(input, new CropEditRequest(40, 30, 120, 90), CancellationToken.None);

        Assert.NotEqual(input, output);
        Assert.True(File.Exists(output));
        Assert.Equal(original, File.ReadAllBytes(input));
        using var cropped = new MagickImage(output);
        Assert.Equal(120u, cropped.Width);
        Assert.Equal(90u, cropped.Height);
    }

    [Fact]
    public async Task Background_composites_gradient_padding_corners_and_shadow_into_png()
    {
        var input = MakeImage();
        var request = new BackgroundEditRequest("#2563EB", "#9333EA", true, 60, 28, true, 18, 40, 520, 360);

        var output = await _editor.AddBackgroundAsync(input, request, CancellationToken.None);

        Assert.EndsWith(".png", output, StringComparison.OrdinalIgnoreCase);
        using var result = new MagickImage(output);
        Assert.Equal(520u, result.Width);
        Assert.Equal(360u, result.Height);
        Assert.True(result.GetPixels().GetPixel(0, 0).ToColor()!.B > 80);
    }

    [Fact]
    public async Task Outputs_use_the_identity_suffixes_and_gradients_run_diagonally()
    {
        var input = MakeImage();

        var crop = await _editor.CropAsync(input, new CropEditRequest(0, 0, 50, 50), CancellationToken.None);
        var bg = await _editor.AddBackgroundAsync(input,
            new BackgroundEditRequest("#000000", "#FFFFFF", true, 200, 0, false, 0, 0, 600, 600), CancellationToken.None);

        Assert.EndsWith("source-crop.png", crop);
        Assert.EndsWith("source-bg.png", bg);
        using var result = new MagickImage(bg);
        var pixels = result.GetPixels();
        byte Gray(int x, int y) => pixels.GetPixel(x, y).ToColor()!.R;
        Assert.True(Gray(2, 2) < 40, "top-left starts dark");
        Assert.True(Gray(597, 597) > 215, "bottom-right ends light");
        Assert.InRange(Math.Abs(Gray(597, 2) - Gray(2, 597)), 0, 6); // on a square canvas a 135° ramp is symmetric
    }

    [Fact]
    public async Task Corner_radius_really_rounds_the_image_corners()
    {
        var path = _dir.Combine("solid.png");
        using (var solid = new MagickImage(MagickColors.Red, 200, 200))
        {
            solid.Write(path, MagickFormat.Png);
        }

        // White background, 100 px padding, 40 px radius, no shadow.
        var output = await _editor.AddBackgroundAsync(path,
            new BackgroundEditRequest("#FFFFFF", "#FFFFFF", false, 100, 40, false, 0, 0, 400, 400), CancellationToken.None);

        using var result = new MagickImage(output);
        var pixels = result.GetPixels();
        var corner = pixels.GetPixel(101, 101).ToColor()!;   // inside the square, outside the rounded corner
        var center = pixels.GetPixel(200, 200).ToColor()!;
        Assert.True(corner.R > 240 && corner.G > 240, $"corner should show the background, was {corner}");
        Assert.True(center.R > 240 && center.G < 20, $"center should show the image, was {center}");
    }

    [Fact]
    public async Task Editors_keep_both_when_the_expected_output_name_exists()
    {
        var input = MakeImage();
        var first = await _editor.CropAsync(input, new CropEditRequest(0, 0, 100, 100), CancellationToken.None);
        var second = await _editor.CropAsync(input, new CropEditRequest(0, 0, 80, 80), CancellationToken.None);

        Assert.NotEqual(first, second);
        Assert.Contains("(1)", Path.GetFileName(second));
    }

    [Fact]
    public async Task Rejects_crop_outside_the_source()
    {
        var input = MakeImage();
        await Assert.ThrowsAsync<ConversionException>(() =>
            _editor.CropAsync(input, new CropEditRequest(300, 170, 50, 50), CancellationToken.None));
    }
}
