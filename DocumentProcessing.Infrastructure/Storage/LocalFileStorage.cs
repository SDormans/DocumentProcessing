using DocumentProcessing.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DocumentProcessing.Infrastructure.Storage;

public class LocalFileStorage : IFileStorage
{
    private readonly string _basePath;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(ILogger<LocalFileStorage> logger, IConfiguration configuration)
    {
        _logger = logger;
        _basePath = configuration["FileStorage:BasePath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "data", "storage");
        Directory.CreateDirectory(_basePath);
    }

    public async Task<string> StoreAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var storageKey = fileName;
        var fullPath = GetFullPath(storageKey);

        // Ensure directory exists
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        try
        {
            using var fileStreamOut = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await fileStream.CopyToAsync(fileStreamOut, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store document in local file storage.");
            throw new Application.Exceptions.StorageException("Failed to store document.", ex);
        }

        _logger.LogInformation("Document stored in local file storage.");
        return storageKey;
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(storageKey);
        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _logger.LogInformation("Stored document deleted.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete stored document.");
            throw new Application.Exceptions.StorageException("Failed to delete stored document.", ex);
        }
        return Task.CompletedTask;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(storageKey);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Stored document was not found.");
        }
        return Task.FromResult<Stream>(new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read));
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(File.Exists(GetFullPath(storageKey)));
    }

    private string GetFullPath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException("Storage key cannot be null or empty.", nameof(storageKey));
        }

        var fullBasePath = Path.GetFullPath(_basePath);
        var combinedPath = Path.GetFullPath(Path.Combine(fullBasePath, storageKey));

        var relativePath = Path.GetRelativePath(fullBasePath, combinedPath);
        if (Path.IsPathRooted(relativePath) ||
            relativePath.Equals("..", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new Application.Exceptions.StorageException("Invalid storage key: directory traversal attempted.");
        }

        return combinedPath;
    }
}