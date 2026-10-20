using ImageMagick;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;

namespace OrbitConvert.Converters.Images;

/// <summary>
/// Raster image conversion through Magick.NET (bundled ImageMagick). The set of offered formats is
/// taken from the coders actually compiled into the bundled native library, so a format that cannot
/// be read or written is never offered.
/// </summary>
public sealed class ImageMagickConverter : IConverter
{
    internal static readonly FileFormat[] RasterInputs =
    [
        KnownFormats.Jpeg, KnownFormats.Png, KnownFormats.WebP, KnownFormats.Bmp, KnownFormats.Tiff,
        KnownFormats.Gif, KnownFormats.Heic, KnownFormats.Avif, KnownFormats.Ico,
    ];

    private static readonly (FileFormat Format, int Priority)[] Outputs =
    [
        (KnownFormats.Png, 10), (KnownFormats.Jpeg, 11), (KnownFormats.WebP, 12), (KnownFormats.Pdf, 13),
        (KnownFormats.Avif, 14), (KnownFormats.Heic, 15), (KnownFormats.Tiff, 16), (KnownFormats.Gif, 17),
        (KnownFormats.Bmp, 18), (KnownFormats.Ico, 19),
    ];

    private static readonly Lazy<Dictionary<MagickFormat, IMagickFormatInfo>> Coders = new(() =>
    {
        ConfigureLimits();
        return MagickNET.SupportedFormats.GroupBy(f => f.Format).ToDictionary(g => g.Key, g => g.First());
    });
    private static readonly Lazy<bool> HeicWriterWorks = new(ProbeHeicWriter);

