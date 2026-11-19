using System;
using DocumentProcessing.Domain.Enums;

namespace DocumentProcessing.Domain.Entities;

public enum DocumentStatus
{
    Uploaded,
    Queued,
    Processing,
    NeedsReview,
    Completed,
    Failed,
    // Stored as an integer: append new values, never reorder.
    Rejected,

    /// <summary>The malware scanner flagged the file; it is never processed or served.</summary>
    Quarantined
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
        DateTime createdAtUtc,
        string? ownerId = null)
    {
        OwnerId = ownerId;
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
    /// Identity user id of the uploader. Null for documents created before ownership was tracked;
    /// those are only visible to users with unrestricted document access.
    /// </summary>
    public string? OwnerId { get; private set; }

    /// <summary>
    /// Timestamp of the latest processing or review of this document in UTC.
    /// </summary>
    public DateTime? LastProcessedAtUtc { get; private set; }

    /// <summary>
    /// The model type the uploader chose. The worker extracts with this model unless fallback is enabled.
    /// </summary>
    public AiModelType AiModelType { get; private set; } = AiModelType.Cloud;

    /// <summary>
    /// When the document and everything derived from it is deleted. Set when the review completes;
    /// null means kept until deleted by an admin.
    /// </summary>
    public DateTime? RetainUntilUtc { get; private set; }

    public void SetRetention(DateTime retainUntilUtc)
    {
        RetainUntilUtc = retainUntilUtc;
    }

    public static Document Create(
        string originalFileName,
        string contentType,
        long fileSize,
        string contentHash,
        string storageKey,
        string? ownerId = null)
    {
        return new Document(
            Guid.NewGuid(),
            originalFileName,
            contentType,
            fileSize,
            contentHash,
            storageKey,
            DocumentStatus.Uploaded,
            DateTime.UtcNow,
            ownerId);
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

    public void SelectAiModel(AiModelType modelType)
    {
        if (!Enum.IsDefined(modelType))
            throw new ArgumentOutOfRangeException(nameof(modelType), modelType, "Unknown AI model type.");
        AiModelType = modelType;
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
        (DocumentStatus.Processing, DocumentStatus.Quarantined) => true,

        (DocumentStatus.NeedsReview, DocumentStatus.Completed) => true,
        (DocumentStatus.NeedsReview, DocumentStatus.Failed) => true,
        (DocumentStatus.NeedsReview, DocumentStatus.Rejected) => true,

        // Failed documents can be retried; Rejected is final.
        (DocumentStatus.Failed, DocumentStatus.Queued) => true,
        (DocumentStatus.Failed, DocumentStatus.Processing) => true,

        _ => false
    };
}