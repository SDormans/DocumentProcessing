using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Domain.Interfaces;
using DocumentProcessing.Domain.Models;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace DocumentProcessing.Infrastructure.TextExtraction;

public class OcrPdfTextExtractor : IDocumentTextExtractor
{
    private readonly IOcrProvider _ocrProvider;
    private readonly ILogger<OcrPdfTextExtractor> _logger;

    public OcrPdfTextExtractor(
        IOcrProvider ocrProvider,
        ILogger<OcrPdfTextExtractor> logger)
    {
        _ocrProvider = ocrProvider;
        _logger = logger;
    }

    public async Task<TextExtractionResult> ExtractAsync(
        Guid documentId,
        Stream pdfStream,
        CancellationToken cancellationToken = default)
    {
        if (!_ocrProvider.IsAvailable)
        {
            _logger.LogError("OCR provider is not available for document {DocumentId}", documentId);
            return new TextExtractionResult
            {
                DocumentId = documentId,
                ExtractionVersion = 1,
                Method = TextExtractionMethod.Failed,
                Pages = new List<ExtractedPage>(),
                Warnings = new List<string> { "OCR-provider is niet beschikbaar." },
                Quality = new TextExtractionQuality { AverageConfidence = 0, PagesWithLowConfidence = 0 }
            };
        }

        var pages = new List<ExtractedPage>();
        var warnings = new List<string>();
        var pagesWithLowConfidence = 0;

        using var pdfDocument = PdfDocument.Open(pdfStream);
        var pageNumber = 0;

        foreach (var pdfPage in pdfDocument.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            pageNumber++;

            try
            {
                // Attempt native text extraction first
                var nativeText = pdfPage.Text ?? string.Empty;

                string ocrText;
                if (string.IsNullOrWhiteSpace(nativeText) || nativeText.Length < 10)
                {
                    _logger.LogDebug("Page {PageNumber}: insufficient native text, attempting OCR", pageNumber);

                    // Attempt OCR - in a full implementation, render page to image first
                    // For now, return empty text if native text is insufficient
                    ocrText = await _ocrProvider.ExtractTextFromImageAsync(
                        Stream.Null,
                        pageNumber,
                        cancellationToken);

                    if (string.IsNullOrWhiteSpace(ocrText))
                    {
                        warnings.Add($"Pagina {pageNumber}: OCR leverde geen tekst op.");
                        pagesWithLowConfidence++;
                    }
                }
                else
                {
                    ocrText = nativeText;
                }

                pages.Add(new ExtractedPage
                {
                    PageNumber = pageNumber,
                    Text = ocrText ?? string.Empty
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OCR extraction failed for page {PageNumber}", pageNumber);
                warnings.Add($"Pagina {pageNumber}: OCR mislukt ({ex.Message})");
                pages.Add(new ExtractedPage
                {
                    PageNumber = pageNumber,
                    Text = string.Empty
                });
            }
        }

        var hasAnyText = pages.Any(p => p.HasText);
        var totalChars = pages.Sum(p => p.CharacterCount);
        var method = hasAnyText
            ? (pages.All(p => p.HasText) ? TextExtractionMethod.Ocr : TextExtractionMethod.Partial)
            : TextExtractionMethod.Failed;

        if (!hasAnyText)
        {
            warnings.Add("OCR leverde voor geen enkele pagina tekst op.");
        }

        var result = new TextExtractionResult
        {
            DocumentId = documentId,
            ExtractionVersion = 1,
            Method = method,
            Pages = pages,
            Warnings = warnings,
            Quality = new TextExtractionQuality
            {
                AverageConfidence = hasAnyText ? 0.7 : 0.0,
                PagesWithLowConfidence = pagesWithLowConfidence
            }
        };

        _logger.LogInformation(
            "OCR extraction completed for document {DocumentId}: {PageCount} pages, {TotalChars} chars, method={Method}",
            documentId, pages.Count, totalChars, method);

        return result;
    }
}