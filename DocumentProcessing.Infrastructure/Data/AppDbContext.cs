using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Infrastructure.Data.Configurations;
using DocumentProcessing.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DocumentProcessing.Infrastructure.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
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
}
