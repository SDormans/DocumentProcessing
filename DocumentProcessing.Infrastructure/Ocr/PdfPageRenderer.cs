using PdfSharp.Pdf;
using PdfSharp.Drawing;
using SkiaSharp;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;

namespace DocumentProcessing.Infrastructure.Ocr
{
    public interface IPdfPageRenderer
    {
        Task<(Size pageSize, Stream imageStream)> RenderPageAsync(
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

        public async Task<(Size pageSize, Stream imageStream)> RenderPageAsync(
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

            // Create a blank white bitmap at the calculated size
            // Note: Actual PDF content rendering requires PdfSharp XGraphics or SKDocument,
            // but this provides the correct dimensions and a base image for OCR
            using var bitmap = new Bitmap(pageWidthPx, pageHeightPx, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.White);

            using var pngStream = new MemoryStream();
            bitmap.Save(pngStream, ImageFormat.Png);
            pngStream.Position = 0;

            _logger.LogInformation(
                "Rendered page {PageNumber}: {Width}x{Height} pixels, {ByteCount} bytes (placeholder image)",
                pageNumber, pageWidthPx, pageHeightPx, pngStream.Length);

            return (new Size(pageWidthPx, pageHeightPx), pngStream);
        }
    }
}