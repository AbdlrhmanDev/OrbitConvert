namespace OrbitConvert.Core.Conversion;

/// <summary>
/// A conversion failure with a message that is safe and useful to show to the user.
/// </summary>
public class ConversionException : Exception
{
    public ConversionException(string userMessage, Exception? inner = null, string? technicalDetail = null)
        : base(userMessage, inner)
    {
        TechnicalDetail = technicalDetail;
    }

    /// <summary>Extra diagnostic text (e.g. the tail of a tool's stderr) for logs and the details pane.</summary>
    public string? TechnicalDetail { get; }
}
