using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentProcessing.Infrastructure.Queue;

public class QueueCommandRepository : IQueueCommandRepository
{
    private readonly AppDbContext _dbContext;

    public QueueCommandRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<QueueCommand?> GetPendingAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.QueueCommands
            .FirstOrDefaultAsync(qc => qc.DocumentId == documentId && qc.Status == QueueCommandStatus.Pending, cancellationToken);
    }

    public async Task<List<QueueCommand>> GetReadyForProcessingAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.QueueCommands
            .Where(qc => qc.Status == QueueCommandStatus.Pending && qc.NextAttemptAtUtc <= DateTime.UtcNow)
            .Take(8) // Max parallelism
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<QueueCommand>> ClaimAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var leaseExpiresAt = now + leaseDuration;
        const int pending = (int)QueueCommandStatus.Pending;
        const int processing = (int)QueueCommandStatus.Processing;

        // FOR UPDATE SKIP LOCKED lets concurrent workers claim disjoint rows without blocking.
        return await _dbContext.QueueCommands
            .FromSql($@"
                UPDATE ""QueueCommands"" AS q
                SET ""Status"" = {processing},
                    ""AttemptCount"" = q.""AttemptCount"" + 1,
                    ""LastErrorCategory"" = NULL,
                    ""LeaseOwner"" = {leaseOwner},
                    ""LeaseExpiresAtUtc"" = {leaseExpiresAt}
                WHERE q.""Id"" IN (
                    SELECT ""Id"" FROM ""QueueCommands""
                    WHERE (""Status"" = {pending} AND ""NextAttemptAtUtc"" <= {now})
                       OR (""Status"" = {processing} AND ""LeaseExpiresAtUtc"" < {now})
                    ORDER BY ""NextAttemptAtUtc""
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED)
                RETURNING q.*")
            .AsTracking()
            .ToListAsync(cancellationToken);
    }

    public Task<int> PurgeCompletedAsync(DateTime completedBeforeUtc, CancellationToken cancellationToken = default) =>
        _dbContext.QueueCommands
            .Where(c => c.Status == QueueCommandStatus.Completed && c.CompletedAtUtc < completedBeforeUtc)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task AddAsync(QueueCommand command, CancellationToken cancellationToken = default)
    {
        await _dbContext.QueueCommands.AddAsync(command, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(QueueCommand command, CancellationToken cancellationToken = default)
    {
        _dbContext.QueueCommands.Update(command);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}