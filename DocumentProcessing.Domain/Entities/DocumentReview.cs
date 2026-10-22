using DocumentProcessing.Domain.Enums;

namespace DocumentProcessing.Domain.Entities;

/// <summary>
/// Houdt de menselijke review van een document bij.
/// De originele AI-output wordt apart bewaard van menselijke correcties en goedgekeurde waarden,
/// zodat niets stilzwijgend wordt overschreven.
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
    /// Originele AI-output (ongewijzigd bewaard).
    /// </summary>
    public string OriginalAiOutputJson { get; private set; } = string.Empty;

    /// <summary>
    /// Door een mens aangebrachte correcties (nog niet goedgekeurd).
    /// </summary>
    public string? CorrectedValuesJson { get; private set; }

    /// <summary>
    /// Uiteindelijk goedgekeurde waarden.
    /// </summary>
    public string? ApprovedValuesJson { get; private set; }

    public ReviewStatus Status { get; private set; }

    /// <summary>
    /// Gebruiker die de review heeft uitgevoerd/laatst gewijzigd.
    /// </summary>
    public string? ReviewedBy { get; private set; }

    public DateTime? ReviewedAtUtc { get; private set; }

    /// <summary>
    /// Beslissing: "Approved" of "Rejected".
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
        ApprovedValuesJson = approvedValuesJson;
        ReviewedBy = reviewedBy;
        ReviewedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
        Status = ReviewStatus.Approved;
        ReviewDecision = "Approved";
    }

    public void SetRejected(string rejectionReason, string reviewedBy)
    {
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
