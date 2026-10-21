namespace DocumentProcessing.Application.Services.Invoice;

/// <summary>
/// Interface voor AI-provider voor factuurgegevensextractie.
/// De businesslogica mag niet afhankelijk zijn van een specifieke modelnaam.
/// </summary>
public interface IAiProvider
{
    /// <summary>
    /// Extraheert factuurgegevens uit de gegeven tekst.
    /// </summary>
    /// <param name="prompt">De prompt met context en instructies.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>AI-extractieresultaat.</returns>
    Task<InvoiceExtractionResult> ExtractAsync(string prompt, CancellationToken cancellationToken = default);
}