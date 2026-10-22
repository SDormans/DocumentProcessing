using System;

namespace DocumentProcessing.Domain.Entities;

public enum DocumentStatus
{
    Uploaded,
    Queued,
    Processing,
    NeedsReview,
    Completed,
    Failed
}

public class Document
{
    private Document()
    {
        // Required by EF Core
    }

    public Document(
        Guid id,
        string originalFileName,
        string contentType,
        long fileSize,
        string contentHash,
        string storageKey,
        DocumentStatus status,
        DateTime createdAtUtc)
    {
        Id = id;
        OriginalFileName = originalFileName;
        ContentType = contentType;
        FileSize = fileSize;
        ContentHash = contentHash;
        StorageKey = storageKey;
        Status = status;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long FileSize { get; private set; }
    public string ContentHash { get; private set; } = string.Empty;
    public string StorageKey { get; private set; } = string.Empty;
    public DocumentStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Timestamp of the latest processing or review of this document in UTC.
    /// </summary>
    public DateTime? LastProcessedAtUtc { get; private set; }

    public static Document Create(
        string originalFileName,
        string contentType,
        long fileSize,
        string contentHash,
        string storageKey)
    {
        return new Document(
            Guid.NewGuid(),
            originalFileName,
            contentType,
            fileSize,
            contentHash,
            storageKey,
            DocumentStatus.Uploaded,
            DateTime.UtcNow);
    }

    public void UpdateStatus(DocumentStatus newStatus)
    {
        if (Status == newStatus)
        {
            return;
        }

        if (!IsValidStatusTransition(Status, newStatus))
        {
            throw new InvalidOperationException($"Invalid document status transition from '{Status}' to '{newStatus}'.");
        }

        Status = newStatus;
    }

    public void MarkProcessed()
    {
        LastProcessedAtUtc = DateTime.UtcNow;
    }

    private static bool IsValidStatusTransition(DocumentStatus current, DocumentStatus next) => (current, next) switch
    {
        (DocumentStatus.Uploaded, DocumentStatus.Queued) => true,
        (DocumentStatus.Uploaded, DocumentStatus.Processing) => true,
        (DocumentStatus.Uploaded, DocumentStatus.Failed) => true,

        (DocumentStatus.Queued, DocumentStatus.Processing) => true,
        (DocumentStatus.Queued, DocumentStatus.Failed) => true,

        (DocumentStatus.Processing, DocumentStatus.NeedsReview) => true,
        (DocumentStatus.Processing, DocumentStatus.Completed) => true,
        (DocumentStatus.Processing, DocumentStatus.Failed) => true,

        (DocumentStatus.NeedsReview, DocumentStatus.Completed) => true,
        (DocumentStatus.NeedsReview, DocumentStatus.Failed) => true,

        // Allow reprocessing retry from Failed
        (DocumentStatus.Failed, DocumentStatus.Queued) => true,
        (DocumentStatus.Failed, DocumentStatus.Processing) => true,

        _ => false
    };
}