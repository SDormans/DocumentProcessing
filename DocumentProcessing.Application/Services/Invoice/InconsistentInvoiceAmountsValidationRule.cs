using DocumentProcessing.Application.Services.Invoice;

/// <summary>
/// Valideert of factuurbedragen consistentie vertonen tussen Subtotal, Tax en Total.
/// Rekening houdt met afrondingstolerantie om specifieke afrekening te voorkomen.
/// </summary>
public class InconsistentInvoiceAmountsValidationRule : IInvoiceValidationRule
{
    private readonly decimal _tolerance;

    /// <summary>
    /// Initialiseert een nieuwe instantie van de <see cref="InconsistentInvoiceAmountsValidationRule"/> klasse.
    /// </summary>
    /// <param name="tolerance">Afrondingstolerantie in EUR/valuta eenheden. Standaard 0.01.</param>
    public InconsistentInvoiceAmountsValidationRule(decimal tolerance = 0.01m)
    {
        _tolerance = tolerance;
    }

    /// <summary>
    /// Unieke identifier van deze regel.
    /// </summary>
    public string RuleId => "INCONSISTENT_AMOUNTS";

    /// <summary>
    /// Voert de validatie uit op het extractieresultaat.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(InvoiceExtractionResult result)
    {
        var failures = new List<ValidationResult>();

        // Alleen valideren als alle drie de bedragen beschikbaar zijn
        if (!result.SubtotalHasValue || !result.Subtotal.HasValue ||
            !result.TaxAmountHasValue || !result.TaxAmount.HasValue ||
            !result.TotalAmountHasValue || !result.TotalAmount.HasValue)
        {
            // Kan geen consistentie controleren als gegevens ontbreken
            return failures;
        }

        var subtotal = result.Subtotal.Value;
        var tax = result.TaxAmount.Value;
        var total = result.TotalAmount.Value;

        // Bereken het verwachte totaal: Subtotal + Tax
        var calculatedTotal = subtotal + tax;

        // Controleer met tolerantie
        var difference = Math.Abs(total - calculatedTotal);

        if (difference > _tolerance)
        {
            // Bepaal of de total te hoog of te laag is
            var isTotalHigher = total > calculatedTotal;

            failures.Add(new ValidationResult
            {
                RuleId = RuleId,
                Status = ValidationStatus.Failing.ToString(),
                Severity = ValidationSeverity.Warning.ToString(),
                Message = isTotalHigher
                    ? $"Factuurbedrag ({total}) ligt {difference:C} boven de verwachte som van Subtotal ({subtotal}) + Tax ({tax}) = {calculatedTotal:C}."
                    : $"Factuurbedrag ({total}) ligt {difference:C} onder de verwachte som van Subtotal ({subtotal}) + Tax ({tax}) = {calculatedTotal:C}. " +
                      $"Dit kan wijzen op foutieve BTW-toepassing of afronding.",
                AffectedFields = "Subtotal, TaxAmount, TotalAmount",
                ExtraData = new
                {
                    Subtotal = subtotal,
                    Tax = tax,
                    CalculatedTotal = calculatedTotal,
                    StoredTotal = total,
                    Difference = difference,
                    IsTotalHigher = isTotalHigher
                }
            });
        }

        // Extra controle: kan Subtotal afgeleid worden van Total en Tax?
        if (tax != 0 && total >= subtotal)
        {
            var recoveredSubtotal = total - tax;
            var subtotalDifference = Math.Abs(subtotal - recoveredSubtotal);

            if (subtotalDifference > _tolerance)
            {
                // Waarschuwing voor afwijking in plaats van fout, omdat afronding kan optreden
                failures.Add(new ValidationResult
                {
                    RuleId = RuleId,
                    Status = ValidationStatus.Warning.ToString(),
                    Severity = ValidationSeverity.Warning.ToString(),
                    Message = $"Mogelijke afrondingverschillen: Subtotal extra herrekend als {recoveredSubtotal:C} (origineel: {subtotal:C}), verschil: {subtotalDifference:C}.",
                    AffectedFields = "Subtotal, TaxAmount, TotalAmount",
                    ExtraData = new
                    {
                        OriginalSubtotal = subtotal,
                        RecoveredSubtotal = recoveredSubtotal,
                        Difference = subtotalDifference
                    }
                });
            }
        }

        return failures;
    }
}
