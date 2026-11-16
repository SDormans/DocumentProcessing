using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DocumentProcessing.Application.Exceptions;
using DocumentProcessing.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace DocumentProcessing.Infrastructure.Storage;

/// <summary>
/// Stores documents as blobs in one Azure Blob Storage container; the storage key is the blob name.
/// </summary>
public sealed class AzureBlobFileStorage : IFileStorage
{
    private readonly BlobContainerClient _container;
    private readonly bool _createContainerIfNotExists;
    private readonly ILogger<AzureBlobFileStorage> _logger;
    private readonly SemaphoreSlim _containerLock = new(1, 1);
    private volatile bool _containerEnsured;

    public AzureBlobFileStorage(BlobContainerClient container, bool createContainerIfNotExists, ILogger<AzureBlobFileStorage> logger)
    {
        _container = container;
        _createContainerIfNotExists = createContainerIfNotExists;
        _logger = logger;
    }

    public async Task<string> StoreAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        var blob = GetBlob(fileName);
        await EnsureContainerAsync(cancellationToken);

        try
        {
            await blob.UploadAsync(fileStream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = GetContentType(fileName) },
                // Storage keys are generated and unique; never silently replace an existing document.
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
            }, cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to store document in blob storage (status {Status}, code {ErrorCode}).", ex.Status, ex.ErrorCode);
            throw new StorageException("Failed to store document.", ex);
        }

        _logger.LogInformation("Document stored in blob storage.");
        return fileName;
    }

    public async Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        try
        {
            if (await GetBlob(storageKey).DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken))
            {
                _logger.LogInformation("Stored document deleted.");
            }
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to delete stored document (status {Status}).", ex.Status);
            throw new StorageException("Failed to delete stored document.", ex);
        }
    }

    public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        try
        {
            // Seekable, range-reading stream: PDF parsing needs random access, which a plain download stream lacks.
            return await GetBlob(storageKey).OpenReadAsync(new BlobOpenReadOptions(allowModifications: false), cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new FileNotFoundException("Stored document was not found.", ex);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to read stored document (status {Status}).", ex.Status);
            throw new StorageException("Failed to read stored document.", ex);
        }
    }

    public async Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetBlob(storageKey).ExistsAsync(cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            throw new StorageException("Failed to check stored document.", ex);
        }
    }

    internal BlobClient GetBlob(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException("Storage key cannot be null or empty.", nameof(storageKey));
        }

        if (storageKey.StartsWith('/') || storageKey.Contains('\\') ||
            storageKey.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new StorageException("Invalid storage key.");
        }

        return _container.GetBlobClient(storageKey);
    }

    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (!_createContainerIfNotExists || _containerEnsured)
        {
            return;
        }

        await _containerLock.WaitAsync(cancellationToken);
        try
        {
            if (!_containerEnsured)
            {
                await _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
                _containerEnsured = true;
            }
        }
        catch (RequestFailedException ex)
        {
            throw new StorageException("Failed to create blob container.", ex);
        }
        finally
        {
            _containerLock.Release();
        }
    }

    private static string GetContentType(string storageKey) =>
        storageKey.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf" : "application/octet-stream";
}
