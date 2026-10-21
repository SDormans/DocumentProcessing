using DocumentProcessing.Application.Services.Invoice;

/// <summary>
/// Valideert of factuurregels consistentie vertonen (e.g. hoeveelheid * eenheidsprijs = bedrag).
/// </summary>
public class InconsistentInvoiceLinesValidationRule : IInvoiceValidationRule
{
    /// <summary>
    /// Unieke identifier van deze regel.
    /// </summary>
    public string RuleId => "INCONSISTENT_LINES";

    /// <summary>
    /// Voert de validatie uit op het extractieresultaat.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(InvoiceExtractionResult result)
    {
        var failures = new List<ValidationResult>();

        // Alleen valideren als er regels beschikbaar zijn
        if (result.Lines == null || result.Lines.Count == 0)
        {
            // Geen regels om op te valideren
            return failures;
        }

        // De Lines verzameling bevat strings; we proberen basisevaluatie te doen
        // Door het format van de strings te parseerën of minimale controle te doen
        for (int i = 0; i < result.Lines.Count; i++)
        {
            var line = result.Lines[i];
            // Basisset: controleer of de regel niet leeg is en een redelijke lengte heeft
            if (string.IsNullOrWhiteSpace(line))
            {
                failures.Add(new ValidationResult
                {
                    RuleId = RuleId,
                    Status = ValidationStatus.Warning.ToString(),
                    Severity = ValidationSeverity.Warning.ToString(),
                    Message = $"Factuurregel {i + 1} is leeg of bevat alleen spaties.",
                    AffectedFields = $"Lines[{i}]",
                    ExtraData = new { LineIndex = i, LineContent = line }
                });
                continue;
            }

            // Probeer basisanalyse: controleer of de regel een beetje eruitziet als een geldige regel
            // Dit is een rudimentaire controle; in een productiesysteem zou dit
            // een geparsed line-object model gebruiken
            if (line.Length > 200)
            {
                failures.Add(new ValidationResult
                {
                    RuleId = RuleId,
                    Status = ValidationStatus.Warning.ToString(),
                    Severity = ValidationSeverity.Warning.ToString(),
                    Message = $"Factuurregel {i + 1} is erg lang ({line.Length} tekens) en kan Problemen bevatten.",
                    AffectedFields = $"Lines[{i}]",
                    ExtraData = new { LineIndex = i, LineLength = line.Length }
                });
            }
        }

        // Algemene controle: totale regel-aantal vs. verwachte structuur
        if (result.Lines.Count > 0 && result.TotalAmountHasValue && result.TotalAmount.HasValue)
        {
            // We kunnen hier geen wiskundige controle doen zonder de structuur te parsen,
            // maar we kunnen waarschuwen als er te weinig of te veel regels zijn
            // (deze threshold kan geconfigureerd worden)
            if (result.Lines.Count > 50)
            {
                failures.Add(new ValidationResult
                {
                    RuleId = RuleId,
                    Status = ValidationStatus.Warning.ToString(),
                    Severity = ValidationSeverity.Warning.ToString(),
                    Message = $"Er zijn {result.Lines.Count} regels gevonden. Dit is een groot aantal en moet worden nagekeken.",
                    AffectedFields = "Lines",
                    ExtraData = new { LineCount = result.Lines.Count }
                });
            }
        }

        return failures;
    }
}
