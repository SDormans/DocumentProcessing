using DocumentProcessing.Application.Services.Invoice;

/// <summary>
/// Valideert of verplichte velden aanwezig zijn.
/// Welke velden verplicht zijn, kan worden configured via de configuratie.
/// </summary>
public class MissingRequiredFieldsValidationRule : IInvoiceValidationRule
{
    private readonly IEnumerable<string> _requiredFields;

    /// <summary>
    /// Initialiseert een nieuwe instantie van de <see cref="MissingRequiredFieldsValidationRule"/> klasse.
    /// </summary>
    /// <param name="requiredFields">Lijst met vereiste velden (e.g. "InvoiceNumber", "InvoiceDate").</param>
    public MissingRequiredFieldsValidationRule(IEnumerable<string> requiredFields)
    {
        _requiredFields = requiredFields ?? throw new ArgumentNullException(nameof(requiredFields));
    }

    /// <summary>
    /// Unieke identifier van deze regel.
    /// </summary>
    public string RuleId => "MISSING_REQUIRED_FIELDS";

    /// <summary>
    /// Voert de validatie uit op het extractieresultaat.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(InvoiceExtractionResult result)
    {
        var failures = new List<ValidationResult>();

        foreach (var field in _requiredFields)
        {
            bool hasValue = field switch
            {
                "SupplierName" => result.SupplierNameHasValue && !string.IsNullOrEmpty(result.SupplierName),
                "InvoiceNumber" => result.InvoiceNumberHasValue && !string.IsNullOrEmpty(result.InvoiceNumber),
                "InvoiceDate" => result.InvoiceDateHasValue && result.InvoiceDate.HasValue,
                "DueDate" => result.DueDateHasValue && result.DueDate.HasValue,
                "Currency" => result.CurrencyHasValue && !string.IsNullOrEmpty(result.Currency),
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
                    Message = $"Veld '{field}' is vereist maar ontbreekt of is leeg.",
                    AffectedFields = field,
                    ExtraData = new { Found = hasValue }
                });
            }
        }

        return failures;
    }
}
