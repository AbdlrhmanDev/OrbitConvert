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

/// <summary>Real, non-destructive raster editing operations used by the floating editors.</summary>
public interface IImageEditor
{
    ImageDimensions ReadDimensions(string inputPath);
    Task<string> CropAsync(string inputPath, CropEditRequest request, CancellationToken cancellationToken);
    Task<string> AddBackgroundAsync(string inputPath, BackgroundEditRequest request, CancellationToken cancellationToken);
}
