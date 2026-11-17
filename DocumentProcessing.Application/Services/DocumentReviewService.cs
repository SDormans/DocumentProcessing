using DocumentProcessing.Application.DTOs;
using DocumentProcessing.Application.Exceptions;
using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Exceptions;
using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DocumentProcessing.Application.Services;

/// <summary>
/// Document review: corrections, approval, rejection and audit log.
/// The backend is the authority for validation and authorization.
/// </summary>
public class DocumentReviewService : IDocumentReviewService
{
    public const int MaxReasonLength = 2_000;

    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentReviewRepository _reviewRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IInvoiceResultRepository _invoiceResultRepository;
    private readonly ValidationService _validationService;
    private readonly ILogger<DocumentReviewService> _logger;
    private readonly ICurrentUser _currentUser;

    public DocumentReviewService(
        IDocumentRepository documentRepository,
        IDocumentReviewRepository reviewRepository,
        IFileStorage fileStorage,
        IInvoiceResultRepository invoiceResultRepository,
        ValidationService validationService,
        ILogger<DocumentReviewService> logger,
        ICurrentUser currentUser)
    {
        _currentUser = currentUser;
        _documentRepository = documentRepository;
        _reviewRepository = reviewRepository;
        _fileStorage = fileStorage;
        _invoiceResultRepository = invoiceResultRepository;
        _validationService = validationService;
        _logger = logger;
    }

    public async Task<DocumentDetailDto?> GetDocumentDetailAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await GetAccessibleDocumentAsync(documentId, cancellationToken);
        if (document == null)
        {
            return null;
        }

        var review = await _reviewRepository.GetByDocumentIdAsync(documentId, cancellationToken);

        var invoiceResult = await _invoiceResultRepository.GetByDocumentIdAsync(documentId, cancellationToken);

        var corrections = ReviewFields.Parse(review?.CorrectedValuesJson);
        var approved = review?.Status == ReviewStatus.Approved ? ReviewFields.Parse(review.ApprovedValuesJson) : null;
        var fields = MapInvoiceFields(invoiceResult, corrections, approved);

        var validationResults = new List<ValidationResultDto>();
        if (invoiceResult != null)
        {
            var result = ReviewFields.Apply(MapToInvoiceExtractionResult(invoiceResult), corrections);
            await _validationService.ValidateAsync(result, cancellationToken);
            validationResults = _validationService.GetAllResults()
                .Select(r => new ValidationResultDto
                {
                    RuleId = r.RuleId,
                    Status = r.Status,
                    Severity = r.Severity,
                    Message = r.Message,
                    AffectedFields = r.AffectedFields
                })
                .ToList();
        }

