namespace DocumentProcessing.Application.Services.Invoice;

/// <summary>
/// Interface voor factuurgegevensextractie.
/// </summary>
public interface IInvoiceExtractor
{
    Task<InvoiceExtractionResult> ExtractAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<InvoiceExtractionResult> ImproveAsync(Guid documentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resultaat van factuurgegevensextractie.
/// </summary>
public class InvoiceExtractionResult
{
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
    public List<string>? Lines { get; set; }
    public bool LinesHasValue { get; set; }
    public List<string> Warnings { get; set; } = new List<string>();
    public string? RawResponse { get; init; }
    public bool Success { get; init; }
    public string ExtractionMethod { get; set; } = "Unknown";
    public int ExtractionVersion { get; set; }
    public int PageCount { get; set; }
    public int TotalCharacterCount { get; set; }

    public static InvoiceExtractionResult Empty() => new()
    {
        ExtractionMethod = "Unknown",
        ExtractionVersion = 1,
        PageCount = 0,
        TotalCharacterCount = 0
    };

    public static InvoiceExtractionResult SuccessResult(string rawResponse, InvoiceExtractionResult data) => new()
    {
        RawResponse = rawResponse,
        Success = true,
        // De velden worden overgenomen van 'data' via de constructor/initializers
    };

    public static InvoiceExtractionResult FailureResult(string errorMessage, string rawResponse = "") => new()
    {
        RawResponse = rawResponse,
        Success = false,
        // De velden worden standaard ingesteld op null/false
    };
}