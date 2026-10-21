using DocumentProcessing.Application.Services.Invoice;

/// <summary>
/// Interface voor een individuele validatieregel voor factuurgegevens.
/// Ondersteunt extensiebaarheid - nieuwe regels kunnen worden toegevoegd
/// zonder de centrale validatielogica te hoeven wijzigen.
/// </summary>
public interface IInvoiceValidationRule
{
    /// <summary>
    /// De unieke identifier van deze regel.
    /// </summary>
    string RuleId { get; }

    /// <summary>
    /// Voert de validatie uit op het extractieresultaat.
    /// </summary>
    /// <param name="result">Het factuur-extractieresultaat om te valideren.</param>
    /// <returnsEen verzameling validatieresultaten (kan lege lijst retourneren als er geen problemen zijn).</returns>
    IEnumerable<ValidationResult> Validate(InvoiceExtractionResult result);
}