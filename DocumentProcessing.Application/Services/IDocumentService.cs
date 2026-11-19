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

    Task<PagedResult<DocumentDto>> GetDocumentsAsync(int skip = 0, int take = DocumentService.DefaultPageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a failed document back on the processing queue.
    /// </summary>
    Task<RequeueResult> RequeueAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Unassigned documents awaiting review from every owner, oldest first.</summary>
    Task<PagedResult<DocumentDto>> GetReviewQueueAsync(int skip = 0, int take = DocumentService.DefaultPageSize, CancellationToken cancellationToken = default);

    /// <summary>Assigns an unassigned document awaiting review to the current user.</summary>
    Task<AssignmentResult> ClaimAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Assigns a document awaiting review to <paramref name="userId"/>, or back to the queue when null (admins).</summary>
    Task<AssignmentResult> AssignAsync(Guid id, string? userId, CancellationToken cancellationToken = default);

    /// <summary>Re-queues a failed document the current user can access, to be processed with <paramref name="modelType"/>.</summary>
    Task<ModelSwitchResult> RetryWithModelAsync(Guid id, string? modelType, CancellationToken cancellationToken = default);

    /// <summary>Re-runs extraction for a document awaiting review; the new result becomes the next version.</summary>
    Task<ReprocessResult> ReprocessAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>One page of the documents the current user may see, newest first, filtered by status and file name.</summary>
    Task<DocumentPage> GetDocumentPageAsync(DocumentQuery query, CancellationToken cancellationToken = default);

    Task<DocumentFileDto?> GetDocumentFileAsync(Guid id, CancellationToken cancellationToken = default);
}

public enum AssignmentResult
{
    Assigned,
    NotFound,
    NotAwaitingReview,
    AssignedToSomeoneElse
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

public sealed record DocumentPage(IReadOnlyList<DocumentDto> Items, int TotalCount);

public enum ReprocessResult
{
    Queued,
    NotFound,
    NotReprocessable
}
