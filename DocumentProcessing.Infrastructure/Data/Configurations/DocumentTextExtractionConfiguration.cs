using DocumentProcessing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentProcessing.Infrastructure.Data.Configurations;

public class DocumentTextExtractionConfiguration : IEntityTypeConfiguration<DocumentTextExtraction>
{
    public void Configure(EntityTypeBuilder<DocumentTextExtraction> builder)
    {
        builder.ToTable("DocumentTextExtractions");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.DocumentId).IsRequired();
        builder.Property(d => d.ExtractionVersion).IsRequired();
        builder.Property(d => d.ExtractionMethod).IsRequired().HasMaxLength(50);
        builder.Property(d => d.FullText).IsRequired();
        builder.Property(d => d.QualityJson).HasMaxLength(2000);
        builder.Property(d => d.WarningsJson).HasMaxLength(4000);
        builder.Property(d => d.ExtractedAtUtc).IsRequired();

        builder.HasIndex(d => d.DocumentId);
        builder.HasIndex(d => new { d.DocumentId, d.ExtractionVersion }).IsUnique();
    }
}