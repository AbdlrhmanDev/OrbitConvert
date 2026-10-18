using System.IO.Compression;
using System.Text;

namespace OrbitConvert.Core.Formats;

public enum DetectionMethod
{
    /// <summary>The file content matched a known signature.</summary>
    Signature,
    /// <summary>Text content; the specific format was taken from the extension.</summary>
    TextHeuristic,
    /// <summary>No signature matched; the extension was trusted.</summary>
    Extension,
    /// <summary>The type could not be determined.</summary>
    Unknown,
}

public sealed record DetectionResult(FileFormat? Format, DetectionMethod Method, string? Warning = null)
{
    public static DetectionResult Unknown(string? warning = null) => new(null, DetectionMethod.Unknown, warning);
}

public interface IFileTypeDetector
{
    DetectionResult Detect(string path);
    DetectionResult Detect(Stream stream, string? fileNameHint);
}

/// <summary>
/// Identifies files by their content ("magic bytes") rather than trusting the extension.
/// The extension is only used to disambiguate text formats or when no signature exists.
/// </summary>
public sealed class FileTypeDetector : IFileTypeDetector
{
    private const int HeaderSize = 4096;

    public DetectionResult Detect(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
        return Detect(stream, path);
    }

    public DetectionResult Detect(Stream stream, string? fileNameHint)
    {
        var header = new byte[HeaderSize];
        var read = ReadUpTo(stream, header);
        var span = header.AsSpan(0, read);
        var byExtension = fileNameHint is null ? null : KnownFormats.FindByPath(fileNameHint);

        if (read == 0)
        {
            return DetectionResult.Unknown("The file is empty.");
        }

        var signature = MatchSignature(span, stream);
        if (signature is not null)
        {
            string? warning = null;
            if (byExtension is not null && !AreCompatible(byExtension, signature))
            {
                warning = $"The file extension suggests {byExtension.Label}, but the content is {signature.Label}.";
            }
            return new DetectionResult(signature, DetectionMethod.Signature, warning);
        }

        if (LooksLikeText(span))
        {
            if (byExtension is { Category: FormatCategory.Document } && IsTextFormat(byExtension))
            {
                return new DetectionResult(byExtension, DetectionMethod.TextHeuristic);
            }
            if (LooksLikeHtml(span))
            {
                return new DetectionResult(KnownFormats.Html, DetectionMethod.TextHeuristic);
            }
            if (byExtension is null)
            {
                return new DetectionResult(KnownFormats.Txt, DetectionMethod.TextHeuristic);
            }
        }

        // Formats with weak or no signatures (e.g. headerless MP3 streams) fall back to the extension,
        // but only for categories where a decoder will validate the content anyway.
        if (byExtension is { Category: FormatCategory.Audio or FormatCategory.Video })
        {
            return new DetectionResult(byExtension, DetectionMethod.Extension,
                "The file content could not be verified; trusting the extension.");
        }

        return DetectionResult.Unknown(byExtension is null
            ? "Unrecognized file type."
            : $"The file has a {byExtension.Label} extension, but its content does not look like {byExtension.Label}.");
    }

    private static bool IsTextFormat(FileFormat f) =>
        f == KnownFormats.Txt || f == KnownFormats.Markdown || f == KnownFormats.Html;

    /// <summary>Formats sharing a container (e.g. .m4a in an MP4 box) should not warn.</summary>
    private static bool AreCompatible(FileFormat byExtension, FileFormat bySignature)
    {
        if (byExtension == bySignature)
        {
            return true;
        }
        string[] isoBmff = ["mp4", "mov", "m4a"];
        if (isoBmff.Contains(byExtension.Id) && isoBmff.Contains(bySignature.Id))
        {
            return true;
        }
        // WebM is a Matroska profile.
        return (byExtension.Id, bySignature.Id) is ("mkv", "webm") or ("webm", "mkv");
    }

