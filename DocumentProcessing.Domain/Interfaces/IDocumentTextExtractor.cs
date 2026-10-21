using DocumentProcessing.Domain.Models;

namespace DocumentProcessing.Domain.Interfaces;

public interface IDocumentTextExtractor
{
    /// <summary>
    /// Extracts text from a PDF document stream.
    /// </summary>
    /// <param name="documentId">The ID of the document being extracted.</param>
    /// <param name="pdfStream">The PDF file stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The text extraction result with per-page text and metadata.</returns>
    Task<TextExtractionResult> ExtractAsync(
        Guid documentId,
        Stream pdfStream,
        CancellationToken cancellationToken = default);
}