using DocumentProcessing.Application.Services.Invoice;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentProcessing.Infrastructure.Data.Configurations
{
    public class InvoiceExtractionResultConfiguration : IEntityTypeConfiguration<InvoiceExtractionResultEntity>
    {
        public void Configure(EntityTypeBuilder<InvoiceExtractionResultEntity> builder)
        {
            builder.ToTable("InvoiceExtractionResults");

            builder.HasKey(d => d.Id);
            builder.Property(d => d.Id).ValueGeneratedNever();
            builder.Property(d => d.DocumentId).IsRequired();
            builder.Property(d => d.ExtractionVersion).IsRequired();
            builder.Property(d => d.ExtractionMethod).IsRequired().HasMaxLength(50);
            builder.Property(d => d.RawAiResponse).HasMaxLength(4000);
            builder.Property(d => d.ErrorMessage).HasMaxLength(2000);
            builder.Property(d => d.LineItemsJson).HasMaxLength(10000);
            builder.Property(d => d.SupplierName).HasMaxLength(200);
            builder.Property(d => d.InvoiceNumber).HasMaxLength(100);
            builder.Property(d => d.Currency).HasMaxLength(10);
            builder.Property(d => d.ExtractedAtUtc).IsRequired();

            // Invoice-level fields
            builder.Property(d => d.SupplierNameHasValue).IsRequired();
            builder.Property(d => d.InvoiceNumberHasValue).IsRequired();
            builder.Property(d => d.InvoiceDate).IsRequired(false);
            builder.Property(d => d.DueDateHasValue).IsRequired();
            builder.Property(d => d.DueDate).IsRequired(false);
            builder.Property(d => d.CurrencyHasValue).IsRequired();
            builder.Property(d => d.SubtotalHasValue).IsRequired();
            builder.Property(d => d.Subtotal).HasColumnType("decimal(18,2)");
            builder.Property(d => d.TaxAmountHasValue).IsRequired();
            builder.Property(d => d.TaxAmount).HasColumnType("decimal(18,2)");
            builder.Property(d => d.TotalAmountHasValue).IsRequired();
            builder.Property(d => d.TotalAmount).HasColumnType("decimal(18,2)");

            // Warnings
            builder.Property(d => d.WarningsJson).HasMaxLength(4000);

            // Indexes
            builder.HasIndex(d => d.DocumentId);
            builder.HasIndex(d => new { d.DocumentId, d.ExtractionVersion }).IsUnique();

            // Relationship
            builder.HasOne(d => d.Document)
                .WithMany()
                .HasForeignKey(d => d.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}