        return new DocumentDetailDto
        {
            Id = document.Id,
            OriginalFileName = document.OriginalFileName,
            ContentType = document.ContentType,
            FileSize = document.FileSize,
            Status = document.Status.ToString(),
            CreatedAtUtc = document.CreatedAtUtc,
            LastProcessedAtUtc = document.LastProcessedAtUtc,
            ReviewStatus = review?.Status.ToString() ?? ReviewStatus.Pending.ToString(),
            ReviewedBy = review?.ReviewedBy,
            ReviewedAtUtc = review?.ReviewedAtUtc,
            ReviewDecision = review?.ReviewDecision,
            RejectionReason = review?.RejectionReason,
            ContentUrl = $"/api/documents/{documentId}/content",
            ExtractedFields = fields,
            ValidationResults = validationResults
        };
    }
    public async Task<ReviewActionResponse> SubmitCorrectionsAsync(
        Guid documentId,
        SubmitCorrectionsRequest request,
        string userId,
        string userName,
        CancellationToken cancellationToken = default)
    {
        var document = await GetAccessibleDocumentAsync(documentId, cancellationToken);
        if (document == null)
        {
            return NotFoundResponse(documentId, "Document not found.");
        }

        var canReview = await CanReviewAsync(userId, cancellationToken);
        if (!canReview)
        {
            return ForbiddenResponse(documentId, "You are not allowed to correct this document.");
        }

        if (document.Status != Domain.Entities.DocumentStatus.NeedsReview)
        {
            return NotReadyResponse(documentId, document.Status);
        }

        if (request.CorrectedValues == null || request.CorrectedValues.Count == 0)
        {
            return InvalidResponse(documentId, "Geen correcties opgegeven.");
        }

        var inputErrors = ReviewFields.Validate(request.CorrectedValues);
        if (inputErrors.Count > 0)
        {
            return InvalidResponse(documentId, "The corrections are invalid.", inputErrors);
        }

        var review = await GetOrCreateReviewAsync(documentId, cancellationToken);
        if (review.Status == ReviewStatus.Approved || review.Status == ReviewStatus.Rejected)
        {
            return AlreadyDecidedResponse(documentId, review.Status);
        }

        var invoiceResult = await _invoiceResultRepository.GetByDocumentIdAsync(documentId, cancellationToken);
        var corrections = ReviewFields.Parse(review.CorrectedValuesJson);
        var previousValues = EffectiveValues(invoiceResult, corrections);

        foreach (var (fieldName, rawValue) in request.CorrectedValues)
        {
            var value = rawValue?.Trim() ?? string.Empty;
            corrections[fieldName] = value;

            var entry = new ReviewAuditEntry(
                Guid.NewGuid(),
                review.Id,
                userId,
                userName,
                "Correction");
            entry.SetFieldDetails(fieldName, previousValues.GetValueOrDefault(fieldName), value);
            await _reviewRepository.AddAuditEntryAsync(entry, cancellationToken);
        }

        review.SetCorrections(JsonSerializer.Serialize(corrections), userId);

        document.MarkProcessed();
        await _reviewRepository.UpdateAsync(review, cancellationToken);
        await _documentRepository.UpdateAsync(document, cancellationToken);
        if (!await TrySaveAsync(documentId, cancellationToken))
        {
            return ConflictResponse(documentId);
        }

        _logger.LogInformation("Corrections saved for document {DocumentId} by user {UserId}", documentId, userId);

        return new ReviewActionResponse
        {
            DocumentId = documentId,
            ReviewStatus = review.Status.ToString(),
            Success = true,
            Message = "Corrections saved."
        };
    }

    public async Task<ReviewActionResponse> ApproveAsync(
        Guid documentId,
        ApproveRequest request,
        string userId,
        string userName,
        CancellationToken cancellationToken = default)
    {
        var document = await GetAccessibleDocumentAsync(documentId, cancellationToken);
        if (document == null)
        {
            return NotFoundResponse(documentId, "Document not found.");
        }

        var canApprove = await CanApproveOrRejectAsync(userId, cancellationToken);
        if (!canApprove)
        {
            return ForbiddenResponse(documentId, "You are not allowed to approve this document.");
        }

        if (request.Comment?.Length > MaxReasonLength)
        {
            return InvalidResponse(documentId, $"A comment can be at most {MaxReasonLength} characters.");
        }

        if (document.Status != Domain.Entities.DocumentStatus.NeedsReview)
        {
            return NotReadyResponse(documentId, document.Status);
        }

        var review = await GetOrCreateReviewAsync(documentId, cancellationToken);

        if (review.Status == ReviewStatus.Approved || review.Status == ReviewStatus.Rejected)
        {
            return AlreadyDecidedResponse(documentId, review.Status);
        }

        var invoiceResult = await _invoiceResultRepository.GetByDocumentIdAsync(documentId, cancellationToken);
        if (invoiceResult == null)
        {
            return new ReviewActionResponse
            {
                DocumentId = documentId,
                ReviewStatus = review.Status.ToString(),
                Success = false,
                Message = "The document cannot be approved without an extraction result.",
                Error = ReviewActionError.Conflict
            };
        }

        // Approval is judged on the values being approved: the extraction with the reviewer's corrections applied.
        var corrections = ReviewFields.Parse(review.CorrectedValuesJson);
        var result = ReviewFields.Apply(MapToInvoiceExtractionResult(invoiceResult), corrections);
        await _validationService.ValidateAsync(result, cancellationToken);

        if (_validationService.HasErrorsOrCritical())
        {
            var blocking = _validationService.GetErrors().Select(r => r.Message).ToList();
            var invalid = InvalidResponse(documentId, "The document cannot be approved while validation errors remain. Correct the fields and try again.", blocking);
            invalid.ReviewStatus = review.Status.ToString();
            return invalid;
        }

        review.SetApproved(JsonSerializer.Serialize(ReviewFields.ToValues(result)), userId);

        var entry = new ReviewAuditEntry(
            Guid.NewGuid(),
            review.Id,
            userId,
            userName,
            "Approval");
        entry.SetFieldDetails(null, null, request.Comment);
        await _reviewRepository.AddAuditEntryAsync(entry, cancellationToken);

        document.MarkProcessed();
        document.UpdateStatus(Domain.Entities.DocumentStatus.Completed);
        await _reviewRepository.UpdateAsync(review, cancellationToken);
        await _documentRepository.UpdateAsync(document, cancellationToken);
        if (!await TrySaveAsync(documentId, cancellationToken))
        {
            return ConflictResponse(documentId);
        }

        _logger.LogInformation("Document {DocumentId} approved by user {UserId}", documentId, userId);

        return new ReviewActionResponse
        {
            DocumentId = documentId,
            ReviewStatus = review.Status.ToString(),
            ReviewDecision = review.ReviewDecision,
            ReviewedBy = review.ReviewedBy,
            ReviewedAtUtc = review.ReviewedAtUtc,
            Success = true,
            Message = "Document approved."
        };
    }

    public async Task<ReviewActionResponse> RejectAsync(
        Guid documentId,
        RejectRequest request,
        string userId,
        string userName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return InvalidResponse(documentId, "A rejection reason is required.");
        }
        if (request.Reason.Length > MaxReasonLength)
        {
            return InvalidResponse(documentId, $"A rejection reason can be at most {MaxReasonLength} characters.");
        }

        var document = await GetAccessibleDocumentAsync(documentId, cancellationToken);
        if (document == null)
        {
            return NotFoundResponse(documentId, "Document not found.");
        }

        var canApprove = await CanApproveOrRejectAsync(userId, cancellationToken);
        if (!canApprove)
        {
            return ForbiddenResponse(documentId, "You are not allowed to reject this document.");
        }

        if (document.Status != Domain.Entities.DocumentStatus.NeedsReview)
        {
            return NotReadyResponse(documentId, document.Status);
        }

        var review = await GetOrCreateReviewAsync(documentId, cancellationToken);

        if (review.Status == ReviewStatus.Approved || review.Status == ReviewStatus.Rejected)
        {
            return AlreadyDecidedResponse(documentId, review.Status);
        }

        review.SetRejected(request.Reason.Trim(), userId);

        var entry = new ReviewAuditEntry(
            Guid.NewGuid(),
            review.Id,
            userId,
            userName,
            "Rejection");
        entry.SetFieldDetails(null, null, request.Reason);
        await _reviewRepository.AddAuditEntryAsync(entry, cancellationToken);

        document.MarkProcessed();
        document.UpdateStatus(Domain.Entities.DocumentStatus.Failed);
        await _reviewRepository.UpdateAsync(review, cancellationToken);
        await _documentRepository.UpdateAsync(document, cancellationToken);
        if (!await TrySaveAsync(documentId, cancellationToken))
        {
            return ConflictResponse(documentId);
        }

        _logger.LogInformation("Document {DocumentId} rejected by user {UserId}: {Reason}", documentId, userId, request.Reason);

        return new ReviewActionResponse
        {
            DocumentId = documentId,
            ReviewStatus = review.Status.ToString(),
            ReviewDecision = review.ReviewDecision,
            ReviewedBy = review.ReviewedBy,
            ReviewedAtUtc = review.ReviewedAtUtc,
            Success = true,
            Message = "Document rejected."
        };
    }

    public async Task<IReadOnlyList<ReviewAuditEntryDto>?> GetAuditLogAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await GetAccessibleDocumentAsync(documentId, cancellationToken);
        if (document == null)
        {
            return null;
        }

        var review = await _reviewRepository.GetByDocumentIdAsync(documentId, cancellationToken);
        if (review == null)
        {
            return new List<ReviewAuditEntryDto>();
        }

        var entries = await _reviewRepository.GetAuditEntriesAsync(review.Id, cancellationToken);
        return entries.Select(e => new ReviewAuditEntryDto
        {
            Id = e.Id,
            UserId = e.UserId,
            UserName = e.UserName,
            ActionType = e.ActionType,
            FieldName = e.FieldName,
            OldValue = e.OldValue,
            NewValue = e.NewValue,
            CreatedAtUtc = e.CreatedAtUtc
        }).OrderByDescending(e => e.CreatedAtUtc).ToList();
    }

    public async Task<bool> CanReviewAsync(string userId, CancellationToken cancellationToken = default)
    {
        return !string.IsNullOrEmpty(userId);
    }

    public async Task<bool> CanApproveOrRejectAsync(string userId, CancellationToken cancellationToken = default)
    {
        return !string.IsNullOrEmpty(userId);
    }

    /// <summary>
    /// Returns null both for missing documents and for documents the user may not see, so ids of other users' documents are not disclosed.
    /// </summary>
    private async Task<Document?> GetAccessibleDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken);
        if (document != null && !_currentUser.CanAccess(document))
        {
            _logger.LogWarning("User {UserId} was denied review access to document {DocumentId}.", _currentUser.UserId, documentId);
            return null;
        }
        return document;
    }

    /// <summary>
    /// Returns the document's review, creating it (staged, not saved) from the latest extraction result
    /// so a reviewer can approve or reject without first submitting corrections.
    /// </summary>
    private async Task<DocumentReview> GetOrCreateReviewAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var review = await _reviewRepository.GetByDocumentIdAsync(documentId, cancellationToken);
        if (review != null)
        {
            return review;
        }

        var invoiceResult = await _invoiceResultRepository.GetByDocumentIdAsync(documentId, cancellationToken);
        var originalAiJson = invoiceResult != null
            ? JsonSerializer.Serialize(new
            {
                invoiceResult.SupplierName,
                invoiceResult.InvoiceNumber,
                invoiceResult.InvoiceDate,
                invoiceResult.DueDate,
                invoiceResult.Currency,
                invoiceResult.Subtotal,
                invoiceResult.TaxAmount,
                invoiceResult.TotalAmount,
                invoiceResult.Lines,
                invoiceResult.RawAiResponse,
                invoiceResult.ExtractionMethod,
                invoiceResult.ExtractionVersion
            })
            : "{}";

        review = new DocumentReview(Guid.NewGuid(), documentId, originalAiJson);
        await _reviewRepository.AddAsync(review, cancellationToken);
        return review;
    }

    /// <summary>
    /// Saves the review, audit entries and document status in one transaction (the repositories share a DbContext).
    /// </summary>
    private async Task<bool> TrySaveAsync(Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            await _reviewRepository.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (ConcurrencyConflictException ex)
        {
            _logger.LogWarning(ex, "Concurrent review change for document {DocumentId}", documentId);
            return false;
        }
    }

    private static ReviewActionResponse NotReadyResponse(Guid documentId, Domain.Entities.DocumentStatus status) => new()
    {
        DocumentId = documentId,
        Success = false,
        Message = $"The document has status {status} and cannot be reviewed now.",
        Error = ReviewActionError.Conflict
    };

    private static ReviewActionResponse AlreadyDecidedResponse(Guid documentId, ReviewStatus status) => new()
    {
        DocumentId = documentId,
        ReviewStatus = status.ToString(),
        Success = false,
        Message = $"The document is already {status} and can no longer be changed.",
        Error = ReviewActionError.Conflict
    };

    private static ReviewActionResponse ConflictResponse(Guid documentId) => new()
    {
        DocumentId = documentId,
        Success = false,
        Message = "Another user reviewed this document at the same time. Refresh the page and try again.",
        Error = ReviewActionError.Conflict
    };

    private static ReviewActionResponse InvalidResponse(Guid documentId, string message, IReadOnlyList<string>? errors = null) => new()
    {
        DocumentId = documentId,
        Success = false,
        Message = message,
        Errors = errors ?? Array.Empty<string>(),
        Error = ReviewActionError.Invalid
    };

    private static ReviewActionResponse NotFoundResponse(Guid documentId, string message) => new()
    {
        DocumentId = documentId,
        Success = false,
        Message = message,
        Error = ReviewActionError.NotFound
    };

    private static ReviewActionResponse ForbiddenResponse(Guid documentId, string message) => new()
    {
        DocumentId = documentId,
        Success = false,
        Message = message,
        Error = ReviewActionError.Forbidden
    };

    private static IReadOnlyDictionary<string, string?> EffectiveValues(
        InvoiceExtractionResultEntity? invoiceResult,
        IReadOnlyDictionary<string, string> corrections)
    {
        var result = invoiceResult != null ? MapToInvoiceExtractionResult(invoiceResult) : new InvoiceExtractionResult();
        return ReviewFields.ToValues(ReviewFields.Apply(result, corrections));
    }

    private static IReadOnlyList<ExtractedFieldDto> MapInvoiceFields(
        InvoiceExtractionResultEntity? invoiceResult,
        IReadOnlyDictionary<string, string> corrections,
        IReadOnlyDictionary<string, string>? approved)
    {
        if (invoiceResult == null)
        {
            return new List<ExtractedFieldDto>();
        }

        var extracted = ReviewFields.ToValues(MapToInvoiceExtractionResult(invoiceResult));
        return ReviewFields.Names.Select(name => new ExtractedFieldDto
        {
            FieldName = name,
            Value = extracted[name],
            HasValue = extracted[name] != null,
            CorrectedValue = corrections.TryGetValue(name, out var corrected) ? corrected : null,
            ApprovedValue = approved != null && approved.TryGetValue(name, out var approvedValue) ? NullIfEmpty(approvedValue) : null
        }).ToList();
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static InvoiceExtractionResult MapToInvoiceExtractionResult(
        DocumentProcessing.Application.Services.Invoice.InvoiceExtractionResultEntity entity)
    {
        return new InvoiceExtractionResult
        {
            SupplierName = entity.SupplierName,
            SupplierNameHasValue = entity.SupplierNameHasValue,
            InvoiceNumber = entity.InvoiceNumber,
            InvoiceNumberHasValue = entity.InvoiceNumberHasValue,
            InvoiceDate = entity.InvoiceDate,
            InvoiceDateHasValue = entity.InvoiceDateHasValue,
            DueDate = entity.DueDate,
            DueDateHasValue = entity.DueDateHasValue,
            Currency = entity.Currency,
            CurrencyHasValue = entity.CurrencyHasValue,
            Subtotal = entity.Subtotal,
            SubtotalHasValue = entity.SubtotalHasValue,
            TaxAmount = entity.TaxAmount,
            TaxAmountHasValue = entity.TaxAmountHasValue,
            TotalAmount = entity.TotalAmount,
            TotalAmountHasValue = entity.TotalAmountHasValue,
            Lines = string.IsNullOrEmpty(entity.Lines) ? null : ReviewFields.SplitLines(entity.Lines),
            LinesHasValue = entity.LinesHasValue,
            RawResponse = entity.RawAiResponse,
            ExtractionMethod = entity.ExtractionMethod,
            ExtractionVersion = entity.ExtractionVersion,
            PageCount = entity.PageCount,
            TotalCharacterCount = entity.TotalCharacterCount
        };
    }
}