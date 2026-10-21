using DocumentProcessing.Application.DTOs.TextExtraction;
using DocumentProcessing.Domain.Interfaces;

namespace DocumentProcessing.Application.Services.TextExtraction;

public interface IDocumentTextExtractionService
{
    /// <summary>
    /// Extracts text from the stored PDF document.
    /// </summary>
    Task<ExtractTextResponse> ExtractTextAsync(
        ExtractTextRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a previously stored text extraction result.
    /// </summary>
    Task<TextExtractionResultDto?> GetExtractionResultAsync(
        Guid documentId,
        int extractionVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all text extraction results for a document.
    /// </summary>
    Task<IReadOnlyList<TextExtractionResultDto>> GetExtractionResultsAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);
}