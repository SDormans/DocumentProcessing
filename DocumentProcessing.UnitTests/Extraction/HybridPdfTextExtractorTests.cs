using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Domain.Interfaces;
using DocumentProcessing.Infrastructure.Ocr;
using DocumentProcessing.Infrastructure.TextExtraction;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace DocumentProcessing.UnitTests.Extraction;

public class HybridPdfTextExtractorTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static HybridPdfTextExtractor Create(IOcrProvider ocr, IPdfPageRenderer renderer, OcrOptions? options = null) => new(
        new PdfPigTextExtractor(NullLogger<PdfPigTextExtractor>.Instance),
        ocr,
        renderer,
        Options.Create(options ?? new OcrOptions()),
        NullLogger<HybridPdfTextExtractor>.Instance);

    private static FileStream Open(string name) => File.OpenRead(Path.Combine(Fixtures, name));

    private static Mock<IPdfPageRenderer> Renderer()
    {
        var renderer = new Mock<IPdfPageRenderer>();
        renderer.Setup(r => r.RenderPageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream([1, 2, 3]));
        return renderer;
    }

    [Fact]
    public async Task Native_text_pdf_is_not_sent_to_ocr()
    {
        var ocr = new Mock<IOcrProvider>();
        ocr.Setup(o => o.IsAvailable).Returns(true);
        await using var pdf = Open("native-invoice.pdf");

        var result = await Create(ocr.Object, Renderer().Object).ExtractAsync(Guid.NewGuid(), pdf);

        result.Method.Should().Be(TextExtractionMethod.Native);
        result.FullText.Should().Contain("NAT-2026-001");
        ocr.Verify(o => o.ExtractTextFromImageAsync(It.IsAny<Stream>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Scanned_pdf_pages_are_read_with_ocr()
    {
        var ocr = new Mock<IOcrProvider>();
        ocr.Setup(o => o.IsAvailable).Returns(true);
        ocr.Setup(o => o.ExtractTextFromImageAsync(It.IsAny<Stream>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Invoice No: SCAN-2026-001\n");
        await using var pdf = Open("scanned-invoice.pdf");

        var result = await Create(ocr.Object, Renderer().Object).ExtractAsync(Guid.NewGuid(), pdf);

        result.Method.Should().Be(TextExtractionMethod.Ocr);
        result.FullText.Should().Be("Invoice No: SCAN-2026-001");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Scanned_pdf_without_ocr_reports_why_it_has_no_text()
    {
        var ocr = new Mock<IOcrProvider>();
        ocr.Setup(o => o.IsAvailable).Returns(false);
        await using var pdf = Open("scanned-invoice.pdf");

        var result = await Create(ocr.Object, Renderer().Object).ExtractAsync(Guid.NewGuid(), pdf);

        result.HasAnyText.Should().BeFalse();
        result.Warnings.Should().Contain(w => w.Contains("OCR is not available"));
    }

    [Fact]
    public async Task Ocr_failure_on_a_page_becomes_a_warning()
    {
        var ocr = new Mock<IOcrProvider>();
        ocr.Setup(o => o.IsAvailable).Returns(true);
        ocr.Setup(o => o.ExtractTextFromImageAsync(It.IsAny<Stream>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("tesseract did not finish"));
        await using var pdf = Open("scanned-invoice.pdf");

        var result = await Create(ocr.Object, Renderer().Object).ExtractAsync(Guid.NewGuid(), pdf);

        result.Method.Should().Be(TextExtractionMethod.Failed);
        result.Warnings.Should().Contain("Page 1: OCR failed.");
    }

    [Fact]
    public async Task Real_tesseract_reads_the_scanned_fixture_when_installed()
    {
        var options = new OcrOptions { Dpi = 150 };
        var ocr = new TesseractCliOcrProvider(Options.Create(options), NullLogger<TesseractCliOcrProvider>.Instance);
        if (!ocr.IsAvailable)
            return; // CI installs tesseract-ocr and poppler-utils; local runs without them skip this check.
        await using var pdf = Open("scanned-invoice.pdf");

        var result = await Create(ocr, new PdftoppmPageRenderer(Options.Create(options)), options).ExtractAsync(Guid.NewGuid(), pdf);

        result.Method.Should().Be(TextExtractionMethod.Ocr);
        result.FullText.Should().Contain("SCAN-2026-001").And.Contain("Scanned Supplier BV");
    }
}
