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

    public Task<string> EditAsync(string inputPath, ImageEditRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => Edit(inputPath, request, cancellationToken), cancellationToken);

    public Task<byte[]> PreviewAsync(string inputPath, ImageEditRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => Preview(inputPath, request, cancellationToken), cancellationToken);

    public IReadOnlyList<ImageMetadataEntry> ReadMetadata(string inputPath)
    {
        EnsureReadable(inputPath);
        using var image = new MagickImage(inputPath);
        var profile = image.GetExifProfile();
        if (profile is null) return [];
        return profile.Values.Select(value =>
        {
            var raw = value.GetValue();
            var text = raw switch
            {
                Array array => string.Join(", ", array.Cast<object>()),
                null => "",
                _ => raw.ToString() ?? "",
            };
            var key = value.Tag.ToString();
            return new ImageMetadataEntry(key, text, key.StartsWith("GPS", StringComparison.OrdinalIgnoreCase));
        }).OrderByDescending(x => x.IsGps).ThenBy(x => x.Key).ToList();
    }

    public Task<string> RemoveMetadataAsync(string inputPath, IReadOnlySet<string> keys, bool removeAll, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            EnsureReadable(inputPath);
            using var image = new MagickImage(inputPath);
            cancellationToken.ThrowIfCancellationRequested();
            if (removeAll)
            {
                image.Strip();
            }
            else
            {
                var profile = image.GetExifProfile();
                if (profile is not null)
                {
                    var tags = profile.Values.Where(value => keys.Contains(value.Tag.ToString())).Select(value => value.Tag).ToList();
                    foreach (var tag in tags)
                    {
                        profile.RemoveValue(tag);
                    }
                    image.SetProfile(profile);
                }
            }
            var extension = SafeOutputExtension(inputPath);
            var destination = NewOutputPath(inputPath, "metadata-clean", extension);
            WriteAtomically(image, destination, image.Format);
            return destination;
        }, cancellationToken);

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

    private static string Edit(string inputPath, ImageEditRequest request, CancellationToken ct)
    {
        EnsureReadable(inputPath);
        using var image = new MagickImage(inputPath);
        ApplyEdits(image, request, ct);
        image.Strip();
        var destination = NewOutputPath(inputPath, "edit", ".png");
        WriteAtomically(image, destination, MagickFormat.Png);
        return destination;
    }

    private static byte[] Preview(string inputPath, ImageEditRequest request, CancellationToken ct)
    {
        EnsureReadable(inputPath);
        using var image = new MagickImage(inputPath);
        ApplyEdits(image, request, ct);
        if (image.Width > 1400 || image.Height > 1000)
        {
            image.Resize(new MagickGeometry(1400, 1000) { Greater = true });
        }
        image.Strip();
        return image.ToByteArray(MagickFormat.Png);
    }

    private static void ApplyEdits(MagickImage image, ImageEditRequest request, CancellationToken ct)
    {
        if (request.RotationDegrees % 90 != 0)
        {
            throw new ConversionException("Rotation must be a multiple of 90 degrees.");
        }
        foreach (var shape in request.Annotations)
        {
            ValidateNormalized(shape.X1, shape.Y1, shape.X2, shape.Y2, "annotation");
        }
        foreach (var area in request.Redactions)
        {
            ValidateNormalized(area.X, area.Y, area.X + area.Width, area.Y + area.Height, "redaction");
        }

        image.AutoOrient();
        ct.ThrowIfCancellationRequested();

        var width = request.Width is > 0 ? request.Width : null;
        var height = request.Height is > 0 ? request.Height : null;
        if (width is not null || height is not null)
        {
            var geometry = new MagickGeometry((uint)(width ?? checked((int)image.Width)), (uint)(height ?? checked((int)image.Height)))
            {
                IgnoreAspectRatio = width is not null && height is not null,
            };
            image.Resize(geometry);
        }
        if (request.RotationDegrees % 360 != 0)
        {
            image.BackgroundColor = MagickColors.Transparent;
            image.Rotate(request.RotationDegrees % 360);
            image.ResetPage();
        }
        if (request.Flip == ImageFlip.Horizontal)
        {
            image.Flop();
        }
        else if (request.Flip == ImageFlip.Vertical)
        {
            image.Flip();
        }
        if (request.Brightness != 0 || request.Contrast != 0)
        {
            image.BrightnessContrast(new Percentage(Math.Clamp(request.Brightness, -100, 100)),
                new Percentage(Math.Clamp(request.Contrast, -100, 100)));
        }

        foreach (var annotation in request.Annotations)
        {
            ct.ThrowIfCancellationRequested();
            DrawAnnotation(image, annotation);
        }
        foreach (var area in request.Redactions)
        {
            ct.ThrowIfCancellationRequested();
            var x1 = area.X * image.Width;
            var y1 = area.Y * image.Height;
            var x2 = (area.X + area.Width) * image.Width;
            var y2 = (area.Y + area.Height) * image.Height;
            new Drawables().FillColor(ParseColor(area.Color, "redaction color"))
                .StrokeColor(ParseColor(area.Color, "redaction color"))
                .Rectangle(x1, y1, x2, y2).Draw(image);
        }

    }

    private static void DrawAnnotation(MagickImage image, ImageAnnotation annotation)
    {
        var color = ParseColor(annotation.Color, "annotation color");
        var x1 = annotation.X1 * image.Width;
        var y1 = annotation.Y1 * image.Height;
        var x2 = annotation.X2 * image.Width;
        var y2 = annotation.Y2 * image.Height;
        var draw = new Drawables().StrokeColor(color).StrokeWidth(Math.Clamp(annotation.StrokeWidth, 1, 100));
        switch (annotation.Kind)
        {
            case ImageAnnotationKind.Text:
                if (string.IsNullOrWhiteSpace(annotation.Text)) return;
                draw.FillColor(color).FontPointSize(Math.Clamp(annotation.FontSize, 8, 300)).Text(x1, y1, annotation.Text).Draw(image);
                break;
            case ImageAnnotationKind.Arrow:
                draw.FillColor(MagickColors.Transparent).Line(x1, y1, x2, y2).Draw(image);
                var angle = Math.Atan2(y2 - y1, x2 - x1);
                var size = Math.Max(10, annotation.StrokeWidth * 4);
                new Drawables().StrokeColor(color).StrokeWidth(annotation.StrokeWidth)
                    .Line(x2, y2, x2 - size * Math.Cos(angle - .55), y2 - size * Math.Sin(angle - .55))
                    .Line(x2, y2, x2 - size * Math.Cos(angle + .55), y2 - size * Math.Sin(angle + .55)).Draw(image);
                break;
            case ImageAnnotationKind.Rectangle:
                draw.FillColor(MagickColors.Transparent).Rectangle(x1, y1, x2, y2).Draw(image);
                break;
            case ImageAnnotationKind.Ellipse:
                draw.FillColor(MagickColors.Transparent)
                    .Ellipse((x1 + x2) / 2, (y1 + y2) / 2, Math.Abs(x2 - x1) / 2, Math.Abs(y2 - y1) / 2, 0, 360)
                    .Draw(image);
                break;
        }
    }

    private static void ValidateNormalized(double x1, double y1, double x2, double y2, string label)
    {
        if (!double.IsFinite(x1) || !double.IsFinite(y1) || !double.IsFinite(x2) || !double.IsFinite(y2)
            || x1 < 0 || y1 < 0 || x2 > 1 || y2 > 1 || x2 <= x1 || y2 <= y1)
        {
            throw new ConversionException($"The {label} area must stay inside the image.");
        }
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
