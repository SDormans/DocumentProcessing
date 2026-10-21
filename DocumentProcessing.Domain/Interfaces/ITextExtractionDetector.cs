using DocumentProcessing.Domain.Models;

namespace DocumentProcessing.Domain.Interfaces;

public interface ITextExtractionDetector
{
    /// <summary>
    /// Determines whether the PDF contains readable text (native) or requires OCR.
    /// </summary>
    Task<bool> NeedsOcrAsync(
        Stream pdfStream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the ratio of pages with extractable text (0.0 = no text, 1.0 = all pages have text).
    /// </summary>
    Task<double> GetTextCoverageAsync(
        Stream pdfStream,
        CancellationToken cancellationToken = default);
}