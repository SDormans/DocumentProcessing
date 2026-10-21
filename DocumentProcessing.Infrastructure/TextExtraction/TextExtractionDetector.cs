using DocumentProcessing.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace DocumentProcessing.Infrastructure.TextExtraction;

public class TextExtractionDetector : ITextExtractionDetector
{
    private readonly ILogger<TextExtractionDetector> _logger;

    public TextExtractionDetector(ILogger<TextExtractionDetector> logger)
    {
        _logger = logger;
    }

    public async Task<bool> NeedsOcrAsync(
        Stream pdfStream,
        CancellationToken cancellationToken = default)
    {
        var coverage = await GetTextCoverageAsync(pdfStream, cancellationToken);
        var needsOcr = coverage < 0.5;

        _logger.LogDebug("Text coverage: {Coverage:P0}, needs OCR: {NeedsOcr}", coverage, needsOcr);
        return needsOcr;
    }

    public async Task<double> GetTextCoverageAsync(
        Stream pdfStream,
        CancellationToken cancellationToken = default)
    {
        using var pdfDocument = PdfDocument.Open(pdfStream);

        var totalPages = pdfDocument.GetPages().Count();
        if (totalPages == 0)
        {
            return 0.0;
        }

        var pagesWithText = 0;
        foreach (var page in pdfDocument.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = page.Text;
            if (!string.IsNullOrWhiteSpace(text) && text.Length > 10)
            {
                pagesWithText++;
            }
        }

        return (double)pagesWithText / totalPages;
    }
}