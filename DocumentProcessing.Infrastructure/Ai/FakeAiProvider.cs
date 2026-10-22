using DocumentProcessing.Application.Services.Invoice;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentProcessing.Infrastructure.Ai;

/// <summary>
/// Fake AI provider implementation for testing purposes.
/// Produces deterministic results without requiring paid external API credentials.
/// </summary>
public class FakeAiProvider : IAiProvider
{
    private readonly bool _returnSuccess;
    private readonly InvoiceExtractionResult? _sampleResult;

    public FakeAiProvider(bool returnSuccess = true, InvoiceExtractionResult? sampleResult = null)
    {
        _returnSuccess = returnSuccess;
        _sampleResult = sampleResult ?? InvoiceExtractionResult.Empty();
    }

    /// <summary>
    /// Returns simulated extraction data.
    /// </summary>
    public async Task<InvoiceExtractionResult> ExtractAsync(string prompt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(10, cancellationToken).ConfigureAwait(false);

        if (_returnSuccess && _sampleResult != null)
        {
            var truncatedPrompt = prompt.Length > 50 ? prompt[..50] : prompt;
            var successMsg = $"Fake AI response for: {truncatedPrompt}...";
            return InvoiceExtractionResult.SuccessResult(successMsg, _sampleResult);
        }

        const string failureMsg = "Fake AI provider returned failure state.";
        return InvoiceExtractionResult.FailureResult(failureMsg, prompt);
    }
}