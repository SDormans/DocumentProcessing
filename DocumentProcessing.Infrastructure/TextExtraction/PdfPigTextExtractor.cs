using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Domain.Interfaces;
using DocumentProcessing.Domain.Models;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace DocumentProcessing.Infrastructure.TextExtraction;

public class PdfPigTextExtractor : IDocumentTextExtractor
{
    private readonly ILogger<PdfPigTextExtractor> _logger;

    public PdfPigTextExtractor(ILogger<PdfPigTextExtractor> logger)
    {
        _logger = logger;
    }

    public async Task<TextExtractionResult> ExtractAsync(
        Guid documentId,
        Stream pdfStream,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting native PDF text extraction for document {DocumentId}", documentId);

        var pages = new List<ExtractedPage>();
        var warnings = new List<string>();

        using var pdfDocument = PdfDocument.Open(pdfStream);

        var pageNumber = 0;
        foreach (var page in pdfDocument.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            pageNumber++;

            try
            {
                var text = page.Text;
                pages.Add(new ExtractedPage
                {
                    PageNumber = pageNumber,
                    Text = text ?? string.Empty
                });

                _logger.LogDebug(
                    "Page {PageNumber}: {CharacterCount} characters extracted",
                    pageNumber, text?.Length ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract text from page {PageNumber}", pageNumber);
                warnings.Add($"Pagina {pageNumber}: tekstextractie mislukt ({ex.Message})");
                pages.Add(new ExtractedPage
                {
                    PageNumber = pageNumber,
                    Text = string.Empty
                });
            }
        }

        var hasAnyText = pages.Any(p => p.HasText);
        var totalChars = pages.Sum(p => p.CharacterCount);
        var method = hasAnyText ? TextExtractionMethod.Native : TextExtractionMethod.Failed;

        if (!hasAnyText)
        {
            warnings.Add("Geen leesbare tekst gevonden in PDF. Overweeg OCR voor gescande documenten.");
        }

        var result = new TextExtractionResult
        {
            DocumentId = documentId,
            ExtractionVersion = 1,
            Method = method,
            Pages = pages,
            Warnings = warnings,
            Quality = CalculateQuality(pages, method)
        };

        _logger.LogInformation(
            "Native extraction completed for document {DocumentId}: {PageCount} pages, {TotalChars} chars, hasText={HasAnyText}",
            documentId, pages.Count, totalChars, hasAnyText);

        return result;
    }

    private static TextExtractionQuality CalculateQuality(
        IReadOnlyList<ExtractedPage> pages,
        TextExtractionMethod method)
    {
        if (!pages.Any())
        {
            return new TextExtractionQuality { AverageConfidence = 0, PagesWithLowConfidence = 0 };
        }

        var pagesWithText = pages.Count(p => p.HasText);
        var confidence = pagesWithText / (double)pages.Count;
        var lowConfidencePages = pages.Count(p => p.CharacterCount < 10);

        return new TextExtractionQuality
        {
            AverageConfidence = confidence,
            PagesWithLowConfidence = lowConfidencePages
        };
    }
}