using DocumentProcessing.Application.DTOs;

namespace DocumentProcessing.Application.Services;

public interface IDocumentReviewService
{
    /// <summary>
    /// Returns the review detail of a document (fields, validation results and status).
    /// </summary>
    Task<DocumentDetailDto?> GetDocumentDetailAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves human corrections. The original AI output is not overwritten.
    /// Vereist de rol Reviewer of hoger.
    /// </summary>
    Task<ReviewActionResponse> SubmitCorrectionsAsync(
        Guid documentId,
        SubmitCorrectionsRequest request,
        string userId,
        string userName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves a document. Only allowed when the business rules allow it
    /// (no validation errors) and the user is authorized.
    /// </summary>
    Task<ReviewActionResponse> ApproveAsync(
        Guid documentId,
        ApproveRequest request,
        string userId,
        string userName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects a document. Requires a reason and an authorized user.
    /// </summary>
    Task<ReviewActionResponse> RejectAsync(
        Guid documentId,
        RejectRequest request,
        string userId,
        string userName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the audit log of a document review; null when the document does not exist or is not accessible.
    /// </summary>
    Task<IReadOnlyList<ReviewAuditEntryDto>?> GetAuditLogAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the signed-in user may review and correct documents.
    /// </summary>
    Task<bool> CanReviewAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the signed-in user may approve or reject documents.
    /// </summary>
    Task<bool> CanApproveOrRejectAsync(string userId, CancellationToken cancellationToken = default);
}
