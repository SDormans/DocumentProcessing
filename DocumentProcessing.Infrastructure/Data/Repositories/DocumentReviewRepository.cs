using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DocumentProcessing.Infrastructure.Data.Repositories;

public class DocumentReviewRepository : IDocumentReviewRepository
{
    private readonly AppDbContext _dbContext;

    public DocumentReviewRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<DocumentReview?> GetByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        return _dbContext.DocumentReviews.FirstOrDefaultAsync(r => r.DocumentId == documentId, cancellationToken);
    }

    public Task<DocumentReview?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.DocumentReviews.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task AddAsync(DocumentReview review, CancellationToken cancellationToken = default)
    {
        await _dbContext.DocumentReviews.AddAsync(review, cancellationToken);
    }

    public Task UpdateAsync(DocumentReview review, CancellationToken cancellationToken = default)
    {
        _dbContext.DocumentReviews.Update(review);
        return Task.CompletedTask;
    }

    public async Task AddAuditEntryAsync(ReviewAuditEntry entry, CancellationToken cancellationToken = default)
    {
        await _dbContext.ReviewAuditEntries.AddAsync(entry, cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewAuditEntry>> GetAuditEntriesAsync(
        Guid documentReviewId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ReviewAuditEntries
            .Where(a => a.DocumentReviewId == documentReviewId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
