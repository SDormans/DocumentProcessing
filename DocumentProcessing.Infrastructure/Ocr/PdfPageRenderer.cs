using Microsoft.Extensions.Logging;
using PdfSharp.Pdf;
using SkiaSharp;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentProcessing.Infrastructure.Ocr;

public readonly record struct PageDimensions(int Width, int Height);

public interface IPdfPageRenderer
{
    Task<(PageDimensions pageSize, Stream imageStream)> RenderPageAsync(
        PdfDocument pdfDocument,
        int pageNumber,
        float dpi,
        CancellationToken cancellationToken = default);
}

public class PdfPageRenderer : IPdfPageRenderer
{
    private readonly ILogger<PdfPageRenderer> _logger;

    public PdfPageRenderer(ILogger<PdfPageRenderer> logger)
    {
        _logger = logger;
    }

    public Task<(PageDimensions pageSize, Stream imageStream)> RenderPageAsync(
        PdfDocument pdfDocument,
        int pageNumber,
        float dpi,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (pageNumber < 1 || pageNumber > pdfDocument.PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber),
                $"Page number {pageNumber} is out of range (1-{pdfDocument.PageCount})");
        }

        var pdfPage = pdfDocument.Pages[pageNumber - 1];

        var pageWidthPt = pdfPage.Width.Point;
        var pageHeightPt = pdfPage.Height.Point;

        var pageWidthPx = (int)Math.Ceiling(pageWidthPt * dpi / 72.0);
        var pageHeightPx = (int)Math.Ceiling(pageHeightPt * dpi / 72.0);

        _logger.LogDebug(
            "Rendering page {PageNumber} of {TotalPages}: {Width}x{Height} pixels at {DPI} DPI",
            pageNumber, pdfDocument.PageCount, pageWidthPx, pageHeightPx, dpi);

        // Render using SkiaSharp to ensure cross-platform compatibility across Windows, Linux, and Docker
        using var bitmap = new SKBitmap(pageWidthPx, pageHeightPx, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        var pngStream = new MemoryStream();
        data.SaveTo(pngStream);
        pngStream.Position = 0;

        _logger.LogInformation(
            "Rendered page {PageNumber}: {Width}x{Height} pixels, {ByteCount} bytes (placeholder image)",
            pageNumber, pageWidthPx, pageHeightPx, pngStream.Length);

        return Task.FromResult((new PageDimensions(pageWidthPx, pageHeightPx), (Stream)pngStream));
    }
}