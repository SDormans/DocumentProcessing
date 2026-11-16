namespace DocumentProcessing.Application.Services;

/// <summary>
/// Schedules documents for background processing. Implementations stage the command in the
/// current unit of work so it is committed atomically with the document metadata.
/// </summary>
public interface IProcessingQueue
{
    Task EnqueueAsync(Guid documentId, CancellationToken cancellationToken = default);
}
