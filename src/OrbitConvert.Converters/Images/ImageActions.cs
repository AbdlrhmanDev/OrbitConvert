using ImageMagick;
using OrbitConvert.Core.Conversion;
using OrbitConvert.Core.Formats;

namespace OrbitConvert.Converters.Images;

/// <summary>
/// Same-format image actions offered in the Orbit Menu's image wheel:
/// <b>Shrink</b> (smaller file, honest failure if it cannot be made smaller) and
/// <b>Strip metadata</b> (lossless for JPEG and PNG: pixels are never re-encoded).
/// </summary>
public static class ImageActions
{
    public const string ShrinkKey = "image-shrink";
    public const string StripKey = "image-strip";

    /// <summary>Quality used by Shrink for lossy formats; visually close to the source for photos.</summary>
    private const int ShrinkQuality = 72;

    internal static IEnumerable<ConversionRoute> Routes()
    {
        yield return new ConversionRoute
        {
            TargetKey = ShrinkKey,
            Label = "Shrink",
            Kind = RouteKind.Transform,
            Inputs = ConversionRoute.Formats(KnownFormats.Jpeg, KnownFormats.Png, KnownFormats.WebP, KnownFormats.Avif),
            Output = null,
            Priority = 8,
            Description = "Smaller file in the same format; removes metadata",
            Limitations = "JPG, WebP and AVIF are re-encoded at quality 72. PNG is recompressed losslessly. If the file cannot be made smaller, nothing is saved.",
        };
        yield return new ConversionRoute
        {
            TargetKey = StripKey,
            Label = "Strip metadata",
            Kind = RouteKind.Transform,
            Inputs = ConversionRoute.Formats(KnownFormats.Jpeg, KnownFormats.Png),
            Output = null,
            Priority = 9,
            Description = "Remove EXIF, GPS location, XMP and text metadata without re-encoding",
            Limitations = "Color profiles are kept. A JPEG whose EXIF rotates it is re-encoded at quality 95 so it still displays upright.",
        };
    }

    internal static string Shrink(ConversionContext context, CancellationToken ct)
    {
        var input = context.Input;
        var format = input.Format!;
        var output = context.DefaultOutputPath("-small");
        try
        {
            using var image = new MagickImage(input.Path, new MagickReadSettings { Format = ImageMagickConverter.ToMagick(format) });
            ct.ThrowIfCancellationRequested();
            image.AutoOrient();
            var profile = image.GetColorProfile();
            image.Strip();
            if (profile is not null)
            {
                image.SetProfile(profile);
            }
            if (format == KnownFormats.Png)
            {
                // Lossless: strongest zlib level with adaptive filtering.
                image.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
                image.Settings.SetDefine(MagickFormat.Png, "compression-filter", "5");
                image.Settings.SetDefine(MagickFormat.Png, "compression-strategy", "1");
            }
            else
            {
                image.Quality = (uint)Math.Min(ShrinkQuality, Math.Clamp(context.Options.Image.Quality, 1, 100));
            }
            image.Write(output, ImageMagickConverter.ToMagick(format));
        }
        catch (MagickException ex)
        {
            throw new ConversionException($"The {format.Label} file could not be read.", ex, ex.Message);
        }

        var before = input.Length;
        var after = new FileInfo(output).Length;
        if (after >= before)
        {
            throw new ConversionException("This image is already compact; shrinking would not make it smaller.");
        }
        context.ResultNote = $"Reduced by {100.0 * (before - after) / before:0}%";
        return output;
    }

