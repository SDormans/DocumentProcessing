using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Domain.Interfaces;
using DocumentProcessing.Domain.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace DocumentProcessing.UnitTests.Extraction;

public class InvoiceExtractorTests
{
    private readonly Document _document = Document.Create("invoice.pdf", "application/pdf", 10, "hash", "key.pdf");
    private readonly Mock<IDocumentRepository> _repository = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly Mock<IDocumentTextExtractor> _textExtractor = new();
    private readonly Mock<IAiProvider> _ai = new();

    public InvoiceExtractorTests()
    {
        _repository.Setup(r => r.GetByIdAsync(_document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_document);
        _storage.Setup(s => s.OpenReadAsync("key.pdf", It.IsAny<CancellationToken>())).ReturnsAsync(() => new MemoryStream());
        SetupText("Invoice INV-2026-001 total EUR 121.00");
    }

    private void SetupText(string text) =>
        _textExtractor.Setup(t => t.ExtractAsync(_document.Id, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TextExtractionResult
            {
                DocumentId = _document.Id,
                Method = TextExtractionMethod.Native,
                Pages = string.IsNullOrEmpty(text)
                    ? new List<ExtractedPage>()
                    : new List<ExtractedPage> { new() { PageNumber = 1, Text = text } }
            });

    private InvoiceExtractor CreateExtractor() => new(
        _repository.Object, _storage.Object, _textExtractor.Object, _ai.Object,
        NullLogger<InvoiceExtractor>.Instance,
        new ValidationService(new IInvoiceValidationRule[] { new InconsistentInvoiceAmountsValidationRule() }));

    [Fact]
    public async Task ExtractAsync_SendsDocumentTextAndReturnsValidatedResult()
    {
        string? prompt = null;
        _ai.Setup(a => a.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => prompt = p)
            .ReturnsAsync(() => InvoiceExtractionSchema.Parse(InvoiceExtractionSchemaTests.ValidJson));

        var result = await CreateExtractor().ExtractAsync(_document.Id);

        result.Success.Should().BeTrue();
        result.ExtractionMethod.Should().Be("Llm+Native");
        result.PageCount.Should().Be(1);
        prompt.Should().Contain("INV-2026-001").And.Contain("<document>");
    }

    [Fact]
    public async Task ExtractAsync_NoText_FailsWithoutCallingAi()
    {
        SetupText(string.Empty);

        var result = await CreateExtractor().ExtractAsync(_document.Id);

        result.Success.Should().BeFalse();
        _ai.Verify(a => a.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_UnknownDocument_Fails()
    {
        var result = await CreateExtractor().ExtractAsync(Guid.NewGuid());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ImproveAsync_ValidationErrors_RunsCorrectionPass()
    {
        var inconsistent = InvoiceExtractionSchemaTests.ValidJson.Replace("121.00", "999.00");
        _ai.SetupSequence(a => a.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InvoiceExtractionSchema.Parse(inconsistent))
            .ReturnsAsync(InvoiceExtractionSchema.Parse(InvoiceExtractionSchemaTests.ValidJson));

        var result = await CreateExtractor().ImproveAsync(_document.Id);

        result.ExtractionVersion.Should().Be(2);
        result.TotalAmount.Should().Be(121.00m);
        _ai.Verify(a => a.ExtractAsync(It.Is<string>(p => p.Contains("<previous_answer>")), It.IsAny<CancellationToken>()), Times.Once);
    }
}
