using DocumentProcessing.Domain.Entities;

namespace DocumentProcessing.Domain.Interfaces;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount);

public interface IDocumentRepository
{
    Task AddAsync(Document document, CancellationToken cancellationToken = default);
    Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>Newest first; <paramref name="ownerId"/> null returns every owner's documents.</summary>
    Task<PagedResult<Document>> GetPageAsync(string? ownerId, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>Newest first, filtered by status and file name. <paramref name="ownerId"/> null means all owners.</summary>
    Task<(IReadOnlyList<Document> Items, int TotalCount)> QueryAsync(
        DocumentQuery query, string? ownerId, CancellationToken cancellationToken = default);
    Task UpdateAsync(Document document, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <param name="Search">Case-insensitive match on the file name.</param>
public sealed record DocumentQuery(DocumentStatus? Status, string? Search, int Skip, int Take);

