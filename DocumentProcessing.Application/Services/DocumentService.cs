using DocumentProcessing.Application.Services.Ai;
using DocumentProcessing.Application.DTOs;
using DocumentProcessing.Application.Exceptions;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Domain.Exceptions;
using DocumentProcessing.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Application.Services;

public class DocumentService : IDocumentService
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    private readonly IDocumentRepository _documentRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IProcessingQueue _processingQueue;
    private readonly ILogger<DocumentService> _logger;
    private readonly DocumentServiceOptions _options;
    private readonly ICurrentUser _currentUser;
    private readonly AiModelSettingsService? _aiModels;

    public DocumentService(
        IDocumentRepository documentRepository,
        IFileStorage fileStorage,
        IProcessingQueue processingQueue,
        ILogger<DocumentService> logger,
        IOptions<DocumentServiceOptions> options,
        ICurrentUser currentUser,
        AiModelSettingsService? aiModels = null)
    {
        _currentUser = currentUser;
        _aiModels = aiModels;
        _documentRepository = documentRepository;
        _fileStorage = fileStorage;
        _processingQueue = processingQueue;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<RequeueResult> RequeueAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(id, cancellationToken);
        if (document == null || !_currentUser.CanAccess(document))
        {
            return RequeueResult.NotFound;
        }
        if (document.Status != DocumentStatus.Failed)
        {
            return RequeueResult.NotFailed;
        }

        document.UpdateStatus(DocumentStatus.Queued);
        await _documentRepository.UpdateAsync(document, cancellationToken);
        await _processingQueue.EnqueueAsync(document.Id, cancellationToken);
        if (!await TrySaveRequeueAsync(document.Id, cancellationToken))
            return RequeueResult.NotFailed;
        _logger.LogInformation("Document {DocumentId} re-queued by {UserId}.", document.Id, _currentUser.UserId);
        return RequeueResult.Requeued;
    }

    /// <summary>Returns false when a concurrent request re-queued the document first.</summary>
    private async Task<bool> TrySaveRequeueAsync(Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            await _documentRepository.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (ConcurrencyConflictException)
        {
            _logger.LogInformation("Document {DocumentId} was already re-queued by a concurrent request.", documentId);
            return false;
        }
    }

    public async Task<ModelSwitchResult> RetryWithModelAsync(Guid id, string? modelType, CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(id, cancellationToken);
        if (document == null || !_currentUser.CanAccess(document))
            return ModelSwitchResult.NotFound;
        if (document.Status != DocumentStatus.Failed)
            return ModelSwitchResult.NotFailed;
        if (!AiModelSettingsService.TryParse(modelType, out var parsed))
            return ModelSwitchResult.InvalidModel;
        if (_aiModels is null || !_aiModels.IsEnabled(parsed))
            return ModelSwitchResult.ModelUnavailable;

        document.SelectAiModel(parsed);
        document.UpdateStatus(DocumentStatus.Queued);
        await _documentRepository.UpdateAsync(document, cancellationToken);
        await _processingQueue.EnqueueAsync(document.Id, cancellationToken);
        if (!await TrySaveRequeueAsync(document.Id, cancellationToken))
            return ModelSwitchResult.NotFailed;
        _logger.LogInformation("Document {DocumentId} re-queued with the {ModelType} model by {UserId}.", document.Id, parsed, _currentUser.UserId);
        return ModelSwitchResult.Requeued;
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
            var messages = validationErrors.Select(e => e.Message).ToList();
            _logger.LogWarning("Document upload validation failed: {Errors}", string.Join("; ", messages));
            var kind = validationErrors.Any(e => e.Kind == ValidationFailureKind.TooLarge) ? ValidationFailureKind.TooLarge
                : validationErrors.Any(e => e.Kind == ValidationFailureKind.UnsupportedMediaType) ? ValidationFailureKind.UnsupportedMediaType
                : ValidationFailureKind.Invalid;
            throw new ValidationException("Document upload validation failed.", messages, kind);
        }

        var contentHash = ComputeContentHash(fileStream);
        fileStream.Position = 0;

        var aiModelType = _aiModels is null ? (AiModelType?)null : await _aiModels.ResolveAsync(_currentUser.UserId, cancellationToken);
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
        if (aiModelType is { } selectedModel)
            document.SelectAiModel(selectedModel);

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

    public async Task<PagedResult<DocumentDto>> GetDocumentsAsync(int skip = 0, int take = DefaultPageSize, CancellationToken cancellationToken = default)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, MaxPageSize);
        PagedResult<Document> page;
        if (_currentUser.HasUnrestrictedDocumentAccess)
            page = await _documentRepository.GetPageAsync(null, skip, take, cancellationToken);
        else if (string.IsNullOrEmpty(_currentUser.UserId))
            page = new PagedResult<Document>([], 0);
        else
            page = await _documentRepository.GetPageAsync(_currentUser.UserId, skip, take, cancellationToken);

        return new PagedResult<DocumentDto>(page.Items.Select(MapToDto).ToList(), page.TotalCount);
    }

    public async Task<ReprocessResult> ReprocessAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await GetAccessibleDocumentAsync(id, cancellationToken);
        if (document is null)
            return ReprocessResult.NotFound;
        if (document.Status != DocumentStatus.NeedsReview)
            return ReprocessResult.NotReprocessable;

        document.UpdateStatus(DocumentStatus.Queued);
        await _processingQueue.EnqueueAsync(document.Id, cancellationToken);
        await _documentRepository.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Document {DocumentId} queued for reprocessing by {UserId}.", id, _currentUser.UserId);
        return ReprocessResult.Queued;
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
        if (document == null || document.Status == DocumentStatus.Quarantined)
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

    private static IEnumerable<(ValidationFailureKind Kind, string Message)> ValidateFile(Stream fileStream, string originalFileName, string contentType, long maxFileSizeBytes)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            yield return (ValidationFailureKind.Invalid, "File name is required.");
            yield break;
        }

        if (!originalFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            yield return (ValidationFailureKind.UnsupportedMediaType, "Only PDF files are allowed.");
        }

        if (!string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(contentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            yield return (ValidationFailureKind.UnsupportedMediaType, "Content type must be application/pdf or application/octet-stream.");
        }

        if (fileStream.Length == 0)
        {
            yield return (ValidationFailureKind.Invalid, "File cannot be empty.");
        }

        if (fileStream.Length > maxFileSizeBytes)
        {
            yield return (ValidationFailureKind.TooLarge, $"File is too large. Maximum size is {maxFileSizeBytes / 1024 / 1024} MB.");
        }

        if (fileStream.Length > 0)
        {
            var pdfHeader = new byte[5];
            var bytesRead = fileStream.Read(pdfHeader, 0, 5);
            if (bytesRead < 5 || !IsPdfSignature(pdfHeader))
            {
                yield return (ValidationFailureKind.UnsupportedMediaType, "File is not a valid PDF document (invalid PDF signature).");
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
            OwnerId = document.OwnerId,
            AiModelType = document.AiModelType.ToString()
        };
    }
}

public class DocumentServiceOptions
{
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
}
