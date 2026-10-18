using OrbitConvert.Core.Formats;

namespace OrbitConvert.Core.Conversion;

/// <summary>
/// Verifies that a converter really produced what it claimed: the file exists, is not empty,
/// and — for binary formats — its content signature matches the requested format.
/// </summary>
public sealed class OutputValidator(IFileTypeDetector detector)
{
    public void Validate(string path, FileFormat? expected)
    {
        if (Directory.Exists(path))
        {
            return;
        }

        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new ConversionException($"The converter reported success but did not create {Path.GetFileName(path)}.");
        }
        if (info.Length == 0)
        {
            throw new ConversionException($"The converter produced an empty file ({Path.GetFileName(path)}).");
        }

        var expectedFormat = expected is not null && expected.HasExtension(Path.GetExtension(path))
            ? expected
            : KnownFormats.FindByPath(path);
        if (expectedFormat is null || IsTextual(expectedFormat))
        {
            return;
        }

        var detection = detector.Detect(path);
        if (detection.Method != DetectionMethod.Signature)
        {
            // Some valid outputs (e.g. raw MP3 without ID3) have weak signatures; only fail on a clear mismatch.
            return;
        }
        if (!SameFamily(detection.Format!, expectedFormat))
        {
            throw new ConversionException(
                $"Output validation failed: expected {expectedFormat.Label} but the file content is {detection.Format!.Label}.");
        }
    }

    private static bool IsTextual(FileFormat f) =>
        f == KnownFormats.Txt || f == KnownFormats.Markdown || f == KnownFormats.Html || f == KnownFormats.Rtf;

    private static bool SameFamily(FileFormat actual, FileFormat expected)
    {
        if (actual == expected)
        {
            return true;
        }
        string[] isoBmff = ["mp4", "mov", "m4a"];
        if (isoBmff.Contains(actual.Id) && isoBmff.Contains(expected.Id))
        {
            return true;
        }
        if ((actual.Id, expected.Id) is ("mkv", "webm") or ("webm", "mkv"))
        {
            return true;
        }
        // An AAC stream in ADTS framing may be sniffed as MP3 frame sync and vice versa.
        return (actual.Id, expected.Id) is ("mp3", "aac") or ("aac", "mp3");
    }
}
