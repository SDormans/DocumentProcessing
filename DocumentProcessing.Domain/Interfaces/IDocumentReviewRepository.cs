using DocumentProcessing.Domain.Entities;

namespace DocumentProcessing.Domain.Interfaces;

public interface IDocumentReviewRepository
{
    Task<DocumentReview?> GetByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<DocumentReview?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(DocumentReview review, CancellationToken cancellationToken = default);
    Task UpdateAsync(DocumentReview review, CancellationToken cancellationToken = default);
    Task AddAuditEntryAsync(ReviewAuditEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReviewAuditEntry>> GetAuditEntriesAsync(Guid documentReviewId, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
