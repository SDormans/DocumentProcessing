using DocumentProcessing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentProcessing.Infrastructure.Data.Configurations;

public class DocumentReviewConfiguration : IEntityTypeConfiguration<DocumentReview>
{
    public void Configure(EntityTypeBuilder<DocumentReview> builder)
    {
        builder.ToTable("DocumentReviews");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.DocumentId).IsRequired();
        builder.Property(d => d.OriginalAiOutputJson).IsRequired().HasMaxLength(4000);
        builder.Property(d => d.CorrectedValuesJson).HasMaxLength(4000);
        builder.Property(d => d.ApprovedValuesJson).HasMaxLength(4000);
        builder.Property(d => d.ReviewedBy).HasMaxLength(200);
        builder.Property(d => d.ReviewDecision).HasMaxLength(50);
        builder.Property(d => d.RejectionReason).HasMaxLength(2000);
        builder.Property(d => d.CreatedAtUtc).IsRequired();

        builder.HasIndex(d => d.DocumentId);
        builder.HasIndex(d => d.Status);

        builder.HasOne(d => d.Document)
            .WithMany()
            .HasForeignKey(d => d.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
