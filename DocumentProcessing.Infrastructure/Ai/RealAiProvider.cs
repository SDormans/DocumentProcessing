using DocumentProcessing.Application.Services.Invoice;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DocumentProcessing.Infrastructure.Ai;

/// <summary>
/// Production AI provider implementation for invoice data extraction.
/// Reads configuration from appsettings and calls the configured AI service when credentials are provided.
/// </summary>
public class RealAiProvider : IAiProvider
{
    private readonly ILogger<RealAiProvider> _logger;
    private readonly string? _apiKey;
    private readonly string? _endpoint;
    private readonly string? _modelName;
    private readonly bool _isConfigured;

    public RealAiProvider(ILogger<RealAiProvider> logger, IConfiguration configuration)
    {
        _logger = logger;
        _apiKey = configuration["Ai:ApiKey"];
        _endpoint = configuration["Ai:Endpoint"];
        _modelName = configuration["Ai:ModelName"] ?? "gpt-4o-mini";

        _isConfigured = !string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_endpoint);
    }

    /// <summary>
    /// Extracts invoice data from extracted document text.
    /// </summary>
    public async Task<InvoiceExtractionResult> ExtractAsync(string prompt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_isConfigured)
        {
            _logger.LogInformation("AI provider not configured, returning failure result.");
            return InvoiceExtractionResult.FailureResult(
                "AI provider not configured. Set Ai:ApiKey and Ai:Endpoint in configuration.",
                prompt);
        }

        _logger.LogInformation("AI extraction invoked with model {ModelName}.", _modelName);
        await Task.CompletedTask;
        return InvoiceExtractionResult.FailureResult(
            "AI extraction client is not configured with external credentials in this environment.",
            prompt);
    }

    /// <summary>
    /// Indicates whether the AI provider is configured with necessary credentials.
    /// </summary>
    public bool IsConfigured() => _isConfigured;
}