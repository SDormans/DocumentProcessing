using DocumentProcessing.Application.DTOs;
using DocumentProcessing.Application.Exceptions;
using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DocumentProcessing.Application.Services;

/// <summary>
/// Service voor documentreview: correcties, goedkeuring, afwijzing en auditlog.
/// De backend is de autoritatieve bron voor validatie en autorisatie.
/// </summary>
public class DocumentReviewService : IDocumentReviewService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentReviewRepository _reviewRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IInvoiceResultRepository _invoiceResultRepository;
    private readonly ValidationService _validationService;
    private readonly ILogger<DocumentReviewService> _logger;

    public DocumentReviewService(
        IDocumentRepository documentRepository,
        IDocumentReviewRepository reviewRepository,
        IFileStorage fileStorage,
        IInvoiceResultRepository invoiceResultRepository,
        ValidationService validationService,
        ILogger<DocumentReviewService> logger)
    {
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
        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken);
        if (document == null)
        {
            return null;
        }

        var review = await _reviewRepository.GetByDocumentIdAsync(documentId, cancellationToken);

        var invoiceResult = await _invoiceResultRepository.GetByDocumentIdAsync(documentId, cancellationToken);

        var fields = MapInvoiceFields(invoiceResult);

        var validationResults = new List<ValidationResultDto>();
        if (invoiceResult != null)
        {
            var result = MapToInvoiceExtractionResult(invoiceResult);
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
        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken);
        if (document == null)
        {
            return NotFoundResponse(documentId, "Document niet gevonden.");
        }

        var canReview = await CanReviewAsync(userId, cancellationToken);
        if (!canReview)
        {
            return ForbiddenResponse(documentId, "U heeft geen toestemming om dit document te corrigeren.");
        }

        var review = await _reviewRepository.GetByDocumentIdAsync(documentId, cancellationToken);
        bool isNew = review == null;

        if (isNew)
        {
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
        }

        if (request.CorrectedValues == null || request.CorrectedValues.Count == 0)
        {
            return new ReviewActionResponse
            {
                DocumentId = documentId,
                ReviewStatus = review.Status.ToString(),
                Success = false,
                Message = "Geen correcties opgegeven."
            };
        }

        var correctedJson = JsonSerializer.Serialize(request.CorrectedValues);
        review.SetCorrections(correctedJson, userId);

        foreach (var kvp in request.CorrectedValues)
        {
            var entry = new ReviewAuditEntry(
                Guid.NewGuid(),
                review.Id,
                userId,
                userName,
                "Correction");
            entry.SetFieldDetails(kvp.Key, null, kvp.Value);
            await _reviewRepository.AddAuditEntryAsync(entry, cancellationToken);
        }

        await _reviewRepository.UpdateAsync(review, cancellationToken);
        await _reviewRepository.SaveChangesAsync(cancellationToken);

        document.MarkProcessed();
        await _documentRepository.UpdateAsync(document, cancellationToken);
        await _documentRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Correcties opgeslagen voor document {DocumentId} door gebruiker {UserId}", documentId, userId);

        return new ReviewActionResponse
        {
            DocumentId = documentId,
            ReviewStatus = review.Status.ToString(),
            Success = true,
            Message = "Correcties opgeslagen."
        };
    }

    public async Task<ReviewActionResponse> ApproveAsync(
        Guid documentId,
        ApproveRequest request,
        string userId,
        string userName,
        CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken);
        if (document == null)
        {
            return NotFoundResponse(documentId, "Document niet gevonden.");
        }

        var canApprove = await CanApproveOrRejectAsync(userId, cancellationToken);
        if (!canApprove)
        {
            return ForbiddenResponse(documentId, "U heeft geen toestemming om dit document goed te keuren.");
        }

        var review = await _reviewRepository.GetByDocumentIdAsync(documentId, cancellationToken);
        if (review == null)
        {
            return new ReviewActionResponse
            {
                DocumentId = documentId,
                ReviewStatus = ReviewStatus.Pending.ToString(),
                Success = false,
                Message = "Geen review aanwezig voor dit document."
            };
        }

        if (review.Status == ReviewStatus.Approved || review.Status == ReviewStatus.Rejected)
        {
            return new ReviewActionResponse
            {
                DocumentId = documentId,
                ReviewStatus = review.Status.ToString(),
                Success = false,
                Message = $"Document is al {review.Status} en kan niet opnieuw worden goedgekeurd."
            };
        }

        var invoiceResult = await _invoiceResultRepository.GetByDocumentIdAsync(documentId, cancellationToken);

        if (invoiceResult != null)
        {
            var result = MapToInvoiceExtractionResult(invoiceResult);
            await _validationService.ValidateAsync(result, cancellationToken);

            if (_validationService.HasCriticalErrors())
            {
                return new ReviewActionResponse
                {
                    DocumentId = documentId,
                    ReviewStatus = review.Status.ToString(),
                    Success = false,
                    Message = "Document kan niet worden goedgekeurd omdat er kritieke validatiefouten zijn."
                };
            }
        }

        var approvedValuesJson = review.CorrectedValuesJson ?? review.OriginalAiOutputJson;
        review.SetApproved(approvedValuesJson, userId);

        var entry = new ReviewAuditEntry(
            Guid.NewGuid(),
            review.Id,
            userId,
            userName,
            "Approval");
        entry.SetFieldDetails(null, null, request.Comment);
        await _reviewRepository.AddAuditEntryAsync(entry, cancellationToken);

        await _reviewRepository.UpdateAsync(review, cancellationToken);
        await _reviewRepository.SaveChangesAsync(cancellationToken);

        document.MarkProcessed();
        document.UpdateStatus(Domain.Entities.DocumentStatus.Completed);
        await _documentRepository.UpdateAsync(document, cancellationToken);
        await _documentRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Document {DocumentId} goedgekeurd door gebruiker {UserId}", documentId, userId);

        return new ReviewActionResponse
        {
            DocumentId = documentId,
            ReviewStatus = review.Status.ToString(),
            ReviewDecision = review.ReviewDecision,
            ReviewedBy = review.ReviewedBy,
            ReviewedAtUtc = review.ReviewedAtUtc,
            Success = true,
            Message = "Document goedgekeurd."
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
            return new ReviewActionResponse
            {
                DocumentId = documentId,
                Success = false,
                Message = "Een afwijsreden is verplicht."
            };
        }

        var document = await _documentRepository.GetByIdAsync(documentId, cancellationToken);
        if (document == null)
        {
            return NotFoundResponse(documentId, "Document niet gevonden.");
        }

        var canApprove = await CanApproveOrRejectAsync(userId, cancellationToken);
        if (!canApprove)
        {
            return ForbiddenResponse(documentId, "U heeft geen toestemming om dit document af te wijzen.");
        }

        var review = await _reviewRepository.GetByDocumentIdAsync(documentId, cancellationToken);
        if (review == null)
        {
            return new ReviewActionResponse
            {
                DocumentId = documentId,
                ReviewStatus = ReviewStatus.Pending.ToString(),
                Success = false,
                Message = "Geen review aanwezig voor dit document."
            };
        }

        if (review.Status == ReviewStatus.Approved || review.Status == ReviewStatus.Rejected)
        {
            return new ReviewActionResponse
            {
                DocumentId = documentId,
                ReviewStatus = review.Status.ToString(),
                Success = false,
                Message = $"Document is al {review.Status} en kan niet opnieuw worden afgewezen."
            };
        }

        review.SetRejected(request.Reason, userId);

        var entry = new ReviewAuditEntry(
            Guid.NewGuid(),
            review.Id,
            userId,
            userName,
            "Rejection");
        entry.SetFieldDetails(null, null, request.Reason);
        await _reviewRepository.AddAuditEntryAsync(entry, cancellationToken);

        await _reviewRepository.UpdateAsync(review, cancellationToken);
        await _reviewRepository.SaveChangesAsync(cancellationToken);

        document.MarkProcessed();
        document.UpdateStatus(Domain.Entities.DocumentStatus.Failed);
        await _documentRepository.UpdateAsync(document, cancellationToken);
        await _documentRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Document {DocumentId} afgewezen door gebruiker {UserId}: {Reason}", documentId, userId, request.Reason);

        return new ReviewActionResponse
        {
            DocumentId = documentId,
            ReviewStatus = review.Status.ToString(),
            ReviewDecision = review.ReviewDecision,
            ReviewedBy = review.ReviewedBy,
            ReviewedAtUtc = review.ReviewedAtUtc,
            Success = true,
            Message = "Document afgewezen."
        };
    }

    public async Task<IReadOnlyList<ReviewAuditEntryDto>> GetAuditLogAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
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

    private static ReviewActionResponse NotFoundResponse(Guid documentId, string message)
    {
        return new ReviewActionResponse
        {
            DocumentId = documentId,
            Success = false,
            Message = message
        };
    }

    private static ReviewActionResponse ForbiddenResponse(Guid documentId, string message)
    {
        return new ReviewActionResponse
        {
            DocumentId = documentId,
            Success = false,
            Message = message
        };
    }

    private static IReadOnlyList<ExtractedFieldDto> MapInvoiceFields(
        DocumentProcessing.Application.Services.Invoice.InvoiceExtractionResultEntity? invoiceResult)
    {
        if (invoiceResult == null)
        {
            return new List<ExtractedFieldDto>();
        }

        return new List<ExtractedFieldDto>
        {
            new() { FieldName = "SupplierName", Value = invoiceResult.SupplierName, HasValue = invoiceResult.SupplierNameHasValue },
            new() { FieldName = "InvoiceNumber", Value = invoiceResult.InvoiceNumber, HasValue = invoiceResult.InvoiceNumberHasValue },
            new() { FieldName = "InvoiceDate", Value = invoiceResult.InvoiceDate?.ToString("yyyy-MM-dd"), HasValue = invoiceResult.InvoiceDateHasValue },
            new() { FieldName = "DueDate", Value = invoiceResult.DueDate?.ToString("yyyy-MM-dd"), HasValue = invoiceResult.DueDateHasValue },
            new() { FieldName = "Currency", Value = invoiceResult.Currency, HasValue = invoiceResult.CurrencyHasValue },
            new() { FieldName = "Subtotal", Value = invoiceResult.Subtotal?.ToString("F2"), HasValue = invoiceResult.SubtotalHasValue },
            new() { FieldName = "TaxAmount", Value = invoiceResult.TaxAmount?.ToString("F2"), HasValue = invoiceResult.TaxAmountHasValue },
            new() { FieldName = "TotalAmount", Value = invoiceResult.TotalAmount?.ToString("F2"), HasValue = invoiceResult.TotalAmountHasValue },
            new() { FieldName = "Lines", Value = invoiceResult.Lines, HasValue = invoiceResult.LinesHasValue }
        };
    }

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
            Lines = string.IsNullOrEmpty(entity.Lines) ? null : new List<string> { entity.Lines },
            LinesHasValue = entity.LinesHasValue,
            RawResponse = entity.RawAiResponse,
            ExtractionMethod = entity.ExtractionMethod,
            ExtractionVersion = entity.ExtractionVersion,
            PageCount = entity.PageCount,
            TotalCharacterCount = entity.TotalCharacterCount
        };
    }
}