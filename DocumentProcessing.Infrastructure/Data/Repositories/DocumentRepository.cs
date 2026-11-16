using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

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

    public async Task<IReadOnlyList<Document>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Documents
            .OrderByDescending(d => d.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public Task UpdateAsync(Document document, CancellationToken cancellationToken = default)
    {
        _dbContext.Documents.Update(document);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
