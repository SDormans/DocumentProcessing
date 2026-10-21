using DocumentProcessing.Application.DTOs.TextExtraction;
using DocumentProcessing.Application.Exceptions;
using DocumentProcessing.Application.Exceptions.TextExtraction;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Domain.Interfaces;
using DocumentProcessing.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DocumentProcessing.Application.Services.TextExtraction;

public class DocumentTextExtractionService : IDocumentTextExtractionService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IDocumentTextExtractor _textExtractor;
    private readonly ITextExtractionDetector _textDetector;
    private readonly ILogger<DocumentTextExtractionService> _logger;
    private readonly TextExtractionOptions _options;

    public DocumentTextExtractionService(
        IDocumentRepository documentRepository,
        IFileStorage fileStorage,
        IDocumentTextExtractor textExtractor,
        ITextExtractionDetector textDetector,
        ILogger<DocumentTextExtractionService> logger,
        IOptions<TextExtractionOptions> options)
    {
        _documentRepository = documentRepository;
        _fileStorage = fileStorage;
        _textExtractor = textExtractor;
        _textDetector = textDetector;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<ExtractTextResponse> ExtractTextAsync(
        ExtractTextRequest request,
        CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(request.DocumentId, cancellationToken);

        if (document == null)
        {
            throw new TextExtractionNotFoundException($"Document met ID {request.DocumentId} niet gevonden.");
        }

        var extractionVersion = request.ExtractionVersion ?? await GetNextExtractionVersionAsync(request.DocumentId, cancellationToken);

        _logger.LogInformation(
            "Starting text extraction for document {DocumentId} (version {Version})",
            request.DocumentId, extractionVersion);

        using var fileStream = await _fileStorage.OpenReadAsync(document.StorageKey, cancellationToken);

        TextExtractionResult result;
        try
        {
            result = await _textExtractor.ExtractAsync(
                request.DocumentId,
                fileStream,
                cancellationToken);
        }
        catch (TextExtractionProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Text extraction failed for document {DocumentId}", request.DocumentId);
            throw new TextExtractionProviderException(
                $"Text extraction failed: {ex.Message}", ex);
        }

        await SaveExtractionResultAsync(document, extractionVersion, result, cancellationToken);

        _logger.LogInformation(
            "Text extraction completed for document {DocumentId}: {PageCount} pages, {TotalChars} chars, method={Method}, partial={IsPartial}",
            request.DocumentId, result.PageCount, result.TotalCharacterCount, result.Method, result.IsPartial);

        return MapToResponse(document.Id, extractionVersion, result);
    }

    public async Task<TextExtractionResultDto?> GetExtractionResultAsync(
        Guid documentId,
        int extractionVersion,
        CancellationToken cancellationToken = default)
    {
        return null;
    }

    public Task<IReadOnlyList<TextExtractionResultDto>> GetExtractionResultsAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<TextExtractionResultDto>>(new List<TextExtractionResultDto>());
    }

    private async Task<int> GetNextExtractionVersionAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        return 1;
    }

    private async Task SaveExtractionResultAsync(
        Document document,
        int extractionVersion,
        TextExtractionResult result,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Extraction result for document {DocumentId} version {Version}: Method={Method}, Pages={PageCount}, Chars={TotalChars}, Partial={IsPartial}, Warnings={WarningCount}",
            document.Id, extractionVersion, result.Method, result.PageCount,
            result.TotalCharacterCount, result.IsPartial, result.Warnings.Count);

        foreach (var warning in result.Warnings)
        {
            _logger.LogWarning("Extraction warning for document {DocumentId}: {Warning}", document.Id, warning);
        }
    }

    private static ExtractTextResponse MapToResponse(
        Guid documentId,
        int extractionVersion,
        TextExtractionResult result)
    {
        return new ExtractTextResponse
        {
            DocumentId = documentId,
            ExtractionVersion = extractionVersion,
            Method = result.Method.ToString(),
            Success = result.HasAnyText && !result.IsPartial,
            IsPartial = result.IsPartial,
            PageCount = result.PageCount,
            TotalCharacterCount = result.TotalCharacterCount,
            HasAnyText = result.HasAnyText,
            Warnings = result.Warnings,
            ExtractedAtUtc = result.ExtractedAtUtc
        };
    }
}

public class TextExtractionOptions
{
    public double MinTextCoverageForNative { get; set; } = 0.8;
    public int MaxOcrTimeoutSeconds { get; set; } = 300;
    public bool EnableOcrFallback { get; set; } = true;
}