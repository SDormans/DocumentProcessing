namespace DocumentProcessing.Domain.Entities;

/// <summary>
/// Controleerbare controlelijst van elke reviewactie op een document.
/// Registreert gebruiker, tijdstip, gewijzigde velden, oude/nieuwe waarde en actietype.
/// </summary>
public class ReviewAuditEntry
{
    private ReviewAuditEntry()
    {
        // Required by EF Core
    }

    public ReviewAuditEntry(
        Guid id,
        Guid documentReviewId,
        string userId,
        string userName,
        string actionType)
    {
        Id = id;
        DocumentReviewId = documentReviewId;
        UserId = userId;
        UserName = userName;
        ActionType = actionType;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid DocumentReviewId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public string UserName { get; private set; } = string.Empty;

    /// <summary>
    /// Type actie: Correction, Approval of Rejection.
    /// </summary>
    public string ActionType { get; private set; } = string.Empty;

    /// <summary>
    /// Naam van het gewijzigde veld (alleen bij correcties).
    /// </summary>
    public string? FieldName { get; private set; }

    /// <summary>
    /// Vorige waarde (alleen bij correcties, indien van toepassing en toegestaan).
    /// </summary>
    public string? OldValue { get; private set; }

    /// <summary>
    /// Nieuwe waarde (alleen bij correcties, indien van toepassing en toegestaan).
    /// </summary>
    public string? NewValue { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    // Navigation
    public virtual DocumentReview? DocumentReview { get; private set; }

    public void SetFieldDetails(string? fieldName, string? oldValue, string? newValue)
    {
        FieldName = fieldName;
        OldValue = oldValue;
        NewValue = newValue;
    }
}
