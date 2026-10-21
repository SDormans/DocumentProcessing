using DocumentProcessing.Application.Services.Invoice;

/// <summary>
/// Valideert of de valuta wordt ondersteund of bekend is.
/// Onbekende valuta's worden als waarschuwing gemarkeerd in plaats van fout,
/// aangezien internationale facturen gebruikelijk zijn.
/// </summary>
public class UnsupportedCurrencyValidationRule : IInvoiceValidationRule
{
    private readonly IEnumerable<string> _supportedCurrencies;

    /// <summary>
    /// Initialiseert een nieuwe instantie van de <see cref="UnsupportedCurrencyValidationRule"/> klasse.
    /// </summary>
    /// <param name="supportedCurrencies">Lijst met ondersteunde valuta codes (e.g. "EUR", "USD", "GBP").</param>
    public UnsupportedCurrencyValidationRule(IEnumerable<string> supportedCurrencies)
    {
        _supportedCurrencies = supportedCurrencies ?? throw new ArgumentNullException(nameof(supportedCurrencies));
    }

    /// <summary>
    /// Unieke identifier van deze regel.
    /// </summary>
    public string RuleId => "UNSUPPORTED_CURRENCY";

    /// <summary>
    /// Voert de validatie uit op het extractieresultaat.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(InvoiceExtractionResult result)
    {
        var failures = new List<ValidationResult>();

        if (result.CurrencyHasValue && !string.IsNullOrEmpty(result.Currency))
        {
            var currency = result.Currency.Trim().ToUpperInvariant();

            if (!_supportedCurrencies.Any(c => c.Trim().ToUpperInvariant() == currency))
            {
                failures.Add(new ValidationResult
                {
                    RuleId = RuleId,
                    Status = ValidationStatus.Warning.ToString(),
                    Severity = ValidationSeverity.Warning.ToString(),
                    Message = $"Valuta '{currency}' is niet standaard. Controleer of dit correct is voor de transactie.",
                    AffectedFields = "Currency",
                    ExtraData = new { CurrencyValue = currency, IsSupported = false }
                });
            }
        }

        return failures;
    }
}
