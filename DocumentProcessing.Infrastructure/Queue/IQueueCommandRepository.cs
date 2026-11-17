using DocumentProcessing.Domain.Entities;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentProcessing.Infrastructure.Queue;

public interface IQueueCommandRepository
{
    Task<QueueCommand?> GetPendingAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<List<QueueCommand>> GetReadyForProcessingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically leases up to <paramref name="batchSize"/> commands that are due (Pending) or whose
    /// lease expired (Processing), incrementing their attempt count. Safe to call from many workers.
    /// </summary>
    Task<IReadOnlyList<QueueCommand>> ClaimAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        int batchSize,
        CancellationToken cancellationToken = default);
    /// <summary>Deletes completed commands finished before <paramref name="completedBeforeUtc"/>; returns the count.</summary>
    Task<int> PurgeCompletedAsync(DateTime completedBeforeUtc, CancellationToken cancellationToken = default);
    Task AddAsync(QueueCommand command, CancellationToken cancellationToken = default);
    Task UpdateAsync(QueueCommand command, CancellationToken cancellationToken = default);
}