    internal static string Strip(ConversionContext context, CancellationToken ct)
    {
        var input = context.Input;
        var output = context.DefaultOutputPath("-clean");
        var bytes = File.ReadAllBytes(input.Path);
        ct.ThrowIfCancellationRequested();

        byte[] cleaned;
        int removed;
        if (input.Format == KnownFormats.Png)
        {
            (cleaned, removed) = StripPng(bytes);
        }
        else if (HasRotatingOrientation(input.Path))
        {
            // Dropping EXIF would also drop the orientation flag and show the photo sideways.
            using var image = new MagickImage(bytes, MagickFormat.Jpeg);
            var profile = image.GetColorProfile();
            image.AutoOrient();
            image.Strip();
            if (profile is not null)
            {
                image.SetProfile(profile);
            }
            image.Quality = 95;
            image.Write(output, MagickFormat.Jpeg);
            context.ResultNote = "Metadata removed (re-encoded to keep the orientation)";
            return output;
        }
        else
        {
            (cleaned, removed) = StripJpeg(bytes);
        }

        if (removed == 0)
        {
            throw new ConversionException("This image has no metadata to remove.");
        }
        File.WriteAllBytes(output, cleaned);
        context.ResultNote = $"Removed {removed} metadata block{(removed == 1 ? "" : "s")}; pixels unchanged";
        return output;
    }

    private static bool HasRotatingOrientation(string path)
    {
        try
        {
            using var image = new MagickImage();
            image.Ping(path);
            return image.Orientation is not (OrientationType.Undefined or OrientationType.TopLeft);
        }
        catch (MagickException)
        {
            return false;
        }
    }

    /// <summary>
    /// Copies a JPEG marker by marker, dropping APP1 (EXIF/XMP), APP3–APP13 (incl. IPTC/Photoshop),
    /// APP15 and comments. Keeps APP0 (JFIF), APP2 (ICC profile) and APP14 (Adobe color transform,
    /// which changes how the pixels decode). Entropy-coded data after SOS is copied untouched.
    /// </summary>
    internal static (byte[] Data, int Removed) StripJpeg(byte[] data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
        {
            throw new ConversionException("The file is not a valid JPEG.");
        }
        using var output = new MemoryStream(data.Length);
        output.Write(data, 0, 2);
        var removed = 0;
        var i = 2;
        while (i + 4 <= data.Length)
        {
            if (data[i] != 0xFF)
            {
                throw new ConversionException("The JPEG structure is damaged.");
            }
            var marker = data[i + 1];
            if (marker == 0xFF)
            {
                i++; // fill byte
                continue;
            }
            if (marker == 0xDA) // start of scan: the rest is image data
            {
                output.Write(data, i, data.Length - i);
                return (output.ToArray(), removed);
            }
            var length = (data[i + 2] << 8) | data[i + 3];
            if (length < 2 || i + 2 + length > data.Length)
            {
                throw new ConversionException("The JPEG structure is damaged.");
            }
            var isMetadata = marker == 0xFE || (marker >= 0xE1 && marker <= 0xEF && marker != 0xE2 && marker != 0xEE);
            if (isMetadata)
            {
                removed++;
            }
            else
            {
                output.Write(data, i, 2 + length);
            }
            i += 2 + length;
        }
        throw new ConversionException("The JPEG has no image data.");
    }

    /// <summary>Drops PNG text, EXIF and timestamp chunks; every other chunk is copied byte for byte.</summary>
    internal static (byte[] Data, int Removed) StripPng(byte[] data)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(signature))
        {
            throw new ConversionException("The file is not a valid PNG.");
        }
        string[] metadataChunks = ["tEXt", "zTXt", "iTXt", "eXIf", "tIME"];
        using var output = new MemoryStream(data.Length);
        output.Write(data, 0, 8);
        var removed = 0;
        var i = 8;
        while (i + 12 <= data.Length)
        {
            var length = (int)((uint)data[i] << 24 | (uint)data[i + 1] << 16 | (uint)data[i + 2] << 8 | data[i + 3]);
            var type = System.Text.Encoding.ASCII.GetString(data, i + 4, 4);
            var total = 12L + length;
            if (length < 0 || i + total > data.Length)
            {
                throw new ConversionException("The PNG structure is damaged.");
            }
            if (metadataChunks.Contains(type))
            {
                removed++;
            }
            else
            {
                output.Write(data, i, (int)total);
            }
            i += (int)total;
            if (type == "IEND")
            {
                return (output.ToArray(), removed);
            }
        }
        throw new ConversionException("The PNG is incomplete.");
    }
}
