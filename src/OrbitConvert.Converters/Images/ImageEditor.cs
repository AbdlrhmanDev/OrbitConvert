using ImageMagick;
using ImageMagick.Drawing;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Images;

namespace OrbitConvert.Converters.Images;

/// <summary>ImageMagick-backed editor. All writes are committed atomically to a new file.</summary>
public sealed class ImageEditor : IImageEditor
{
    public ImageDimensions ReadDimensions(string inputPath)
    {
        EnsureReadable(inputPath);
        var info = new MagickImageInfo(inputPath);
        return new ImageDimensions(checked((int)info.Width), checked((int)info.Height));
    }

    public Task<string> CropAsync(string inputPath, CropEditRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => Crop(inputPath, request, cancellationToken), cancellationToken);

    public Task<string> AddBackgroundAsync(string inputPath, BackgroundEditRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => AddBackground(inputPath, request, cancellationToken), cancellationToken);

    private static string Crop(string inputPath, CropEditRequest request, CancellationToken ct)
    {
        EnsureReadable(inputPath);
        if (request.Width <= 0 || request.Height <= 0 || request.X < 0 || request.Y < 0)
        {
            throw new ConversionException("Choose a valid crop area.");
        }

        using var image = new MagickImage(inputPath);
        image.AutoOrient();
        if (request.X >= image.Width || request.Y >= image.Height
            || request.X + request.Width > image.Width || request.Y + request.Height > image.Height)
        {
            throw new ConversionException($"The crop area must stay inside the {image.Width}×{image.Height} image.");
        }

        ct.ThrowIfCancellationRequested();
        image.Crop(new MagickGeometry(request.X, request.Y, (uint)request.Width, (uint)request.Height));
        image.ResetPage();
        image.Strip();

        var extension = SafeOutputExtension(inputPath);
        var destination = NewOutputPath(inputPath, "crop", extension);
        WriteAtomically(image, destination, image.Format);
        return destination;
    }

    private static string AddBackground(string inputPath, BackgroundEditRequest request, CancellationToken ct)
    {
        EnsureReadable(inputPath);
        var color1 = ParseColor(request.Color1, "first background color");
        var color2 = ParseColor(request.Color2, "second background color");
        var padding = Math.Clamp(request.Padding, 0, 1000);
        var radius = Math.Clamp(request.CornerRadius, 0, 1000);
        var blur = Math.Clamp(request.ShadowBlur, 0, 100);
        var opacity = Math.Clamp(request.ShadowOpacity, 0, 100);

        using var image = new MagickImage(inputPath);
        image.AutoOrient();
        image.Alpha(AlphaOption.On);

        var requestedWidth = request.OutputWidth is > 0 ? request.OutputWidth.Value : checked((int)image.Width + padding * 2);
        var requestedHeight = request.OutputHeight is > 0 ? request.OutputHeight.Value : checked((int)image.Height + padding * 2);
        var canvasWidth = Math.Clamp(requestedWidth, 1, 60_000);
        var canvasHeight = Math.Clamp(requestedHeight, 1, 60_000);
        var availableWidth = Math.Max(1, canvasWidth - padding * 2);
        var availableHeight = Math.Max(1, canvasHeight - padding * 2);
        if (image.Width > availableWidth || image.Height > availableHeight)
        {
            image.Resize(new MagickGeometry((uint)availableWidth, (uint)availableHeight) { Greater = true });
        }

        if (radius > 0)
        {
            ApplyRoundedMask(image, Math.Min(radius, checked((int)Math.Min(image.Width, image.Height) / 2)));
        }

        ct.ThrowIfCancellationRequested();
        using var canvas = CreateBackground(canvasWidth, canvasHeight, color1, color2, request.UseGradient);
        var x = (canvasWidth - checked((int)image.Width)) / 2;
        var y = (canvasHeight - checked((int)image.Height)) / 2;

        if (request.DropShadow && opacity > 0 && blur > 0)
        {
            using var shadow = image.Clone();
            shadow.Shadow(0, Math.Max(1, (int)Math.Round(blur / 3)), blur, new Percentage(opacity), MagickColors.Black);
            shadow.ResetPage();
            var shadowX = (canvasWidth - checked((int)shadow.Width)) / 2;
            var shadowY = (canvasHeight - checked((int)shadow.Height)) / 2;
            canvas.Composite(shadow, shadowX, shadowY, CompositeOperator.Over);
        }

        canvas.Composite(image, x, y, CompositeOperator.Over);
        canvas.Strip();
        var destination = NewOutputPath(inputPath, "bg", ".png");
        WriteAtomically(canvas, destination, MagickFormat.Png);
        return destination;
    }

    private static MagickImage CreateBackground(int width, int height, MagickColor color1, MagickColor color2, bool gradient)
    {
        if (!gradient || color1 == color2)
        {
            return new MagickImage(color1, (uint)width, (uint)height);
        }

        // Values are parsed by MagickColor first, so the generated pseudo-image contains only
        // canonical color text and cannot inject an arbitrary ImageMagick coder or path.
        // 135° (top-left to bottom-right), matching the identity's gradient swatches.
        var settings = new MagickReadSettings { Width = (uint)width, Height = (uint)height };
        settings.SetDefine("gradient:angle", "135");
        var image = new MagickImage($"gradient:{color1}-{color2}", settings) { Format = MagickFormat.Png };
        return image;
    }

    private static void ApplyRoundedMask(MagickImage image, int radius)
    {
        // Keep the image only where the rounded rectangle is drawn (Porter-Duff "destination in").
        using var mask = new MagickImage(MagickColors.Transparent, image.Width, image.Height);
        new Drawables()
            .FillColor(MagickColors.White)
            .RoundRectangle(0, 0, image.Width - 1, image.Height - 1, radius, radius)
            .Draw(mask);
        image.Composite(mask, CompositeOperator.DstIn);
    }

    private static MagickColor ParseColor(string value, string label)
    {
        try
        {
            return new MagickColor(value);
        }
        catch (Exception ex) when (ex is ArgumentException or MagickException)
        {
            throw new ConversionException($"The {label} is invalid.", ex);
        }
    }

    private static string SafeOutputExtension(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".tif" or ".tiff" or ".gif" or ".avif" or ".heic"
            ? extension
            : ".png";
    }

    private static string NewOutputPath(string inputPath, string suffix, string extension)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(inputPath))!;
        var fileName = $"{Path.GetFileNameWithoutExtension(inputPath)}-{suffix}{extension}";
        return OutputPlanner.ChooseDestination(directory, fileName, CollisionStrategy.KeepBoth, [Path.GetFullPath(inputPath)])!;
    }

    private static void WriteAtomically(MagickImage image, string destination, MagickFormat format)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.partial");
        try
        {
            image.Write(temporary, format);
            OutputPlanner.Commit(temporary, destination, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void EnsureReadable(string inputPath)
    {
        if (!File.Exists(inputPath))
        {
            throw new ConversionException("The image no longer exists.");
        }
        if (new FileInfo(inputPath).Length > 2L * 1024 * 1024 * 1024)
        {
            throw new ConversionException("The image is too large to edit safely.");
        }
    }
}
