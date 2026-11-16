using DocumentProcessing.Domain.Enums;
using System;

namespace DocumentProcessing.Domain.Entities;

public class QueueCommand
{
    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public QueueCommandStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string? LastErrorCategory { get; private set; }
    public DateTime? NextAttemptAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresAtUtc { get; private set; }

    public Document? Document { get; private set; }

    private QueueCommand()
    {
        // Required by EF Core
    }

    public QueueCommand(Guid documentId)
    {
        Id = Guid.NewGuid();
        DocumentId = documentId;
        Status = QueueCommandStatus.Pending;
        AttemptCount = 0;
        CreatedAtUtc = DateTime.UtcNow;
        LastErrorCategory = null;
        NextAttemptAtUtc = DateTime.UtcNow;
    }

    public void MarkProcessing()
    {
        Status = QueueCommandStatus.Processing;
        AttemptCount++;
        LastErrorCategory = null;
    }

    /// <summary>
    /// In-memory equivalent of the atomic claim done by the queue repository.
    /// </summary>
    public void Claim(string leaseOwner, TimeSpan leaseDuration)
    {
        MarkProcessing();
        LeaseOwner = leaseOwner;
        LeaseExpiresAtUtc = DateTime.UtcNow + leaseDuration;
    }

    public void RenewLease(TimeSpan leaseDuration)
    {
        LeaseExpiresAtUtc = DateTime.UtcNow + leaseDuration;
    }

    public void MarkCompleted()
    {
        Status = QueueCommandStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
        ReleaseLease();
    }

    public void MarkFailed(string errorCategory)
    {
        Status = QueueCommandStatus.Failed;
        LastErrorCategory = errorCategory;
        CompletedAtUtc = DateTime.UtcNow;
        ReleaseLease();
    }

    public void RecordRetry(string errorCategory, TimeSpan delay)
    {
        Status = QueueCommandStatus.Pending;
        LastErrorCategory = errorCategory;
        NextAttemptAtUtc = DateTime.UtcNow + delay;
        ReleaseLease();
    }

    private void ReleaseLease()
    {
        LeaseOwner = null;
        LeaseExpiresAtUtc = null;
    }

    public void SetNextAttempt(TimeSpan delay)
    {
        NextAttemptAtUtc = DateTime.UtcNow + delay;
    }
}