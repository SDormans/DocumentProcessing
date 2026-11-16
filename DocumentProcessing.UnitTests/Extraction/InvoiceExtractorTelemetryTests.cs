using System.Diagnostics;
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

public class InvoiceExtractorTelemetryTests
{
    [Fact]
    public async Task Extraction_emits_a_span_per_pipeline_stage_under_one_parent()
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
                Pages = new List<ExtractedPage> { new() { PageNumber = 1, Text = "Invoice INV-2026-001" } }
            });
        var ai = new Mock<IAiProvider>();
        ai.Setup(a => a.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => InvoiceExtractionSchema.Parse(InvoiceExtractionSchemaTests.ValidJson));
        var extractor = new InvoiceExtractor(repository.Object, storage.Object, textExtractor.Object, ai.Object,
            NullLogger<InvoiceExtractor>.Instance, new ValidationService(Array.Empty<IInvoiceValidationRule>()));

        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ExtractionTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.GetTagItem("document.id") is Guid id ? id == document.Id : activity.Parent?.GetTagItem("document.id") is Guid p && p == document.Id)
                    lock (spans) spans.Add(activity);
            }
        };
        ActivitySource.AddActivityListener(listener);

        await extractor.ExtractAsync(document.Id);

        var root = spans.Should().ContainSingle(s => s.OperationName == "invoice.extract").Subject;
        root.GetTagItem("extraction.success").Should().Be(true);
        spans.Where(s => s.ParentSpanId == root.SpanId).Select(s => s.OperationName).Should().Equal(
            "invoice.text_extraction", "invoice.llm_extraction", "invoice.validation");
    }
}
