using DocumentProcessing.Application.Services;
using DocumentProcessing.Domain.Interfaces;
using DocumentProcessing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Infrastructure.Retention;

public class DocumentDeletion : IDocumentDeletion
{
    private readonly AppDbContext _dbContext;
    private readonly IFileStorage _fileStorage;
    private readonly RetentionOptions _options;
    private readonly ILogger<DocumentDeletion> _logger;

    public DocumentDeletion(
        AppDbContext dbContext,
        IFileStorage fileStorage,
        IOptions<RetentionOptions> options,
        ILogger<DocumentDeletion> logger)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> DeleteAsync(Guid documentId, string reason, CancellationToken cancellationToken = default)
    {
        var document = await _dbContext.Documents.FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);
        if (document is null)
            return false;

        // File first: if a later step fails, the row is still there and the delete can be retried.
        await _fileStorage.DeleteAsync(document.StorageKey, cancellationToken);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await _dbContext.DocumentTextExtractions.Where(t => t.DocumentId == documentId).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.QueueCommands.Where(c => c.DocumentId == documentId).ExecuteDeleteAsync(cancellationToken);
        // Extraction results, reviews and audit entries cascade from the document row.
        await _dbContext.Documents.Where(d => d.Id == documentId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // The id and reason only: no file name or invoice data, so this log line holds no personal data.
        _logger.LogInformation("Document {DocumentId} deleted ({Reason}).", documentId, reason);
        return true;
    }

    public async Task<int> PurgeExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var expired = await _dbContext.Documents
            .Where(d => d.RetainUntilUtc != null && d.RetainUntilUtc <= nowUtc)
            .OrderBy(d => d.RetainUntilUtc)
            .Select(d => d.Id)
            .Take(_options.PurgeBatchSize)
            .ToListAsync(cancellationToken);

        var deleted = 0;
        foreach (var id in expired)
        {
            if (await DeleteAsync(id, "RetentionExpired", cancellationToken))
                deleted++;
        }
        return deleted;
    }
}
