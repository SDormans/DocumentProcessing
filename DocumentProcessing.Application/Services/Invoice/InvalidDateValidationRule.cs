using DocumentProcessing.Application.Services.Invoice;

/// <summary>
/// Checks that dates are valid and within expected ranges.
/// </summary>
public class InvalidDateValidationRule : IInvoiceValidationRule
{
    /// <summary>
    /// Unieke identifier van deze regel.
    /// </summary>
    public string RuleId => "INVALID_DATE";

    /// <summary>
    /// Validates the extraction result.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(InvoiceExtractionResult result)
    {
        var failures = new List<ValidationResult>();

        // Invoice date
        if (result.InvoiceDateHasValue && result.InvoiceDate.HasValue)
        {
            var date = result.InvoiceDate.Value;
            // The invoice date cannot be in the future (general business rule).
            // Deze controle kan worden uitgezet via configuratie
            if (date > DateTime.Today.AddYears(1))
            {
                failures.Add(new ValidationResult
                {
                    RuleId = RuleId,
                    Status = ValidationStatus.Warning.ToString(),
                    Severity = ValidationSeverity.Warning.ToString(),
                    Message = $"Invoice date {date:d} is in the future and may be suspicious.",
                    AffectedFields = "InvoiceDate",
                    ExtraData = new { DateValue = date }
                });
            }

            // The date cannot be implausibly old (e.g. before 1900).
            if (date < new DateTime(1900, 1, 1))
            {
                failures.Add(new ValidationResult
                {
                    RuleId = RuleId,
                    Status = ValidationStatus.Failing.ToString(),
                    Severity = ValidationSeverity.Error.ToString(),
                    Message = $"Invoice date {date:d} looks implausibly old.",
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
                        Message = $"Due date {dueDate:d} is before the invoice date {result.InvoiceDate.Value:d}.",
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
                    Message = $"Due date {dueDate:d} is in the future.",
                    AffectedFields = "DueDate",
                    ExtraData = new { DueDateValue = dueDate }
                });
            }
        }

        return failures;
    }
}
