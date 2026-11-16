using DocumentProcessing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentProcessing.Infrastructure.Data;

public class QueueCommandConfiguration : IEntityTypeConfiguration<QueueCommand>
{
    public void Configure(EntityTypeBuilder<QueueCommand> builder)
    {
        builder.ToTable("QueueCommands");
        builder.HasKey(qc => qc.Id);

        builder.Property(qc => qc.Id)
            .ValueGeneratedNever();

        builder.Property(qc => qc.DocumentId)
            .IsRequired();

        builder.Property(qc => qc.Status)
            .IsRequired();

        builder.Property(qc => qc.AttemptCount)
            .IsRequired();

        builder.Property(qc => qc.CreatedAtUtc)
            .IsRequired();

        builder.Property(qc => qc.LastErrorCategory)
            .HasMaxLength(100);

        builder.Property(qc => qc.NextAttemptAtUtc)
            .IsRequired();

        builder.Property(qc => qc.CompletedAtUtc)
            .IsRequired(false);

        // Concurrency token: a worker whose lease was taken over cannot overwrite the new owner's state.
        builder.Property(qc => qc.LeaseOwner)
            .HasMaxLength(200)
            .IsConcurrencyToken();

        builder.Property(qc => qc.LeaseExpiresAtUtc)
            .IsRequired(false);

        builder.HasIndex(qc => new { qc.Status, qc.NextAttemptAtUtc });

        // At most one active (Pending/Processing) command per document.
        builder.HasIndex(qc => qc.DocumentId)
            .IsUnique()
            .HasFilter("\"Status\" IN (0, 1)")
            .HasDatabaseName("IX_QueueCommands_DocumentId_Active");

        builder.HasOne(qc => qc.Document)
            .WithMany()
            .HasForeignKey(qc => qc.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}