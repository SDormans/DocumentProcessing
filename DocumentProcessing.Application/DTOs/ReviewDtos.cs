using DocumentProcessing.Domain.Enums;
using System.Text.Json.Serialization;

namespace DocumentProcessing.Application.DTOs;

/// <summary>
/// Samenvatting van een document voor de overzichtspagina, inclusief review-status.
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
    /// Naam van de gebruiker die de review heeft uitgevoerd (indien van toepassing).
    /// </summary>
    public string? ReviewedBy { get; set; }

    public bool CanBeReviewed { get; set; }
}

/// <summary>
/// Een individueel geëxtraheerd factuurveld met optionele bronlocatie.
/// </summary>
public class ExtractedFieldDto
{
    public string FieldName { get; set; } = string.Empty;
    public string? Value { get; set; }
    public bool HasValue { get; set; }
    public string? CorrectedValue { get; set; }
    public string? ApprovedValue { get; set; }
    public bool IsCorrected => !string.Equals(Value, CorrectedValue, StringComparison.OrdinalIgnoreCase);
    public bool IsApproved => !string.IsNullOrEmpty(ApprovedValue);

    /// <summary>
    /// Pagina in het PDF-document waar de waarde vandaan komt (indien beschikbaar).
    /// </summary>
    public int? SourcePage { get; set; }

    /// <summary>
    /// Passage/tekst uit de bron rondom de waarde (indien beschikbaar).
    /// </summary>
    public string? SourcePassage { get; set; }
}

/// <summary>
/// Detail van één validatieresultaat voor de frontend.
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

    public string ReviewStatus { get; set; } = string.Empty;
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewDecision { get; set; }
    public string? RejectionReason { get; set; }

    /// <summary>
    /// Geautoriseerde URL om het originele PDF-document op te vragen.
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
/// Verzoek om een document goed te keuren.
/// </summary>
public class ApproveRequest
{
    public string? Comment { get; set; }
}

/// <summary>
/// Verzoek om een document af te wijzen.
/// </summary>
public class RejectRequest
{
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Antwoord op een reviewactie met bijgewerkte status.
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
}

/// <summary>
/// Één auditlog-rij voor de frontend.
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
/// Gebruikersinformatie voor de auth-context.
/// </summary>
public class UserInfoDto
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public IReadOnlyList<string> Roles { get; set; } = new List<string>();
}
