using DocumentProcessing.Application.Services.Invoice;

/// <summary>
/// Valideert of numerieke waarden geldig zijn (geen negatieve bedragen waar niet verwacht, redelijke bereiken, etc.).
/// </summary>
public class InvalidNumericValuesValidationRule : IInvoiceValidationRule
{
    /// <summary>
    /// Unieke identifier van deze regel.
    /// </summary>
    public string RuleId => "INVALID_NUMERIC_VALUES";

    /// <summary>
    /// Validates the extraction result.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(InvoiceExtractionResult result)
    {
        var failures = new List<ValidationResult>();

        // Valideer Subtotal
        if (result.SubtotalHasValue && result.Subtotal.HasValue)
        {
            var subtotal = result.Subtotal.Value;
            if (subtotal < 0)
            {
                failures.Add(new ValidationResult
                {
                    RuleId = RuleId,
                    Status = ValidationStatus.Failing.ToString(),
                    Severity = ValidationSeverity.Error.ToString(),
                    Message = $"Subtotal {subtotal} is negative. Check that it was extracted correctly.",
                    AffectedFields = "Subtotal",
                    ExtraData = new { SubtotalValue = subtotal, IsNegative = true }
                });
            }
        }

        // Valideer TaxAmount
        if (result.TaxAmountHasValue && result.TaxAmount.HasValue)
        {
            var tax = result.TaxAmount.Value;
            if (tax < 0)
            {
                failures.Add(new ValidationResult
                {
                    RuleId = RuleId,
                    Status = ValidationStatus.Failing.ToString(),
                    Severity = ValidationSeverity.Error.ToString(),
                    Message = $"VAT amount {tax} is negative. Check the value.",
                    AffectedFields = "TaxAmount",
                    ExtraData = new { TaxAmountValue = tax, IsNegative = true }
                });
            }
        }

        // Valideer TotalAmount
        if (result.TotalAmountHasValue && result.TotalAmount.HasValue)
        {
            var total = result.TotalAmount.Value;
            if (total < 0)
            {
                failures.Add(new ValidationResult
                {
                    RuleId = RuleId,
                    Status = ValidationStatus.Failing.ToString(),
                    Severity = ValidationSeverity.Error.ToString(),
                    Message = $"Total amount {total} is negative.",
                    AffectedFields = "TotalAmount",
                    ExtraData = new { TotalAmountValue = total, IsNegative = true }
                });
            }
        }

        return failures;
    }
}