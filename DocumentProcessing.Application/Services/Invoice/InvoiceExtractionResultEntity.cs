using DocumentProcessing.Domain.Entities;

namespace DocumentProcessing.Application.Services.Invoice;

/// <summary>
/// Resultaat van factuurgegevensextractie - EF Core entity voor database persistentie.
/// </summary>
public class InvoiceExtractionResultEntity
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int ExtractionVersion { get; set; }
    public string ExtractionMethod { get; set; } = "Unknown";
    public int PageCount { get; set; }
    public int TotalCharacterCount { get; set; }
    public string? RawAiResponse { get; set; }
    public string? ErrorMessage { get; set; }
    public string? LineItemsJson { get; set; }
    public string? SupplierName { get; set; }
    public bool SupplierNameHasValue { get; set; }
    public string? InvoiceNumber { get; set; }
    public bool InvoiceNumberHasValue { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public bool InvoiceDateHasValue { get; set; }
    public DateTime? DueDate { get; set; }
    public bool DueDateHasValue { get; set; }
    public string? Currency { get; set; }
    public bool CurrencyHasValue { get; set; }
    public decimal? Subtotal { get; set; }
    public bool SubtotalHasValue { get; set; }
    public decimal? TaxAmount { get; set; }
    public bool TaxAmountHasValue { get; set; }
    public decimal? TotalAmount { get; set; }
    public bool TotalAmountHasValue { get; set; }
    public string? Lines { get; set; }
    public bool LinesHasValue { get; set; }
    public string? WarningsJson { get; set; }
    public DateTime ExtractedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public virtual Document? Document { get; set; }
}