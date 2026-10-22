using DocumentProcessing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentProcessing.Infrastructure.Data.Configurations;

public class ReviewAuditEntryConfiguration : IEntityTypeConfiguration<ReviewAuditEntry>
{
    public void Configure(EntityTypeBuilder<ReviewAuditEntry> builder)
    {
        builder.ToTable("ReviewAuditEntries");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.DocumentReviewId).IsRequired();
        builder.Property(d => d.UserId).IsRequired().HasMaxLength(200);
        builder.Property(d => d.UserName).IsRequired().HasMaxLength(200);
        builder.Property(d => d.ActionType).IsRequired().HasMaxLength(50);
        builder.Property(d => d.FieldName).HasMaxLength(200);
        builder.Property(d => d.OldValue).HasMaxLength(1000);
        builder.Property(d => d.NewValue).HasMaxLength(1000);
        builder.Property(d => d.CreatedAtUtc).IsRequired();

        builder.HasIndex(d => d.DocumentReviewId);
        builder.HasIndex(d => d.CreatedAtUtc);

        builder.HasOne(d => d.DocumentReview)
            .WithMany(d => d.AuditEntries)
            .HasForeignKey(d => d.DocumentReviewId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
