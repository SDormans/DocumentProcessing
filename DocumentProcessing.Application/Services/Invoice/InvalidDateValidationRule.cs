using DocumentProcessing.Application.Services.Invoice;

/// <summary>
/// Valideert of datums geldige datums zijn en voldoen aan verwachte formaten/bereiken.
/// </summary>
public class InvalidDateValidationRule : IInvoiceValidationRule
{
    /// <summary>
    /// Unieke identifier van deze regel.
    /// </summary>
    public string RuleId => "INVALID_DATE";

    /// <summary>
    /// Voert de validatie uit op het extractieresultaat.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(InvoiceExtractionResult result)
    {
        var failures = new List<ValidationResult>();

        // Valideer factuurdatum
        if (result.InvoiceDateHasValue && result.InvoiceDate.HasValue)
        {
            var date = result.InvoiceDate.Value;
            // Voorbeeldcontrole: factuurdatum mag geen toekomstige datum zijn (algemene bedrijfsregel)
            // Deze controle kan worden uitgezet via configuratie
            if (date > DateTime.Today.AddYears(1))
            {
                failures.Add(new ValidationResult
                {
                    RuleId = RuleId,
                    Status = ValidationStatus.Warning.ToString(),
                    Severity = ValidationSeverity.Warning.ToString(),
                    Message = $"Factuurdatum {date:d} is een toekomstige datum en kan verdacht zijn.",
                    AffectedFields = "InvoiceDate",
                    ExtraData = new { DateValue = date }
                });
            }

            // Valideer dat de datum niet te oud is (bijv. voor 1900)
            if (date < new DateTime(1900, 1, 1))
            {
                failures.Add(new ValidationResult
                {
                    RuleId = RuleId,
                    Status = ValidationStatus.Failing.ToString(),
                    Severity = ValidationSeverity.Error.ToString(),
                    Message = $"Factuurdatum {date:d} lijkt onrealistisch oud te zijn.",
                    AffectedFields = "InvoiceDate",
                    ExtraData = new { DateValue = date }
                });
            }
        }

        // Valideer vervaldatum
        if (result.DueDateHasValue && result.DueDate.HasValue)
        {
            var dueDate = result.DueDate.Value;

            // Vervaldatum mag niet voor factuurdatum zijn (tenzij expliciet toegestaan)
            if (result.InvoiceDateHasValue && result.InvoiceDate.HasValue)
            {
                if (dueDate < result.InvoiceDate.Value)
                {
                    failures.Add(new ValidationResult
                    {
                        RuleId = RuleId,
                        Status = ValidationStatus.Failing.ToString(),
                        Severity = ValidationSeverity.Error.ToString(),
                        Message = $"Vervaldatum {dueDate:d} ligt voor de factuurdatum {result.InvoiceDate.Value:d}.",
                        AffectedFields = "DueDate, InvoiceDate",
                        ExtraData = new { InvoiceDate = result.InvoiceDate.Value, DueDate = dueDate }
                    });
                }
            }

            // Vervaldatum mag geen toekomstige datum zijn (ten strikte regel of warning)
            if (dueDate > DateTime.Today.AddYears(1))
            {
                failures.Add(new ValidationResult
                {
                    RuleId = RuleId,
                    Status = ValidationStatus.Warning.ToString(),
                    Severity = ValidationSeverity.Warning.ToString(),
                    Message = $"Vervaldatum {dueDate:d} is een toekomstige datum.",
                    AffectedFields = "DueDate",
                    ExtraData = new { DueDateValue = dueDate }
                });
            }
        }

        return failures;
    }
}
