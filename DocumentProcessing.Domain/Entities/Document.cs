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

    public void UpdateStatus(DocumentStatus status)
    {
        Status = status;
    }
}