using DocumentProcessing.Application.Services.Invoice;

namespace DocumentProcessing.Infrastructure.Ai;

/// <summary>
/// Fake AI-provider voor testing doeleinden.
/// Vereist geen betaalde API-sleutels en levert voorspelbare resultaten.
/// Ideaal voor unit-tests en CI/CD pipelines.
/// </summary>
public class FakeAiProvider : IAiProvider
{
    private readonly bool _returnSuccess;
    private readonly InvoiceExtractionResult? _sampleResult;

    /// <summary>
    /// Initialiseert een nieuwe instantie van de <see cref="FakeAiProvider"/> klasse.
    /// </summary>
    /// <param name="returnSuccess">Of de fake succesvol moet zijn.</param>
    /// <param name="sampleResult">Voorbeeld-extractieresultaat (indien success).</param>
    public FakeAiProvider(bool returnSuccess = true, InvoiceExtractionResult? sampleResult = null)
    {
        _returnSuccess = returnSuccess;
        _sampleResult = sampleResult ?? InvoiceExtractionResult.Empty();
    }

    /// <summary>
    /// Extraheert factuurgegevens uit de gegeven tekst (fake implementatie).
    /// </summary>
    /// <param name="prompt">De prompt met context en instructies (wordt genegeerd door fake).</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>AI-extractieresultaat.</returns>
    public async Task<InvoiceExtractionResult> ExtractAsync(string prompt, CancellationToken cancellationToken = default)
    {
        // Simuleer enige verwerkingstijd
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);

        if (_returnSuccess && _sampleResult != null)
        {
            var successMsg = String.Format("Fake AI response for: {0}...", prompt.Substring(0, Math.Min(50, prompt.Length)));
            return InvoiceExtractionResult.SuccessResult(successMsg, _sampleResult);
        }

        var failureMsg = "Fake AI provider returned failure state.";
        return InvoiceExtractionResult.FailureResult(failureMsg, prompt);
    }
}