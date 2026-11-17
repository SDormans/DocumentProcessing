using DocumentProcessing.Domain.Interfaces;
using DocumentProcessing.Application.DTOs;

namespace DocumentProcessing.Application.Services;

public interface IDocumentService
{
    Task<UploadDocumentResponse> UploadDocumentAsync(
        Stream fileStream,
        string originalFileName,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<DocumentDto?> GetDocumentAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentDto>> GetDocumentsAsync(CancellationToken cancellationToken = default);

    /// <summary>One page of the documents the current user may see, newest first.</summary>
    /// <summary>Re-runs extraction for a document awaiting review; the new result becomes the next version.</summary>
    Task<ReprocessResult> ReprocessAsync(Guid id, CancellationToken cancellationToken = default);

    Task<DocumentPage> GetDocumentPageAsync(DocumentQuery query, CancellationToken cancellationToken = default);

    Task<DocumentFileDto?> GetDocumentFileAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record DocumentPage(IReadOnlyList<DocumentDto> Items, int TotalCount);

public enum ReprocessResult
{
    Queued,
    NotFound,
    NotReprocessable
}
