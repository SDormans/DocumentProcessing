using DocumentProcessing.Domain.Entities;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentProcessing.Infrastructure.Queue;

public interface IQueueCommandRepository
{
    Task<QueueCommand?> GetPendingAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<List<QueueCommand>> GetReadyForProcessingAsync(CancellationToken cancellationToken = default);
    Task AddAsync(QueueCommand command, CancellationToken cancellationToken = default);
    Task UpdateAsync(QueueCommand command, CancellationToken cancellationToken = default);
}