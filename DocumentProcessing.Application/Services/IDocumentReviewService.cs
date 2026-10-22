using DocumentProcessing.Application.DTOs;

namespace DocumentProcessing.Application.Services;

public interface IDocumentReviewService
{
    /// <summary>
    /// Haalt de review-detail voor een document op (inclusief velden, validaties en status).
    /// </summary>
    Task<DocumentDetailDto?> GetDocumentDetailAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Slaat menselijke correcties op. Originele AI-output wordt niet overschreven.
    /// Vereist de rol Reviewer of hoger.
    /// </summary>
    Task<ReviewActionResponse> SubmitCorrectionsAsync(
        Guid documentId,
        SubmitCorrectionsRequest request,
        string userId,
        string userName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Keurt een document goed. Alleen toegestaan als de bedrijfsregels dit toelaten
    /// (bijv. geen kritieke validatiefouten) en de gebruiker bevoegd is.
    /// </summary>
    Task<ReviewActionResponse> ApproveAsync(
        Guid documentId,
        ApproveRequest request,
        string userId,
        string userName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Wijst een document af. Vereist een reden en een bevoegde gebruiker.
    /// </summary>
    Task<ReviewActionResponse> RejectAsync(
        Guid documentId,
        RejectRequest request,
        string userId,
        string userName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Haalt de auditlog voor een documentreview op.
    /// </summary>
    Task<IReadOnlyList<ReviewAuditEntryDto>> GetAuditLogAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bepaalt of de ingelogde gebruiker een document mag beoordelen/corrigeren.
    /// </summary>
    Task<bool> CanReviewAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bepaalt of de ingelogde gebruiker een document mag goedkeuren of afwijzen.
    /// </summary>
    Task<bool> CanApproveOrRejectAsync(string userId, CancellationToken cancellationToken = default);
}
