using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Domain.Interfaces;
using DocumentProcessing.Domain.Models;
using DocumentProcessing.Infrastructure.Ocr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Infrastructure.TextExtraction;

/// <summary>
/// Reads the PDF text layer with PdfPig and OCRs only the pages that have (almost) no text,
/// so digital invoices stay fast and scanned ones still get text.
/// </summary>
public sealed class HybridPdfTextExtractor : IDocumentTextExtractor
{
    private readonly PdfPigTextExtractor _native;
    private readonly IOcrProvider _ocr;
    private readonly IPdfPageRenderer _renderer;
    private readonly OcrOptions _options;
    private readonly ILogger<HybridPdfTextExtractor> _logger;

    public HybridPdfTextExtractor(
        PdfPigTextExtractor native,
        IOcrProvider ocr,
        IPdfPageRenderer renderer,
        IOptions<OcrOptions> options,
        ILogger<HybridPdfTextExtractor> logger)
    {
        _native = native;
        _ocr = ocr;
        _renderer = renderer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TextExtractionResult> ExtractAsync(Guid documentId, Stream pdfStream, CancellationToken cancellationToken = default)
    {
        var pdfPath = Path.Combine(Path.GetTempPath(), $"dp-ocr-{Guid.NewGuid():N}.pdf");
        try
        {
            await using (var file = File.Create(pdfPath))
                await pdfStream.CopyToAsync(file, cancellationToken);

            TextExtractionResult native;
            await using (var file = File.OpenRead(pdfPath))
                native = await _native.ExtractAsync(documentId, file, cancellationToken);

            var sparse = native.Pages.Where(IsSparse).Select(p => p.PageNumber).ToList();
            if (sparse.Count == 0)
                return native;

            var warnings = native.Warnings.ToList();
            if (!_options.Enabled || !_ocr.IsAvailable)
            {
                warnings.Add($"{sparse.Count} page(s) have no text layer and OCR is not available.");
                return Copy(native, native.Pages, native.Method, warnings);
            }

            var pages = native.Pages.ToList();
            var ocrPages = 0;
            foreach (var pageNumber in sparse.Take(_options.MaxPages))
            {
                try
                {
                    await using var image = await _renderer.RenderPageAsync(pdfPath, pageNumber, cancellationToken);
                    var text = (await _ocr.ExtractTextFromImageAsync(image, pageNumber, cancellationToken)).Trim();
                    if (text.Length == 0)
                    {
                        warnings.Add($"Page {pageNumber}: OCR found no text.");
                        continue;
                    }
                    pages[pages.FindIndex(p => p.PageNumber == pageNumber)] = new ExtractedPage { PageNumber = pageNumber, Text = text };
                    ocrPages++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "OCR failed for page {PageNumber} of document {DocumentId}.", pageNumber, documentId);
                    warnings.Add($"Page {pageNumber}: OCR failed.");
                }
            }
            if (sparse.Count > _options.MaxPages)
                warnings.Add($"Only the first {_options.MaxPages} of {sparse.Count} pages without a text layer were OCR'd.");

            if (ocrPages > 0)
                warnings.Remove(PdfPigTextExtractor.NoReadableTextWarning);

            var method = !pages.Any(p => p.HasText) ? TextExtractionMethod.Failed
                : pages.Any(IsSparse) ? TextExtractionMethod.Partial
                : ocrPages == pages.Count ? TextExtractionMethod.Ocr
                : ocrPages > 0 ? TextExtractionMethod.Hybrid
                : native.Method;

            _logger.LogInformation("OCR read {OcrPages} of {SparsePages} pages without a text layer in document {DocumentId}.",
                ocrPages, sparse.Count, documentId);
            return Copy(native, pages, method, warnings);
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    private bool IsSparse(ExtractedPage page) => page.CharacterCount < _options.MinNativeCharactersPerPage;

    private TextExtractionResult Copy(TextExtractionResult native, IReadOnlyList<ExtractedPage> pages, TextExtractionMethod method, List<string> warnings) => new()
    {
        DocumentId = native.DocumentId,
        ExtractionVersion = native.ExtractionVersion,
        Method = method,
        Pages = pages,
        Warnings = warnings,
        Quality = new TextExtractionQuality
        {
            AverageConfidence = pages.Count == 0 ? 0 : pages.Count(p => p.HasText) / (double)pages.Count,
            PagesWithLowConfidence = pages.Count(IsSparse)
        }
    };
}
