using System;

namespace DocumentProcessing.Domain.Entities;

/// <summary>
/// Audit trail record for an action performed during document review.
/// Captures user identifier, timestamp, affected fields, old/new values, and action type.
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
    /// Type of action: Correction, Approval, or Rejection.
    /// </summary>
    public string ActionType { get; private set; } = string.Empty;

    /// <summary>
    /// Name of the corrected field (for corrections).
    /// </summary>
    public string? FieldName { get; private set; }

    /// <summary>
    /// Value prior to correction.
    /// </summary>
    public string? OldValue { get; private set; }

    /// <summary>
    /// Value following correction.
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
