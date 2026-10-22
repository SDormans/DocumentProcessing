using DocumentProcessing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentProcessing.Infrastructure.Data.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("Documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.OriginalFileName).IsRequired().HasMaxLength(500);
        builder.Property(d => d.ContentType).IsRequired().HasMaxLength(200);
        builder.Property(d => d.ContentHash).IsRequired().HasMaxLength(128);
        builder.Property(d => d.StorageKey).IsRequired().HasMaxLength(500);
        builder.Property(d => d.Status).IsRequired();
        builder.Property(d => d.CreatedAtUtc).IsRequired();
        builder.Property(d => d.FileSize).IsRequired();

        builder.HasIndex(d => d.CreatedAtUtc);
        builder.HasIndex(d => d.Status);
    }
}