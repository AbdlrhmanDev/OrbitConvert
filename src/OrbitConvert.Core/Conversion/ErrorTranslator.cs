namespace OrbitConvert.Core.Conversion;

/// <summary>Turns exceptions into short messages a user can act on.</summary>
public static class ErrorTranslator
{
    private const int ErrorSharingViolation = unchecked((int)0x80070020);
    private const int ErrorLockViolation = unchecked((int)0x80070021);
    private const int ErrorDiskFull = unchecked((int)0x80070070);
    private const int ErrorHandleDiskFull = unchecked((int)0x80070027);
    private const int ErrorFilenameTooLong = unchecked((int)0x800700CE);

    public static string ToUserMessage(Exception ex) => ex switch
    {
        ConversionException ce => ce.Message,
        OperationCanceledException => "The conversion was canceled.",
        FileNotFoundException fnf => $"File not found: {Path.GetFileName(fnf.FileName ?? string.Empty)}",
        DirectoryNotFoundException => "A folder in the path does not exist.",
        UnauthorizedAccessException => "Access denied. Check that you have permission to read the file and write to the output folder.",
        PathTooLongException => "The path is too long for this operation.",
        IOException io when IsDiskFull(io) => "There is not enough disk space to finish the conversion.",
        IOException io when io.HResult is ErrorSharingViolation or ErrorLockViolation =>
            "The file is in use by another program. Close it and try again.",
        IOException io when io.HResult == ErrorFilenameTooLong => "The path is too long for this operation.",
        IOException io => $"File error: {io.Message}",
        OutOfMemoryException => "The file is too large to process in memory.",
        _ => $"Unexpected error: {ex.Message}",
    };

    public static bool IsDiskFull(Exception ex) =>
        ex is IOException { HResult: ErrorDiskFull or ErrorHandleDiskFull };

    /// <summary>Recognizes common failure messages in a tool's stderr output.</summary>
    public static string? FromToolOutput(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
        {
            return null;
        }
        if (stderr.Contains("No space left on device", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("disk full", StringComparison.OrdinalIgnoreCase))
        {
            return "There is not enough disk space to finish the conversion.";
        }
        if (stderr.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
        {
            return "Access denied while reading or writing a file.";
        }
        if (stderr.Contains("Invalid data found when processing input", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("moov atom not found", StringComparison.OrdinalIgnoreCase))
        {
            return "The file appears to be damaged or is not a valid media file.";
        }
        if (stderr.Contains("Unknown encoder", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("Encoder not found", StringComparison.OrdinalIgnoreCase))
        {
            return "The required encoder is not included in your FFmpeg build.";
        }
        return null;
    }
}
