using DocumentProcessing.Application.Services.Invoice;

/// <summary>
/// Valideert of benodigde referenties/identificatoren aanwezig zijn.
/// Denkt aan: order numbers, klantreferenties, aankooporder nummers, etc.
/// </summary>
public class MissingRequiredReferencesValidationRule : IInvoiceValidationRule
{
    private readonly IEnumerable<string> _requiredReferenceFields;

    /// <summary>
    /// Initialiseert een nieuwe instantie van de <see cref="MissingRequiredReferencesValidationRule"/> klasse.
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
    /// Voert de validatie uit op het extractieresultaat.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(InvoiceExtractionResult result)
    {
        var failures = new List<ValidationResult>();

        foreach (var field in _requiredReferenceFields)
        {
            // Elk veld in de Lines verzameling of specifieke eigenschappen worden gechecked
            bool hasReference = result.Lines != null && result.Lines.Any(line =>
            {
                if (string.IsNullOrWhiteSpace(line)) return false;
                // Basiskontrole: bevat de regel de zoekterm?
                return line.Contains(field, StringComparison.OrdinalIgnoreCase);
            });

            if (!hasReference)
            {
                // Check of het veld direct op het resultaat bestaat (indien van toepassing)
                // Hier kijken we of de regel overal in de lijst voorkomt
                failures.Add(new ValidationResult
                {
                    RuleId = RuleId,
                    Status = ValidationStatus.Warning.ToString(),
                    Severity = ValidationSeverity.Warning.ToString(),
                    Message = $"Referentieveld '{field}' komt niet voor in de factuurregels. Controleer of de referentie correct is geëxtraheerd.",
                    AffectedFields = field,
                    ExtraData = new { FoundInLines = hasReference, LineCount = result.Lines?.Count ?? 0 }
                });
            }
        }

        return failures;
    }
}
