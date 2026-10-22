using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentProcessing.Application.Services.Invoice;

/// <summary>
/// Contract for extracting structured invoice information.
/// </summary>
public interface IInvoiceExtractor
{
    Task<InvoiceExtractionResult> ExtractAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<InvoiceExtractionResult> ImproveAsync(Guid documentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the result of an invoice extraction operation.
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
    public List<string> Warnings { get; set; } = new();
    public string? RawResponse { get; init; }
    public bool Success { get; init; }
    public string ExtractionMethod { get; set; } = "Unknown";
    public int ExtractionVersion { get; set; } = 1;
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
        SupplierName = data.SupplierName,
        SupplierNameHasValue = data.SupplierNameHasValue,
        InvoiceNumber = data.InvoiceNumber,
        InvoiceNumberHasValue = data.InvoiceNumberHasValue,
        InvoiceDate = data.InvoiceDate,
        InvoiceDateHasValue = data.InvoiceDateHasValue,
        DueDate = data.DueDate,
        DueDateHasValue = data.DueDateHasValue,
        Currency = data.Currency,
        CurrencyHasValue = data.CurrencyHasValue,
        Subtotal = data.Subtotal,
        SubtotalHasValue = data.SubtotalHasValue,
        TaxAmount = data.TaxAmount,
        TaxAmountHasValue = data.TaxAmountHasValue,
        TotalAmount = data.TotalAmount,
        TotalAmountHasValue = data.TotalAmountHasValue,
        Lines = data.Lines,
        LinesHasValue = data.LinesHasValue,
        Warnings = data.Warnings != null ? new List<string>(data.Warnings) : new List<string>(),
        ExtractionMethod = data.ExtractionMethod,
        ExtractionVersion = data.ExtractionVersion,
        PageCount = data.PageCount,
        TotalCharacterCount = data.TotalCharacterCount
    };

    public static InvoiceExtractionResult FailureResult(string errorMessage, string rawResponse = "") => new()
    {
        RawResponse = rawResponse,
        Success = false,
        Warnings = string.IsNullOrWhiteSpace(errorMessage) ? new List<string>() : new List<string> { errorMessage }
    };
}