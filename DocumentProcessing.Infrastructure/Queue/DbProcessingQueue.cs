using DocumentProcessing.Application.Services;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Infrastructure.Data;

namespace DocumentProcessing.Infrastructure.Queue;

/// <summary>
/// Adds a <see cref="QueueCommand"/> to the shared <see cref="AppDbContext"/> without saving,
/// so the caller's SaveChanges persists the document and its command in one transaction.
/// </summary>
public class DbProcessingQueue : IProcessingQueue
{
    private readonly AppDbContext _dbContext;

    public DbProcessingQueue(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task EnqueueAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        await _dbContext.QueueCommands.AddAsync(new QueueCommand(documentId), cancellationToken);
    }
}
