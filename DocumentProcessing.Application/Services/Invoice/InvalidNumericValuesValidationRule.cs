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
    /// Voert de validatie uit op het extractieresultaat.
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
                    Message = $"Subtotaal {subtotal} is negatief. Controleer of de waarde correct is geëxtraheerd.",
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
                    Message = $"BTW-bedrag {tax} is negatief. Controleer de waarde.",
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
                    Message = $"Totaalbedrag {total} is negatief.",
                    AffectedFields = "TotalAmount",
                    ExtraData = new { TotalAmountValue = total, IsNegative = true }
                });
            }
        }

        return failures;
    }
}