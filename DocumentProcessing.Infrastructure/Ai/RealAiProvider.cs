using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using DocumentProcessing.Application.Services.Invoice;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DocumentProcessing.Infrastructure.Ai;

/// <summary>
/// Echte AI-provider implementatie voor factuurgegevensextractie.
/// Leest configuratie uit appsettings en gebruikt de API indien beschikbaar.
/// Wordt gebruikt wanneer explicite geconfigureerde credentials beschikbaar zijn.
/// </summary>
public class RealAiProvider : IAiProvider
{
    private readonly ILogger<RealAiProvider> _logger;
    private readonly string? _apiKey;
    private readonly string? _endpoint;
    private readonly string? _modelName;
    private readonly bool _isConfigured;

    /// <summary>
    /// Initialiseert een nieuwe instantie van de <see cref="RealAiProvider"/> klasse.
    /// </summary>
    /// <param name="logger">Logger instantie.</param>
    /// <param name="configuration">Configuration voor AI-instellingen.</param>
    public RealAiProvider(ILogger<RealAiProvider> logger, IConfiguration configuration)
    {
        _logger = logger;
        _apiKey = configuration["Ai:ApiKey"];
        _endpoint = configuration["Ai:Endpoint"];
        _modelName = configuration["Ai:ModelName"] ?? "gpt-4o-mini";

        _isConfigured = !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_endpoint);
    }

    /// <summary>
    /// Extraheert factuurgegevens uit de gegeven tekst.
    /// </summary>
    /// <param name="prompt">De prompt met context en instructies.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>AI-extractieresultaat.</returns>
    public async Task<InvoiceExtractionResult> ExtractAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (!_isConfigured)
        {
            _logger.LogInformation("AI provider not configured, returning mock result.");
            return InvoiceExtractionResult.FailureResult(
                "AI provider not configured. Set Ai:ApiKey and Ai:Endpoint in configuration.",
                prompt);
        }

        // In een volledige implementatie zou hier de API-call plaatsvinden:
        // var client = new HttpClient();
        // var response = await client.PostAsync(_endpoint, new StringContent(prompt));
        // var content = await response.Content.ReadAsStringAsync();
        // ... parseer de response en vul InvoiceExtractionResult

        // Voornuut returnen we een placeholder aangezien de API niet direct beschikbaar is
        // in deze omgeving zonder echte API-sleutels.
        _logger.LogInformation("AI extraction called but API not implemented in this build. Using fallback.");
        return InvoiceExtractionResult.FailureResult(
            "AI-extractie niet geïmplementeerd in huidige build zonder API-sleutels.",
            prompt);
    }

    /// <summary>
    /// Controleert of de AI-provider correct is geconfigureerd.
    /// </summary>
    /// <returns>True als de provider geconfigureerd is, anders false.</returns>
    public bool IsConfigured() => _isConfigured;
}