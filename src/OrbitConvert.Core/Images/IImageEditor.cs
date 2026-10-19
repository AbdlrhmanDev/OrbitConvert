namespace OrbitConvert.Core.Images;

public readonly record struct ImageDimensions(int Width, int Height);

public sealed record CropEditRequest(int X, int Y, int Width, int Height);

public sealed record BackgroundEditRequest(
    string Color1,
    string Color2,
    bool UseGradient,
    int Padding,
    int CornerRadius,
    bool DropShadow,
    double ShadowBlur,
    int ShadowOpacity,
    int? OutputWidth = null,
    int? OutputHeight = null);

public enum ImageFlip { None, Horizontal, Vertical }

public enum ImageAnnotationKind { Text, Arrow, Rectangle, Ellipse }

public sealed record ImageAnnotation(
    ImageAnnotationKind Kind,
    double X1,
    double Y1,
    double X2,
    double Y2,
    string Color = "#FF3B81",
    double StrokeWidth = 4,
    string? Text = null,
    double FontSize = 32);

/// <summary>Coordinates are normalized to 0..1 so edits survive preview scaling.</summary>
public sealed record ImageRedaction(double X, double Y, double Width, double Height, string Color = "#000000");

public sealed record ImageEditRequest(
    int? Width,
    int? Height,
    int RotationDegrees,
    ImageFlip Flip,
    int Brightness,
    int Contrast,
    IReadOnlyList<ImageAnnotation> Annotations,
    IReadOnlyList<ImageRedaction> Redactions);

public sealed record ImageMetadataEntry(string Key, string Value, bool IsGps);

/// <summary>Real, non-destructive raster editing operations used by the floating editors.</summary>
public interface IImageEditor
{
    ImageDimensions ReadDimensions(string inputPath);
    Task<string> CropAsync(string inputPath, CropEditRequest request, CancellationToken cancellationToken);
    Task<string> AddBackgroundAsync(string inputPath, BackgroundEditRequest request, CancellationToken cancellationToken);
    Task<string> EditAsync(string inputPath, ImageEditRequest request, CancellationToken cancellationToken);
    Task<byte[]> PreviewAsync(string inputPath, ImageEditRequest request, CancellationToken cancellationToken);
    IReadOnlyList<ImageMetadataEntry> ReadMetadata(string inputPath);
    Task<string> RemoveMetadataAsync(string inputPath, IReadOnlySet<string> keys, bool removeAll, CancellationToken cancellationToken);
}
