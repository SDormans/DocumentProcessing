using DocumentProcessing.Domain.Enums;

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
        NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(1);
    }

    public void MarkProcessing()
    {
        Status = QueueCommandStatus.Processing;
        AttemptCount++;
        LastErrorCategory = null;
    }

    public void MarkCompleted()
    {
        Status = QueueCommandStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void MarkFailed(string errorCategory)
    {
        Status = QueueCommandStatus.Failed;
        LastErrorCategory = errorCategory;
    }

    public void SetNextAttempt(TimeSpan delay)
    {
        NextAttemptAtUtc = DateTime.UtcNow + delay;
    }
}