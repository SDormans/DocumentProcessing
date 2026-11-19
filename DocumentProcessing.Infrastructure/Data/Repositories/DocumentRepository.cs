using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Interfaces;
using DocumentProcessing.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DocumentProcessing.Infrastructure.Data.Repositories;

public class DocumentRepository : IDocumentRepository
{
    private readonly AppDbContext _dbContext;

    public DocumentRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(Document document, CancellationToken cancellationToken = default)
    {
        await _dbContext.Documents.AddAsync(document, cancellationToken);
    }

    public Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Documents.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<PagedResult<Document>> GetPageAsync(string? ownerId, int skip, int take, CancellationToken cancellationToken = default)
    {
        var (items, total) = await QueryAsync(new DocumentQuery(null, null, skip, take), ownerId, cancellationToken);
        return new PagedResult<Document>(items, total);
    }

    public async Task<(IReadOnlyList<Document> Items, int TotalCount)> QueryAsync(
        DocumentQuery query, string? ownerId, CancellationToken cancellationToken = default)
    {
        var documents = ownerId is null ? _dbContext.Documents : _dbContext.Documents.Where(d => d.OwnerId == ownerId);
        if (query.Status is { } status)
            documents = documents.Where(d => d.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLower();
            documents = documents.Where(d => d.OriginalFileName.ToLower().Contains(search));
        }

        var total = await documents.CountAsync(cancellationToken);
        var items = await documents
            .OrderByDescending(d => d.CreatedAtUtc)
            .ThenByDescending(d => d.Id)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task UpdateAsync(Document document, CancellationToken cancellationToken = default)
    {
        _dbContext.Documents.Update(document);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // E.g. two requests queueing the same document: only one active queue command is allowed.
            throw new ConcurrencyConflictException("The document was changed by another request.", ex);
        }
    }
}
