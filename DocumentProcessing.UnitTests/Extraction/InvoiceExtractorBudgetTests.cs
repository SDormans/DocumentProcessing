using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Domain.Interfaces;
using DocumentProcessing.Domain.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace DocumentProcessing.UnitTests.Extraction;

public class InvoiceExtractorBudgetTests
{
    private static InvoiceExtractionResult IncompleteAnswer(decimal cost)
    {
        var result = InvoiceExtractionResult.SuccessResult("{}", new InvoiceExtractionResult { SupplierName = "Acme", SupplierNameHasValue = true });
        result.EstimatedCost = cost;
        return result;
    }

    private static (InvoiceExtractor Extractor, Guid DocumentId) Create(Mock<IAiProvider> ai, string text, decimal? budget = null)
    {
        var document = Document.Create("invoice.pdf", "application/pdf", 10, "hash", "key.pdf");
        var repository = new Mock<IDocumentRepository>();
        repository.Setup(r => r.GetByIdAsync(document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        var storage = new Mock<IFileStorage>();
        storage.Setup(s => s.OpenReadAsync("key.pdf", It.IsAny<CancellationToken>())).ReturnsAsync(() => new MemoryStream());
        var textExtractor = new Mock<IDocumentTextExtractor>();
        textExtractor.Setup(t => t.ExtractAsync(document.Id, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TextExtractionResult
            {
                DocumentId = document.Id,
                Method = TextExtractionMethod.Native,
                Pages = new List<ExtractedPage> { new() { PageNumber = 1, Text = text } }
            });
        var extractor = new InvoiceExtractor(repository.Object, storage.Object, textExtractor.Object, ai.Object,
            NullLogger<InvoiceExtractor>.Instance,
            new ValidationService(new IInvoiceValidationRule[] { new MissingRequiredFieldsValidationRule() }),
            Options.Create(new InvoiceExtractionOptions { MaxCostPerDocument = budget }));
        return (extractor, document.Id);
    }

    [Fact]
    public async Task Correction_pass_is_skipped_when_the_first_pass_reaches_the_budget()
    {
        var ai = new Mock<IAiProvider>();
        ai.Setup(a => a.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => IncompleteAnswer(0.02m));
        var (extractor, id) = Create(ai, "Invoice from Acme", budget: 0.01m);

        var result = await extractor.ImproveAsync(id);

        ai.Verify(a => a.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        result.EstimatedCost.Should().Be(0.02m);
        result.Warnings.Should().Contain(w => w.StartsWith("Correction pass skipped"));
        result.Warnings.Should().Contain(w => w.Contains("exceeded the per-document budget"));
    }

    [Fact]
    public async Task Without_a_budget_the_correction_pass_runs_and_costs_add_up()
    {
        var ai = new Mock<IAiProvider>();
        ai.Setup(a => a.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => IncompleteAnswer(0.02m));
        var (extractor, id) = Create(ai, "Invoice from Acme");

        var result = await extractor.ImproveAsync(id);

        ai.Verify(a => a.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        result.EstimatedCost.Should().Be(0.04m);
        result.Warnings.Should().NotContain(w => w.Contains("budget"));
    }

    [Fact]
    public async Task Text_beyond_the_prompt_limit_is_flagged()
    {
        var ai = new Mock<IAiProvider>();
        ai.Setup(a => a.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => IncompleteAnswer(0m));
        var (extractor, id) = Create(ai, new string('x', InvoiceExtractor.MaxPromptTextCharacters + 500));

        var result = await extractor.ExtractAsync(id);

        result.Warnings.Should().Contain(InvoiceExtractor.TruncationWarning(InvoiceExtractor.MaxPromptTextCharacters + 500));
    }

    [Fact]
    public async Task Short_text_is_not_flagged()
    {
        var ai = new Mock<IAiProvider>();
        ai.Setup(a => a.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => IncompleteAnswer(0m));
        var (extractor, id) = Create(ai, "Invoice from Acme");

        var result = await extractor.ExtractAsync(id);

        result.Warnings.Should().NotContain(w => w.Contains("truncated"));
    }
}
