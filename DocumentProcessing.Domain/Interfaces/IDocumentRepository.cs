using DocumentProcessing.Domain.Entities;

namespace DocumentProcessing.Domain.Interfaces;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount);

public interface IDocumentRepository
{
    Task AddAsync(Document document, CancellationToken cancellationToken = default);
    Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>Newest first; <paramref name="ownerId"/> null returns every owner's documents.</summary>
    Task<PagedResult<Document>> GetPageAsync(string? ownerId, int skip, int take, CancellationToken cancellationToken = default);
    Task UpdateAsync(Document document, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}