using DocumentProcessing.Application.Services;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Interfaces;
using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Infrastructure.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace DocumentProcessing.Infrastructure.Data;

public class AppDbContext : DbContext, IDocumentRepository, IInvoiceResultRepository, IDocumentReviewRepository
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentTextExtraction> DocumentTextExtractions => Set<DocumentTextExtraction>();
    public DbSet<InvoiceExtractionResultEntity> InvoiceExtractionResults => Set<InvoiceExtractionResultEntity>();
    public DbSet<DocumentReview> DocumentReviews => Set<DocumentReview>();
    public DbSet<ReviewAuditEntry> ReviewAuditEntries => Set<ReviewAuditEntry>();
public DbSet<QueueCommand> QueueCommands => Set<QueueCommand>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new DocumentConfiguration());
        modelBuilder.ApplyConfiguration(new DocumentTextExtractionConfiguration());
        modelBuilder.ApplyConfiguration(new InvoiceExtractionResultConfiguration());
        modelBuilder.ApplyConfiguration(new DocumentReviewConfiguration());
        modelBuilder.ApplyConfiguration(new ReviewAuditEntryConfiguration());
        modelBuilder.ApplyConfiguration(new QueueCommandConfiguration());
    }

    public async Task AddAsync(Document document, CancellationToken cancellationToken = default)
    {
        await Documents.AddAsync(document, cancellationToken);
    }

    public Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Document>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await Documents
            .OrderByDescending(d => d.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public Task UpdateAsync(Document document, CancellationToken cancellationToken = default)
    {
        Documents.Update(document);
        return Task.CompletedTask;
    }

    public new Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return base.SaveChangesAsync(cancellationToken);
    }

    public Task<InvoiceExtractionResultEntity?> GetByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        return InvoiceExtractionResults.FirstOrDefaultAsync(r => r.DocumentId == documentId, cancellationToken);
    }
    // IDocumentReviewRepository implementation (explicit interface to avoid signature clash with IDocumentRepository)
    Task<DocumentReview?> IDocumentReviewRepository.GetByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken)
    {
        return DocumentReviews.FirstOrDefaultAsync(d => d.DocumentId == documentId, cancellationToken);
    }

    Task<DocumentReview?> IDocumentReviewRepository.GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return DocumentReviews.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    Task IDocumentReviewRepository.AddAsync(DocumentReview review, CancellationToken cancellationToken)
    {
        return DocumentReviews.AddAsync(review, cancellationToken).AsTask();
    }

    Task IDocumentReviewRepository.UpdateAsync(DocumentReview review, CancellationToken cancellationToken)
    {
        DocumentReviews.Update(review);
        return Task.CompletedTask;
    }

    Task IDocumentReviewRepository.AddAuditEntryAsync(ReviewAuditEntry entry, CancellationToken cancellationToken)
    {
        return ReviewAuditEntries.AddAsync(entry, cancellationToken).AsTask();
    }

    async Task<IReadOnlyList<ReviewAuditEntry>> IDocumentReviewRepository.GetAuditEntriesAsync(Guid documentReviewId, CancellationToken cancellationToken)
    {
        return await ReviewAuditEntries
            .Where(a => a.DocumentReviewId == documentReviewId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }
}