    private static FileFormat? MatchSignature(ReadOnlySpan<byte> h, Stream stream)
    {
        // Signatures containing bytes >= 0x80 must be byte arrays: u8 literals would UTF-8 encode them.
        if (h.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return KnownFormats.Png;
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return KnownFormats.Jpeg;
        if (h.StartsWith("GIF87a"u8) || h.StartsWith("GIF89a"u8)) return KnownFormats.Gif;
        if (h.StartsWith("BM"u8) && h.Length >= 18 && h[14] is 12 or 40 or 52 or 56 or 64 or 108 or 124 && h[15] == 0) return KnownFormats.Bmp;
        if (h.StartsWith("II*\0"u8) || h.StartsWith("MM\0*"u8)) return KnownFormats.Tiff;
        if (h.Length >= 6 && h[0] == 0 && h[1] == 0 && h[2] == 1 && h[3] == 0 && h[4] != 0) return KnownFormats.Ico;
        if (h.StartsWith("%PDF-"u8) || h[..Math.Min(h.Length, 1024)].IndexOf("%PDF-"u8) >= 0) return KnownFormats.Pdf;
        if (h.StartsWith((ReadOnlySpan<byte>)[0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C])) return KnownFormats.SevenZip;
        if (h.StartsWith("fLaC"u8)) return KnownFormats.Flac;
        if (h.StartsWith("{\\rtf"u8)) return KnownFormats.Rtf;
        if (h.StartsWith((ReadOnlySpan<byte>)[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1])) return MatchOleCompound(h);

        if (h.Length >= 12 && h.StartsWith("RIFF"u8))
        {
            var kind = h.Slice(8, 4);
            if (kind.SequenceEqual("WEBP"u8)) return KnownFormats.WebP;
            if (kind.SequenceEqual("WAVE"u8)) return KnownFormats.Wav;
            if (kind.SequenceEqual("AVI "u8)) return KnownFormats.Avi;
            return null;
        }

        if (h.StartsWith("OggS"u8))
        {
            return h.IndexOf("OpusHead"u8) >= 0 ? KnownFormats.Opus : KnownFormats.Ogg;
        }

        if (h.Length >= 12 && h.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            return MatchIsoBmff(h);
        }

        if (h.StartsWith((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3]))
        {
            return h[..Math.Min(h.Length, 64)].IndexOf("webm"u8) >= 0 ? KnownFormats.WebM : KnownFormats.Mkv;
        }

        if (h.StartsWith("ID3"u8)) return KnownFormats.Mp3;
        if (h.Length >= 2 && h[0] == 0xFF)
        {
            // ADTS AAC: 1111 1111 1111 x00x (layer bits are always 0)
            if ((h[1] & 0xF6) == 0xF0) return KnownFormats.Aac;
            // MPEG audio frame sync: 11 bits set, layer != reserved
            if ((h[1] & 0xE0) == 0xE0 && (h[1] & 0x06) != 0) return KnownFormats.Mp3;
        }

        if (h.StartsWith("PK\x03\x04"u8) || h.StartsWith("PK\x05\x06"u8))
        {
            return MatchZipContainer(stream);
        }

        return null;
    }

    private static FileFormat? MatchOleCompound(ReadOnlySpan<byte> h)
    {
        // Word 97-2003 documents are OLE compound files. Without parsing the FAT we look for the
        // "WordDocument" stream name (UTF-16) in the header region, which is where it usually appears.
        var wordDocument = Encoding.Unicode.GetBytes("WordDocument");
        return h.IndexOf(wordDocument) >= 0 ? KnownFormats.Doc : null;
    }

    private static FileFormat? MatchIsoBmff(ReadOnlySpan<byte> h)
    {
        var boxSize = (int)Math.Min(h.Length, (uint)(h[0] << 24 | h[1] << 16 | h[2] << 8 | h[3]));
        var major = Encoding.ASCII.GetString(h.Slice(8, 4));
        var brands = new List<string> { major };
        for (var i = 16; i + 4 <= boxSize; i += 4)
        {
            brands.Add(Encoding.ASCII.GetString(h.Slice(i, 4)));
        }

        if (brands.Any(b => b is "avif" or "avis")) return KnownFormats.Avif;
        if (brands.Any(b => b is "heic" or "heix" or "hevc" or "hevx" or "heim" or "heis" or "mif1" or "msf1")) return KnownFormats.Heic;
        if (major is "M4A " or "M4B " or "M4P ") return KnownFormats.M4a;
        if (major == "qt  ") return KnownFormats.Mov;
        return KnownFormats.Mp4;
    }

    private static FileFormat? MatchZipContainer(Stream stream)
    {
        if (!stream.CanSeek)
        {
            return KnownFormats.Zip;
        }

        try
        {
            stream.Position = 0;
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var mimetype = zip.GetEntry("mimetype");
            if (mimetype is not null && mimetype.Length < 256)
            {
                using var reader = new StreamReader(mimetype.Open(), Encoding.ASCII);
                var value = reader.ReadToEnd().Trim();
                if (value == "application/epub+zip") return KnownFormats.Epub;
                if (value == "application/vnd.oasis.opendocument.text") return KnownFormats.Odt;
            }
            if (zip.GetEntry("[Content_Types].xml") is not null && zip.GetEntry("word/document.xml") is not null)
            {
                return KnownFormats.Docx;
            }
            return KnownFormats.Zip;
        }
        catch (InvalidDataException)
        {
            // Truncated or damaged archive; still report ZIP so the archive tools can explain the damage.
            return KnownFormats.Zip;
        }
    }

    private static bool LooksLikeText(ReadOnlySpan<byte> h)
    {
        if (h.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) || h.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || h.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return true;
        }
        var control = 0;
        foreach (var b in h)
        {
            if (b == 0) return false;
            if (b < 0x09 || (b > 0x0D && b < 0x20 && b != 0x1B)) control++;
        }
        return control <= h.Length / 100;
    }

    private static bool LooksLikeHtml(ReadOnlySpan<byte> h)
    {
        var text = Encoding.UTF8.GetString(h[..Math.Min(h.Length, 1024)]).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return text.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadUpTo(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = stream.Read(buffer, total, buffer.Length - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
