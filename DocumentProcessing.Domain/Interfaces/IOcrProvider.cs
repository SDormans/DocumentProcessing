namespace DocumentProcessing.Domain.Interfaces;

public interface IOcrProvider
{
    /// <summary>
    /// Extracts text from a single page image using OCR.
    /// </summary>
    /// <param name="imageStream">The page image stream.</param>
    /// <param name="pageNumber">The page number (1-based).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The extracted text, or empty string if no text was found.</returns>
    Task<string> ExtractTextFromImageAsync(
        Stream imageStream,
        int pageNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if the OCR provider is available and configured.
    /// </summary>
    bool IsAvailable { get; }
}