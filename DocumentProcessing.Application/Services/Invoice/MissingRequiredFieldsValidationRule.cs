namespace DocumentProcessing.Application.Services.Invoice;

/// <summary>
/// Validates that mandatory invoice fields are present.
/// </summary>
public class MissingRequiredFieldsValidationRule : IInvoiceValidationRule
{
    private static readonly string[] DefaultRequiredFields = { "SupplierName", "InvoiceNumber", "InvoiceDate", "TotalAmount" };
    private readonly IEnumerable<string> _requiredFields;

    /// <summary>
    /// Initializes a new instance of the <see cref="MissingRequiredFieldsValidationRule"/> class.
    /// </summary>
    /// <param name="requiredFields">List of required fields (e.g. "InvoiceNumber", "InvoiceDate"). Defaults to standard fields.</param>
    public MissingRequiredFieldsValidationRule(IEnumerable<string>? requiredFields = null)
    {
        _requiredFields = requiredFields ?? DefaultRequiredFields;
    }

    /// <summary>
    /// Unique identifier for this rule.
    /// </summary>
    public string RuleId => "MISSING_REQUIRED_FIELDS";

    /// <summary>
    /// Executes validation against the extracted invoice result.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(InvoiceExtractionResult result)
    {
        var failures = new List<ValidationResult>();

        foreach (var field in _requiredFields)
        {
            bool hasValue = field switch
            {
                "SupplierName" => result.SupplierNameHasValue && !string.IsNullOrWhiteSpace(result.SupplierName),
                "InvoiceNumber" => result.InvoiceNumberHasValue && !string.IsNullOrWhiteSpace(result.InvoiceNumber),
                "InvoiceDate" => result.InvoiceDateHasValue && result.InvoiceDate.HasValue,
                "DueDate" => result.DueDateHasValue && result.DueDate.HasValue,
                "Currency" => result.CurrencyHasValue && !string.IsNullOrWhiteSpace(result.Currency),
                "Subtotal" => result.SubtotalHasValue && result.Subtotal.HasValue,
                "TaxAmount" => result.TaxAmountHasValue && result.TaxAmount.HasValue,
                "TotalAmount" => result.TotalAmountHasValue && result.TotalAmount.HasValue,
                "Lines" => result.LinesHasValue && (result.Lines?.Count > 0),
                _ => false
            };

            if (!hasValue)
            {
                failures.Add(new ValidationResult
                {
                    RuleId = RuleId,
                    Status = ValidationStatus.Failing.ToString(),
                    Severity = ValidationSeverity.Error.ToString(),
                    Message = $"Field '{field}' is required but is missing or empty.",
                    AffectedFields = field,
                    ExtraData = new { Found = hasValue }
                });
            }
        }

        return failures;
    }
}
