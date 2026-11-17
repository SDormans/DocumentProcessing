using DocumentProcessing.Domain.Entities;

namespace DocumentProcessing.Domain.Interfaces;

public interface IDocumentRepository
{
    Task AddAsync(Document document, CancellationToken cancellationToken = default);
    Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Document>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Document>> GetByOwnerAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Newest first. <paramref name="ownerId"/> null means all owners.</summary>
    Task<(IReadOnlyList<Document> Items, int TotalCount)> QueryAsync(
        DocumentQuery query, string? ownerId, CancellationToken cancellationToken = default);
    Task UpdateAsync(Document document, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <param name="Search">Case-insensitive match on the file name.</param>
public sealed record DocumentQuery(DocumentStatus? Status, string? Search, int Skip, int Take);

