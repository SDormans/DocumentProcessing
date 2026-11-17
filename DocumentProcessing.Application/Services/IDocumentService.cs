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

    /// <summary>
    /// Puts a failed document back on the processing queue.
    /// </summary>
    Task<RequeueResult> RequeueAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Re-queues a failed document the current user can access, to be processed with <paramref name="modelType"/>.</summary>
    Task<ModelSwitchResult> RetryWithModelAsync(Guid id, string? modelType, CancellationToken cancellationToken = default);

    Task<DocumentFileDto?> GetDocumentFileAsync(Guid id, CancellationToken cancellationToken = default);
}

public enum RequeueResult
{
    Requeued,
    NotFound,
    NotFailed
}

public enum ModelSwitchResult
{
    Requeued,
    NotFound,
    NotFailed,
    InvalidModel,
    ModelUnavailable
}
