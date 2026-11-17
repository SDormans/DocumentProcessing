using DocumentProcessing.Application.DTOs;
using DocumentProcessing.Application.Exceptions;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Application.Services;

public class DocumentService : IDocumentService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IProcessingQueue _processingQueue;
    private readonly ILogger<DocumentService> _logger;
    private readonly DocumentServiceOptions _options;
    private readonly ICurrentUser _currentUser;

    public DocumentService(
        IDocumentRepository documentRepository,
        IFileStorage fileStorage,
        IProcessingQueue processingQueue,
        ILogger<DocumentService> logger,
        IOptions<DocumentServiceOptions> options,
        ICurrentUser currentUser)
    {
        _currentUser = currentUser;
        _documentRepository = documentRepository;
        _fileStorage = fileStorage;
        _processingQueue = processingQueue;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<UploadDocumentResponse> UploadDocumentAsync(
        Stream fileStream,
        string originalFileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var safeFileName = GetSafeFileName(originalFileName);
        var validationErrors = ValidateFile(fileStream, safeFileName, contentType, _options.MaxFileSizeBytes).ToList();
        if (validationErrors.Count > 0)
        {
            _logger.LogWarning("Document upload validation failed: {Errors}", string.Join("; ", validationErrors));
            throw new ValidationException("Document upload validation failed.", validationErrors);
        }

        var contentHash = ComputeContentHash(fileStream);
        fileStream.Position = 0;

        var storageKey = GenerateStorageKey();

        string? storedStorageKey = null;
        try
        {
            storedStorageKey = await _fileStorage.StoreAsync(fileStream, storageKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store uploaded document.");
            throw new StorageException("Failed to store uploaded file.", ex);
        }

        var document = Document.Create(
            originalFileName: safeFileName,
            contentType: contentType,
            fileSize: fileStream.Length,
            contentHash: contentHash,
            storageKey: storedStorageKey,
            ownerId: _currentUser.UserId);

        try
        {
            document.UpdateStatus(DocumentStatus.Queued);
            await _documentRepository.AddAsync(document, cancellationToken);
            await _processingQueue.EnqueueAsync(document.Id, cancellationToken);
            await _documentRepository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save document metadata. Attempting stored file cleanup.");
            try
            {
                await _fileStorage.DeleteAsync(storedStorageKey, cancellationToken);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogError(cleanupEx, "Cleanup of an orphaned document failed after a database error.");
            }
            throw new StorageException("Failed to save document metadata.", ex);
        }

        _logger.LogInformation("Document {DocumentId} uploaded and queued for processing.", document.Id);

        return new UploadDocumentResponse
        {
            Id = document.Id,
            Status = document.Status.ToString(),
            CreatedAtUtc = document.CreatedAtUtc
        };
    }

    public async Task<DocumentDto?> GetDocumentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await GetAccessibleDocumentAsync(id, cancellationToken);
        return document == null ? null : MapToDto(document);
    }

    public async Task<IReadOnlyList<DocumentDto>> GetDocumentsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Document> documents;
        if (_currentUser.HasUnrestrictedDocumentAccess)
            documents = await _documentRepository.GetAllAsync(cancellationToken);
        else if (string.IsNullOrEmpty(_currentUser.UserId))
            documents = [];
        else
            documents = await _documentRepository.GetByOwnerAsync(_currentUser.UserId, cancellationToken);

        return documents.Select(MapToDto).ToList();
    }

    public async Task<DocumentPage> GetDocumentPageAsync(DocumentQuery query, CancellationToken cancellationToken = default)
    {
        string? ownerId = null;
        if (!_currentUser.HasUnrestrictedDocumentAccess)
        {
            if (string.IsNullOrEmpty(_currentUser.UserId))
                return new DocumentPage([], 0);
            ownerId = _currentUser.UserId;
        }

        var (items, total) = await _documentRepository.QueryAsync(query, ownerId, cancellationToken);
        return new DocumentPage(items.Select(MapToDto).ToList(), total);
    }

    public async Task<DocumentFileDto?> GetDocumentFileAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await GetAccessibleDocumentAsync(id, cancellationToken);
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

    /// <summary>
    /// Returns null both for missing documents and for documents the user may not see, so ids of other users' documents are not disclosed.
    /// </summary>
    private async Task<Document?> GetAccessibleDocumentAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await _documentRepository.GetByIdAsync(id, cancellationToken);
        if (document != null && !_currentUser.CanAccess(document))
        {
            _logger.LogWarning("User {UserId} was denied access to document {DocumentId}.", _currentUser.UserId, id);
            return null;
        }
        return document;
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

        if (!string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(contentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Content type must be application/pdf or application/octet-stream.";
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

    private static string GetSafeFileName(string originalFileName)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            return string.Empty;
        }

        var fileName = Path.GetFileName(originalFileName.Replace('\\', '/'));
        return new string(fileName.Where(character => !char.IsControl(character)).ToArray());
    }

    private static string GenerateStorageKey()
    {
        return $"{Guid.NewGuid():N}.pdf";
    }

    private static DocumentDto MapToDto(Document document)
    {
        return new DocumentDto
        {
            Id = document.Id,
            OriginalFileName = document.OriginalFileName,
            ContentType = document.ContentType,
            FileSize = document.FileSize,
            Status = document.Status.ToString(),
            CreatedAtUtc = document.CreatedAtUtc,
            OwnerId = document.OwnerId
        };
    }
}

public class DocumentServiceOptions
{
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
}
