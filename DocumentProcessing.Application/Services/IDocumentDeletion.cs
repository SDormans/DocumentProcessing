namespace DocumentProcessing.Application.Services;

public class RetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>How long a completed (approved) document is kept. Default 7 years (Dutch bookkeeping rule).</summary>
    public TimeSpan CompletedDocumentRetention { get; set; } = TimeSpan.FromDays(7 * 365 + 2);

    /// <summary>How often the worker deletes documents past their <c>RetainUntilUtc</c>.</summary>
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromHours(1);

    public int PurgeBatchSize { get; set; } = 100;
}

/// <summary>
/// Deletes a document everywhere: the stored file, then text extractions, queue commands,
/// extraction results, reviews and audit entries, then the document row.
/// </summary>
public interface IDocumentDeletion
{
    /// <returns>False when the document does not exist.</returns>
    Task<bool> DeleteAsync(Guid documentId, string reason, CancellationToken cancellationToken = default);

    /// <returns>The number of documents deleted because their retention period ended.</returns>
    Task<int> PurgeExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
