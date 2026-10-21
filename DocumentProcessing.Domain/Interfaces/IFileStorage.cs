namespace DocumentProcessing.Domain.Interfaces;

public interface IFileStorage
{
    /// <summary>
    /// Stores a file stream and returns a storage key that can be used to retrieve or delete the file.
    /// </summary>
    Task<string> StoreAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the file identified by <paramref name="storageKey"/>.
    /// </summary>
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a read-only stream for the file identified by <paramref name="storageKey"/>.
    /// </summary>
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if a file with the given <paramref name="storageKey"/> exists.
    /// </summary>
    Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default);
}