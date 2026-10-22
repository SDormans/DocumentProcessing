using DocumentProcessing.Domain.Enums;
using System;
using System.Collections.Generic;

namespace DocumentProcessing.Domain.Entities;

/// <summary>
/// Tracks human review of an extracted document.
/// Preserves original AI output separately from human corrections and approved values.
/// </summary>
public class DocumentReview
{
    private DocumentReview()
    {
        // Required by EF Core
    }

    public DocumentReview(
        Guid id,
        Guid documentId,
        string originalAiOutputJson)
    {
        Id = id;
        DocumentId = documentId;
        OriginalAiOutputJson = originalAiOutputJson;
        Status = ReviewStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }

    /// <summary>
    /// Original unedited AI extraction output in JSON format.
    /// </summary>
    public string OriginalAiOutputJson { get; private set; } = string.Empty;

    /// <summary>
    /// Human-supplied corrections before final approval in JSON format.
    /// </summary>
    public string? CorrectedValuesJson { get; private set; }

    /// <summary>
    /// Final approved values in JSON format.
    /// </summary>
    public string? ApprovedValuesJson { get; private set; }

    public ReviewStatus Status { get; private set; }

    /// <summary>
    /// Identifier of the reviewer who performed or last updated the review.
    /// </summary>
    public string? ReviewedBy { get; private set; }

    public DateTime? ReviewedAtUtc { get; private set; }

    /// <summary>
    /// Final review decision: "Approved" or "Rejected".
    /// </summary>
    public string? ReviewDecision { get; private set; }

    public string? RejectionReason { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    // Navigation
    public virtual Document? Document { get; private set; }
    public virtual ICollection<ReviewAuditEntry> AuditEntries { get; private set; } = new List<ReviewAuditEntry>();

    public void SetCorrections(string correctedValuesJson, string reviewedBy)
    {
        if (Status == ReviewStatus.Approved || Status == ReviewStatus.Rejected)
        {
            throw new InvalidOperationException($"Cannot apply corrections to a review that is already {Status}.");
        }

        CorrectedValuesJson = correctedValuesJson;
        ReviewedBy = reviewedBy;
        UpdatedAtUtc = DateTime.UtcNow;

        if (Status == ReviewStatus.Pending)
        {
            Status = ReviewStatus.Corrected;
        }
    }

    public void SetApproved(string approvedValuesJson, string reviewedBy)
    {
        if (Status == ReviewStatus.Approved || Status == ReviewStatus.Rejected)
        {
            throw new InvalidOperationException($"Cannot approve a review that is already {Status}.");
        }

        ApprovedValuesJson = approvedValuesJson;
        ReviewedBy = reviewedBy;
        ReviewedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
        Status = ReviewStatus.Approved;
        ReviewDecision = "Approved";
    }

    public void SetRejected(string rejectionReason, string reviewedBy)
    {
        if (Status == ReviewStatus.Approved || Status == ReviewStatus.Rejected)
        {
            throw new InvalidOperationException($"Cannot reject a review that is already {Status}.");
        }

        ReviewedBy = reviewedBy;
        ReviewedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
        Status = ReviewStatus.Rejected;
        ReviewDecision = "Rejected";
        RejectionReason = rejectionReason;
    }

    public void AddAuditEntry(ReviewAuditEntry entry)
    {
        AuditEntries.Add(entry);
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
