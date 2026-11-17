using DocumentProcessing.Application.Services.Invoice;

/// <summary>
/// Checks that required references are present.
/// Denkt aan: order numbers, klantreferenties, aankooporder nummers, etc.
/// </summary>
public class MissingRequiredReferencesValidationRule : IInvoiceValidationRule
{
    private readonly IEnumerable<string> _requiredReferenceFields;

    /// <summary>
    /// Creates the rule.
    /// </summary>
    /// <param name="requiredReferenceFields">Lijst met velden die referenties bevatten, bijv. ["OrderNumber", "PurchaseOrderNumber"].</param>
    public MissingRequiredReferencesValidationRule(IEnumerable<string> requiredReferenceFields)
    {
        _requiredReferenceFields = requiredReferenceFields ?? throw new ArgumentNullException(nameof(requiredReferenceFields));
    }

    /// <summary>
    /// Unieke identifier van deze regel.
    /// </summary>
    public string RuleId => "MISSING_REQUIRED_REFERENCES";

    /// <summary>
    /// Validates the extraction result.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(InvoiceExtractionResult result)
    {
        var failures = new List<ValidationResult>();

        foreach (var field in _requiredReferenceFields)
        {
            // Each field is checked against the lines and specific properties.
            bool hasReference = result.Lines != null && result.Lines.Any(line =>
            {
                if (string.IsNullOrWhiteSpace(line)) return false;
                // Basiskontrole: bevat de regel de zoekterm?
                return line.Contains(field, StringComparison.OrdinalIgnoreCase);
            });

            if (!hasReference)
            {
                // Check whether the field exists directly on the result.
                // Hier kijken we of de regel overal in de lijst voorkomt
                failures.Add(new ValidationResult
                {
                    RuleId = RuleId,
                    Status = ValidationStatus.Warning.ToString(),
                    Severity = ValidationSeverity.Warning.ToString(),
                    Message = $"Reference field '{field}' does not appear in the invoice lines. Check that the reference was extracted correctly.",
                    AffectedFields = field,
                    ExtraData = new { FoundInLines = hasReference, LineCount = result.Lines?.Count ?? 0 }
                });
            }
        }

        return failures;
    }
}