    public ImageMagickConverter()
    {
        Routes = Outputs.Select(o => new ConversionRoute
        {
            TargetKey = o.Format.Id,
            Label = o.Format.Label,
            Kind = RouteKind.Convert,
            Inputs = RasterInputs.Where(i => i != o.Format).Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase),
            Output = o.Format,
            Priority = o.Priority,
            Description = o.Format == KnownFormats.Pdf ? "Image as a PDF page" : $"Convert to {o.Format.DisplayName}",
        }).Concat(ImageActions.Routes()).ToList();
    }

    public string Id => "imagemagick";
    public string DisplayName => "ImageMagick (Magick.NET)";
    public IReadOnlyList<ConversionRoute> Routes { get; }

    public static string EngineVersion => MagickNET.Version;

    public RouteAvailability GetAvailability(ConversionRoute route)
    {
        if (route.Output is null)
        {
            return RouteAvailability.Available; // same-format actions
        }
        var output = ToMagick(route.Output);
        if (!Coders.Value.TryGetValue(output, out var info) || !info.SupportsWriting)
        {
            return RouteAvailability.Unavailable($"The bundled ImageMagick cannot write {route.Output!.Label}.");
        }
        if (route.Output == KnownFormats.Heic && !HeicWriterWorks.Value)
        {
            return RouteAvailability.Unavailable("HEIC encoding is not available in this build. Install a build with a working HEIC writer or choose AVIF/WebP.");
        }
        return RouteAvailability.Available;
    }

    /// <summary>
    /// ImageMagick can advertise a coder even when its native HEIC delegate cannot actually encode.
    /// Prove capability with a one-pixel in-memory encode before exposing HEIC as an output.
    /// </summary>
    private static bool ProbeHeicWriter()
    {
        try
        {
            if (!Coders.Value.TryGetValue(MagickFormat.Heic, out var coder) || !coder.SupportsWriting)
            {
                return false;
            }
            using var image = new MagickImage(MagickColors.Black, 1, 1);
            var bytes = image.ToByteArray(MagickFormat.Heic);
            return bytes.Length >= 12
                   && bytes[4] == (byte)'f' && bytes[5] == (byte)'t'
                   && bytes[6] == (byte)'y' && bytes[7] == (byte)'p';
        }
        catch (Exception ex) when (ex is MagickException or NotSupportedException or TypeInitializationException)
        {
            return false;
        }
    }

    public static bool CanRead(FileFormat format) =>
        Coders.Value.TryGetValue(ToMagick(format), out var info) && info.SupportsReading;

    public Task<IReadOnlyList<string>> ExecuteAsync(ConversionContext context, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<string>>(() => context.Route.TargetKey switch
        {
            ImageActions.ShrinkKey => [ImageActions.Shrink(context, cancellationToken)],
            ImageActions.StripKey => [ImageActions.Strip(context, cancellationToken)],
            _ => [Convert(context, cancellationToken)],
        }, cancellationToken);

    private static string Convert(ConversionContext context, CancellationToken ct)
    {
        var input = context.Input;
        var inputFormat = input.Format ?? throw new ConversionException("Unknown image type.");
        if (!CanRead(inputFormat))
        {
            throw new ConversionException($"The bundled ImageMagick cannot read {inputFormat.Label} files.");
        }

        var output = context.Route.Output!;
        var outputMagick = ToMagick(output);
        var options = context.Options.Image;
        var outPath = context.DefaultOutputPath();

        try
        {
            using var frames = new MagickImageCollection();
            // The coder is chosen from the verified signature, never from file content hints,
            // which prevents a disguised file from reaching script-like coders (MVG, MSL, SVG, ...).
            frames.Read(input.Path, new MagickReadSettings { Format = ToMagick(inputFormat) });
            ct.ThrowIfCancellationRequested();
            context.ReportProgress(0.3);

            var keepAllFrames = frames.Count > 1 && (
                (IsAnimated(inputFormat) && IsAnimated(output) && options.KeepAnimation)
                || (inputFormat == KnownFormats.Tiff && (output == KnownFormats.Tiff || output == KnownFormats.Pdf)));

            if (keepAllFrames)
            {
                if (IsAnimated(inputFormat))
                {
                    frames.Coalesce();
                }
                var index = 0;
                foreach (var frame in frames)
                {
                    ct.ThrowIfCancellationRequested();
                    Process(frame, output, options);
                    context.ReportProgress(0.3 + 0.6 * (++index) / frames.Count);
                }
                if (output == KnownFormats.Gif)
                {
                    frames.Optimize();
                }
                frames.Write(outPath, outputMagick);
            }
            else
            {
                using var image = PickFrame(frames, inputFormat).Clone();
                Process(image, output, options);
                ct.ThrowIfCancellationRequested();
                context.ReportProgress(0.8);
                if (output == KnownFormats.Ico)
                {
                    PrepareIcon(image);
                }
                image.Write(outPath, outputMagick);
            }
        }
        catch (MagickException ex)
        {
            throw new ConversionException(DescribeMagickError(ex, inputFormat), ex, ex.Message);
        }

        return outPath;
    }

    /// <summary>Reads the pixel size from the header without decoding the image, or null.</summary>
    public static (uint Width, uint Height)? ReadDimensions(InputFile input)
    {
        if (input.Format is not { } format || !CanRead(format))
        {
            return null;
        }
        try
        {
            var info = new MagickImageInfo();
            info.Read(input.Path, new MagickReadSettings { Format = ToMagick(format) });
            return (info.Width, info.Height);
        }
        catch (Exception ex) when (ex is MagickException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Encodes the first frame in memory with the same processing as a real conversion and returns
    /// the byte count, so the UI can show a measured (not guessed) output size. Returns null when
    /// the file cannot be read or the format cannot be written.
    /// </summary>
    public static long? EstimateOutputSize(InputFile input, FileFormat output, ImageOptions options, CancellationToken ct)
    {
        if (input.Format is not { } inputFormat || !CanRead(inputFormat))
        {
            return null;
        }
        try
        {
            using var frames = new MagickImageCollection();
            frames.Read(input.Path, new MagickReadSettings { Format = ToMagick(inputFormat) });
            ct.ThrowIfCancellationRequested();
            using var image = PickFrame(frames, inputFormat).Clone();
            Process(image, output, options);
            ct.ThrowIfCancellationRequested();
            if (output == KnownFormats.Ico)
            {
                PrepareIcon(image);
            }
            return image.ToByteArray(ToMagick(output)).LongLength;
        }
        catch (Exception ex) when (ex is MagickException or ConversionException or ArgumentException)
        {
            return null;
        }
    }

    private static void Process(IMagickImage<byte> image, FileFormat output, ImageOptions options)
    {
        image.AutoOrient();

        if (options.HasCrop)
        {
            var x = Math.Max(0, options.CropX ?? 0);
            var y = Math.Max(0, options.CropY ?? 0);
            if (x >= image.Width || y >= image.Height)
            {
                throw new ConversionException($"The crop area starts outside the image ({image.Width}×{image.Height}).");
            }
            var w = (uint)Math.Min(options.CropWidth!.Value, image.Width - x);
            var h = (uint)Math.Min(options.CropHeight!.Value, image.Height - y);
            image.Crop(new MagickGeometry(x, y, w, h));
            image.ResetPage();
        }

        if (options.Rotation is 90 or 180 or 270)
        {
            image.Rotate(options.Rotation);
        }

        if (options.MaxWidth is > 0 || options.MaxHeight is > 0)
        {
            var box = new MagickGeometry((uint)(options.MaxWidth ?? int.MaxValue), (uint)(options.MaxHeight ?? int.MaxValue))
            {
                Greater = true, // only shrink, never enlarge
            };
            image.Resize(box);
        }
        else if (options.ScalePercent is > 0 and not 100)
        {
            image.Resize(new Percentage(options.ScalePercent.Value));
        }

        var profile = image.GetColorProfile();
        if (options.ConvertToSrgb && profile is not null)
        {
            image.TransformColorSpace(ColorProfiles.SRGB);
            profile = image.GetColorProfile();
        }

        if (options.StripMetadata)
        {
            image.Strip();
            if (profile is not null)
            {
                image.SetProfile(profile); // keep colors correct even without metadata
            }
        }

        if (!SupportsAlpha(output) && image.HasAlpha)
        {
            image.BackgroundColor = ParseColor(options.BackgroundColor);
            image.Alpha(AlphaOption.Remove);
        }

        image.Quality = (uint)Math.Clamp(options.Quality, 1, 100);
    }

    private static void PrepareIcon(IMagickImage<byte> image)
    {
        if (image.Width > 256 || image.Height > 256)
        {
            image.Resize(new MagickGeometry(256, 256));
        }
        // Embed the standard icon sizes, but never enlarge a small source.
        var largest = Math.Max(image.Width, image.Height);
        var sizes = new uint[] { 256, 128, 64, 48, 32, 16 }.Where(s => s <= largest).DefaultIfEmpty(16u);
        image.Settings.SetDefine(MagickFormat.Icon, "auto-resize", string.Join(",", sizes));
    }

    private static IMagickImage<byte> PickFrame(MagickImageCollection frames, FileFormat inputFormat)
    {
        // Icons hold several sizes; the largest is the best source.
        return inputFormat == KnownFormats.Ico
            ? frames.OrderByDescending(f => (long)f.Width * f.Height).First()
            : frames[0];
    }

    private static MagickColor ParseColor(string value)
    {
        try
        {
            return new MagickColor(string.IsNullOrWhiteSpace(value) ? "#FFFFFF" : value);
        }
        catch (ArgumentException)
        {
            return MagickColors.White;
        }
    }

    private static string DescribeMagickError(MagickException ex, FileFormat input) => ex switch
    {
        MagickCorruptImageErrorException or MagickCorruptImageWarningException => $"The {input.Label} file is damaged or incomplete.",
        MagickMissingDelegateErrorException => $"The bundled ImageMagick lacks support for this {input.Label} variant.",
        MagickResourceLimitErrorException => "The image is too large to process.",
        _ => $"Image conversion failed: {FirstLine(ex.Message)}",
    };

    private static string FirstLine(string s) => s.Split('\n')[0].Trim();

    private static bool IsAnimated(FileFormat f) => f == KnownFormats.Gif || f == KnownFormats.WebP;

    private static bool SupportsAlpha(FileFormat f) =>
        f != KnownFormats.Jpeg && f != KnownFormats.Bmp && f != KnownFormats.Pdf;

    internal static MagickFormat ToMagick(FileFormat format) => format.Id switch
    {
        "jpeg" => MagickFormat.Jpeg,
        "png" => MagickFormat.Png,
        "webp" => MagickFormat.WebP,
        "bmp" => MagickFormat.Bmp,
        "tiff" => MagickFormat.Tiff,
        "gif" => MagickFormat.Gif,
        "heic" => MagickFormat.Heic,
        "avif" => MagickFormat.Avif,
        "ico" => MagickFormat.Ico,
        "pdf" => MagickFormat.Pdf,
        _ => throw new ConversionException($"{format.Label} is not an image format."),
    };

    private static void ConfigureLimits()
    {
        // Bound memory use for hostile or enormous images; ImageMagick spills to disk beyond these.
        ResourceLimits.Width = 60_000;
        ResourceLimits.Height = 60_000;
        ResourceLimits.ListLength = 10_000;
    }
}
