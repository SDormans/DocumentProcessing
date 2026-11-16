using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Interfaces;
using DocumentProcessing.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
        // A review created in this unit of work is already tracked as Added; Update() would turn it into an UPDATE.
        if (_dbContext.Entry(review).State == EntityState.Detached)
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

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("The review was changed by another request.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConcurrencyConflictException("A review for this document was created by another request.", ex);
        }
    }
}
