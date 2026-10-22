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

    Task<DocumentFileDto?> GetDocumentFileAsync(Guid id, CancellationToken cancellationToken = default);
}