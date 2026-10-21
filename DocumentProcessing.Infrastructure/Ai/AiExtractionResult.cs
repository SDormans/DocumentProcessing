using DocumentProcessing.Application.Services.Invoice;

namespace DocumentProcessing.Infrastructure.Ai;

/// <summary>
/// Resultaat van AI-gegevensextractie.
/// Bevat zowel de ruwe AI-response als de gestructureerde factuurdata.
/// </summary>
public class AiExtractionResult
{
    /// <summary>
    /// Ruwe AI-response tekst.
    /// </summary>
    public string RawResponse { get; init; } = string.Empty;

    /// <summary>
    /// Of de response succesvol was.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gestructureerde factuurgegevens (indien parseerbaar).
    /// </summary>
    public InvoiceExtractionResult? StructuredData { get; init; }

    /// <summary>
    /// Foutmelding indien mislukt.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Aanmaaktijd in UTC.
    /// </summary>
    public DateTime ExtractedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Maakt een succesvol resultaat aan.
    /// </summary>
    /// <param name="rawResponse">De ruwe AI-response tekst.</param>
    /// <param name="data">De gestructureerde factuurgegevens.</param>
    /// <returns>Een nieuwe AiExtractionResult exemplaar met Success=true.</returns>
    public static AiExtractionResult SuccessResult(string rawResponse, InvoiceExtractionResult data)
    {
        return new AiExtractionResult
        {
            RawResponse = rawResponse,
            Success = true,
            StructuredData = data
        };
    }

    /// <summary>
    /// Maakt een mislukkend resultaat aan.
    /// </summary>
    /// <param name="errorMessage">De foutmelding.</param>
    /// <param name="rawResponse">De ruwe AI-response tekst (optioneel).</param>
    /// <returns>Een nieuwe AiExtractionResult exemplaar met Success=false.</returns>
    public static AiExtractionResult FailureResult(string errorMessage, string rawResponse = "")
    {
        return new AiExtractionResult
        {
            RawResponse = rawResponse,
            Success = false,
            ErrorMessage = errorMessage
        };
    }
}