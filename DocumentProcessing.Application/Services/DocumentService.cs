using DocumentProcessing.Application.DTOs;
using DocumentProcessing.Application.Exceptions;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Application.Services;

public class DocumentService : IDocumentService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<DocumentService> _logger;
    private readonly DocumentServiceOptions _options;

    public DocumentService(
        IDocumentRepository documentRepository,
        IFileStorage fileStorage,
        ILogger<DocumentService> logger,
        IOptions<DocumentServiceOptions> options)
    {
        _documentRepository = documentRepository;
        _fileStorage = fileStorage;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<UploadDocumentResponse> UploadDocumentAsync(
        Stream fileStream,
        string originalFileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = ValidateFile(fileStream, originalFileName, contentType, _options.MaxFileSizeBytes).ToList();
        if (validationErrors.Count > 0)
        {
            _logger.LogWarning("Document validation failed for {FileName}: {Errors}",
                originalFileName, string.Join("; ", validationErrors));
            throw new ValidationException("Document upload validation failed.", validationErrors);
        }

        var contentHash = ComputeContentHash(fileStream);
        fileStream.Position = 0;

        var storageKey = GenerateStorageKey(originalFileName);

        string? storedStorageKey = null;
        try
        {
            storedStorageKey = await _fileStorage.StoreAsync(fileStream, storageKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store file {StorageKey}", storageKey);
            throw new StorageException("Failed to store uploaded file.", ex);
        }

        var document = Document.Create(
            originalFileName: originalFileName,
            contentType: contentType,
            fileSize: fileStream.Length,
            contentHash: contentHash,
            storageKey: storedStorageKey);

        try
        {
            await _documentRepository.AddAsync(document, cancellationToken);
            await _documentRepository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save document metadata for {StorageKey}. Attempting cleanup of stored file.", storedStorageKey);
            try
            {
                await _fileStorage.DeleteAsync(storedStorageKey, cancellationToken);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogError(cleanupEx, "Cleanup of orphaned file {StorageKey} failed after database error.", storedStorageKey);
            }
            throw new StorageException("Failed to save document metadata.", ex);
        }

        _logger.LogInformation("Document {DocumentId} uploaded successfully with storage key {StorageKey}",
            document.Id, storedStorageKey);

        return new UploadDocumentResponse
        {
            Id = document.Id,
            Status = document.Status.ToString(),
            CreatedAtUtc = document.CreatedAtUtc
        };
    }

    public async Task<DocumentDto?> GetDocumentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(id, cancellationToken);
        return document == null ? null : MapToDto(document);
    }

    public async Task<IReadOnlyList<DocumentDto>> GetDocumentsAsync(CancellationToken cancellationToken = default)
    {
        var documents = await _documentRepository.GetAllAsync(cancellationToken);
        return documents.Select(MapToDto).ToList();
    }

    public async Task<DocumentFileDto?> GetDocumentFileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(id, cancellationToken);
        if (document == null)
        {
            return null;
        }

        var stream = await _fileStorage.OpenReadAsync(document.StorageKey, cancellationToken);
        return new DocumentFileDto
        {
            Stream = stream,
            ContentType = document.ContentType,
            FileName = document.OriginalFileName
        };
    }

    private static IEnumerable<string> ValidateFile(Stream fileStream, string originalFileName, string contentType, long maxFileSizeBytes)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            yield return "File name is required.";
            yield break;
        }

        if (!originalFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Only PDF files are allowed.";
        }

        if (fileStream.Length == 0)
        {
            yield return "File cannot be empty.";
        }

        if (fileStream.Length > maxFileSizeBytes)
        {
            yield return $"File is too large. Maximum size is {maxFileSizeBytes / 1024 / 1024} MB.";
        }

        if (fileStream.Length > 0)
        {
            var pdfHeader = new byte[5];
            var bytesRead = fileStream.Read(pdfHeader, 0, 5);
            if (bytesRead < 5 || !IsPdfSignature(pdfHeader))
            {
                yield return "File is not a valid PDF document (invalid PDF signature).";
            }
        }
    }

    private static bool IsPdfSignature(byte[] header)
    {
        return header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 &&
               header[3] == 0x46 && header[4] == 0x2D;
    }

    private static string ComputeContentHash(Stream fileStream)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        fileStream.Position = 0;
        var hashBytes = sha256.ComputeHash(fileStream);
        return Convert.ToHexString(hashBytes);
    }

    private static string GenerateStorageKey(string originalFileName)
    {
        var fileId = Guid.NewGuid().ToString("N");
        var extension = Path.GetExtension(originalFileName)?.ToLowerInvariant() ?? ".pdf";
        return $"{fileId}{extension}";
    }

    private static DocumentDto MapToDto(Document document)
    {
        return new DocumentDto
        {
            Id = document.Id,
            OriginalFileName = document.OriginalFileName,
            ContentType = document.ContentType,
            FileSize = document.FileSize,
            ContentHash = document.ContentHash,
            StorageKey = document.StorageKey,
            Status = document.Status.ToString(),
            CreatedAtUtc = document.CreatedAtUtc
        };
    }
}

public class DocumentServiceOptions
{
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
}
