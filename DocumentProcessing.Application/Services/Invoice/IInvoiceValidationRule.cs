namespace DocumentProcessing.Application.Services.Invoice;

/// <summary>
/// Defines a single invoice validation rule.
/// Supports extensibility: new rules can be introduced without modifying central validation orchestration.
/// </summary>
public interface IInvoiceValidationRule
{
    /// <summary>
    /// Unique identifier for this rule (e.g. "MISSING_REQUIRED_FIELDS", "INCONSISTENT_AMOUNTS").
    /// </summary>
    string RuleId { get; }

    /// <summary>
    /// Executes validation against the extracted invoice result.
    /// </summary>
    /// <param name="result">The invoice extraction result to validate.</param>
    /// <returns>A collection of validation findings.</returns>
    IEnumerable<ValidationResult> Validate(InvoiceExtractionResult result);
}