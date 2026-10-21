using DocumentProcessing.Application.Services.Invoice;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Service voor het coordinaten van alle factuurvalidatieregels.
/// Ondersteunt extensiebaarheid - nieuwe regels kunnen worden toegevoegd
/// aan de verzameling zonder de centrale logica te hoeven wijzigen.
/// </summary>
public class ValidationService
{
    private readonly IEnumerable<IInvoiceValidationRule> _validationRules;
    private readonly List<ValidationResult> _allResults = new List<ValidationResult>();

    /// <summary>
    /// Initialiseert een nieuwe instantie van de <see cref="ValidationService"/> klasse.
    /// </summary>
    /// <param name="validationRules">Lijst van geldige <see cref="IInvoiceValidationRule"/> instanties.</param>
    public ValidationService(IEnumerable<IInvoiceValidationRule> validationRules)
    {
        _validationRules = validationRules ?? throw new ArgumentNullException(nameof(validationRules));
    }

    /// <summary>
    /// Valideert een factuur-extractieresultaat door alle geregistreerde regels uit te voeren.
    /// </summary>
    /// <param name="result">Het factuur-extractieresultaat om te valideren.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    public async Task ValidateAsync(InvoiceExtractionResult result, CancellationToken cancellationToken = default)
    {
        _allResults.Clear();

        foreach (var rule in _validationRules)
        {
            var ruleResults = rule.Validate(result);
            foreach (var validationResult in ruleResults)
            {
                _allResults.Add(validationResult);
            }
        }
    }

    /// <summary>
    /// Krijgt alle validatieresultaten van de laatste validatie-run.
    /// </summary>
    public IReadOnlyCollection<ValidationResult> GetAllResults() => _allResults.AsReadOnly();

    /// <summary>
    /// Krijgt alle fouten (ernst >= Error) van de laatste validatie-run.
    /// </summary>
    public IEnumerable<ValidationResult> GetErrors()
    {
        return _allResults.Where(r => r.Severity == "Error" || r.Severity == "Critical");
    }

    /// <summary>
    /// Krijgt alle waarschuwingen (ernst >= Warning) van de laatste validatie-run.
    /// </summary>
    public IEnumerable<ValidationResult> GetWarnings()
    {
        return _allResults.Where(r => r.Severity == "Warning");
    }

    /// <summary>
    /// Krijgt alle succesvolle validaties (ernst = Passing) van de laatste validatie-run.
    /// </summary>
    public IEnumerable<ValidationResult> GetPassing()
    {
        return _allResults.Where(r => r.Status == "Passing");
    }

    /// <summary>
    /// Bepaalt of het document kritieke fouten heeft die een "NeedsReview" status zouden moeten opleveren.
    /// </summary>
    public bool HasCriticalErrors()
    {
        return _allResults.Any(r => r.Severity == "Critical");
    }

    /// <summary>
    /// Bepaalt of het document fouten heeft die aandacht vereisen.
    /// </summary>
    public bool HasErrorsOrCritical()
    {
        return _allResults.Any(r => r.Severity == "Error" || r.Severity == "Critical");
    }

    /// <summary>
    /// Bepaalt of het document alleen waarschuwingen heeft (geen fatale fouten).
    /// </summary>
    public bool HasOnlyWarnings()
    {
        return _allResults.Any(r => r.Severity == "Warning") &&
               !_allResults.Any(r => r.Severity == "Error" || r.Severity == "Critical");
    }

    /// <summary>
    /// Bepaalt of de validatie is geslaagd (geen errors of critical issues).
    /// </summary>
    public bool IsValidationPassed()
    {
        return !_allResults.Any(r => r.Severity == "Error" || r.Severity == "Critical");
    }
}
