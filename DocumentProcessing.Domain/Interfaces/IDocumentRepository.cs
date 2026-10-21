using DocumentProcessing.Domain.Entities;

namespace DocumentProcessing.Domain.Interfaces;

public interface IDocumentRepository
{
    Task AddAsync(Document document, CancellationToken cancellationToken = default);
    Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Document>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpdateAsync(Document document, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}