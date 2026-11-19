using DocumentProcessing.Domain.Enums;
using System.Text.Json.Serialization;

namespace DocumentProcessing.Application.DTOs;

/// <summary>
/// Document summary for the list page, including review status.
/// </summary>
public class DocumentListItemDto
{
    public Guid Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastProcessedAtUtc { get; set; }

    /// <summary>
    /// Aantal validatiefouten (Ernst Error/Critical).
    /// </summary>
    public int ValidationErrorCount { get; set; }

    /// <summary>
    /// Aantal validatiewaarschuwingen (Ernst Warning).
    /// </summary>
    public int ValidationWarningCount { get; set; }

    /// <summary>
    /// Huidige review-status.
    /// </summary>
    public string ReviewStatus { get; set; } = string.Empty;

    /// <summary>
    /// Name of the user who reviewed the document, if any.
    /// </summary>
    public string? ReviewedBy { get; set; }

    public bool CanBeReviewed { get; set; }
}

/// <summary>
/// One extracted invoice field with its optional source location.
/// </summary>
public class ExtractedFieldDto
{
    public string FieldName { get; set; } = string.Empty;
    public string? Value { get; set; }
    public bool HasValue { get; set; }
    public string? CorrectedValue { get; set; }
    public string? ApprovedValue { get; set; }
    public bool IsCorrected => CorrectedValue != null && !string.Equals(Value, CorrectedValue, StringComparison.Ordinal);
    public bool IsApproved => ApprovedValue != null;

    /// <summary>
    /// The value the review currently stands on: approved, else corrected, else extracted.
    /// </summary>
    public string? EffectiveValue => ApprovedValue ?? CorrectedValue ?? Value;

    /// <summary>
    /// PDF page the value came from, if known.
    /// </summary>
    public int? SourcePage { get; set; }

    /// <summary>
    /// Source text around the value, if known.
    /// </summary>
    public string? SourcePassage { get; set; }
}

/// <summary>
/// One validation result for the frontend.
/// </summary>
public class ValidationResultDto
{
    [JsonPropertyName("ruleId")]
    public string RuleId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("affectedFields")]
    public string? AffectedFields { get; set; }
}

/// <summary>
/// Detailpagina van één document, inclusief geëxtraheerde velden en validatieresultaten.
/// </summary>
public class DocumentDetailDto
{
    public Guid Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastProcessedAtUtc { get; set; }

    /// <summary>Model type chosen for this document (<c>Cloud</c> or <c>Local</c>).</summary>
    public string AiModelType { get; set; } = string.Empty;

    /// <summary>Model type and name that produced the latest result, after any fallback.</summary>
    public string? ProcessedWithModelType { get; set; }
    public string? ProcessedWithModelName { get; set; }

    /// <summary>Why processing failed, when <see cref="Status"/> is <c>Failed</c>.</summary>
    public string? FailureReason { get; set; }

    public string ReviewStatus { get; set; } = string.Empty;
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewDecision { get; set; }
    public string? RejectionReason { get; set; }

    /// <summary>
    /// Authorized URL of the original PDF.
    /// </summary>
    public string ContentUrl { get; set; } = string.Empty;

    public IReadOnlyList<ExtractedFieldDto> ExtractedFields { get; set; } = new List<ExtractedFieldDto>();
    public IReadOnlyList<ValidationResultDto> ValidationResults { get; set; } = new List<ValidationResultDto>();
}

/// <summary>
/// Verzoek om geëxtraheerde waarden te corrigeren.
/// </summary>
public class SubmitCorrectionsRequest
{
    /// <summary>
    /// Velden met gecorrigeerde waarden, sleuteld op veldnaam.
    /// </summary>
    public IDictionary<string, string> CorrectedValues { get; set; } = new Dictionary<string, string>();
}

/// <summary>
/// Request to approve a document.
/// </summary>
public class ApproveRequest
{
    public string? Comment { get; set; }
}

/// <summary>
/// Request to reject a document.
/// </summary>
public class RejectRequest
{
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Response to a review action, with the updated status.
/// </summary>
public class ReviewActionResponse
{
    public Guid DocumentId { get; set; }
    public string ReviewStatus { get; set; } = string.Empty;
    public string? ReviewDecision { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }

    /// <summary>
    /// Why the action failed; the API maps it to the HTTP status code.
    /// </summary>
    [JsonIgnore]
    public ReviewActionError Error { get; set; }

    /// <summary>
    /// Validation failures that blocked the action (invalid corrections or blocking validation results on approval).
    /// </summary>
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();
}

public enum ReviewActionError
{
    None,
    Invalid,
    NotFound,
    Forbidden,
    Conflict
}

/// <summary>
/// One audit log row for the frontend.
/// </summary>
public class ReviewAuditEntryDto
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string? FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// The signed-in user, for the auth context.
/// </summary>
public class UserInfoDto
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public IReadOnlyList<string> Roles { get; set; } = new List<string>();
}
