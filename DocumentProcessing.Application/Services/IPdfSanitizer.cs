namespace DocumentProcessing.Application.Services;

/// <summary>
/// Produces a copy of a PDF without active content (JavaScript, automatic actions, launch actions, annotations,
/// embedded files, forms), so a browser PDF viewer cannot run anything when the document is previewed.
/// </summary>
public interface IPdfSanitizer
{
    /// <summary>The cleaned PDF, or null when the file cannot be parsed and rebuilt.</summary>
    byte[]? Sanitize(byte[] pdf);
}
