using System.Diagnostics;
using System.Diagnostics.Metrics;
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
        var ai = new Mock<IAiProvider>();
        ai.Setup(a => a.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => InvoiceExtractionSchema.Parse(InvoiceExtractionSchemaTests.ValidJson));
        var (extractor, document) = CreateExtractor(ai.Object);
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

    [Fact]
    public async Task Provider_exception_marks_the_extraction_failed_and_is_rethrown()
    {
        var ai = new Mock<IAiProvider>();
        ai.Setup(a => a.ExtractAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("503"));
        var (extractor, document) = CreateExtractor(ai.Object);
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

        var failures = new List<long>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == ExtractionTelemetry.SourceName && instrument.Name == "documentprocessing.extraction.results")
                    l.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var t = tags.ToArray();
            if (t.Any(x => x.Key == "success" && Equals(x.Value, false)) && t.Any(x => x.Key == "error.type" && Equals(x.Value, typeof(HttpRequestException).FullName)))
                lock (failures) failures.Add(value);
        });
        meterListener.Start();

        var act = () => extractor.ExtractAsync(document.Id);

        await act.Should().ThrowAsync<HttpRequestException>();
        var root = spans.Should().ContainSingle(s => s.OperationName == "invoice.extract").Subject;
        root.Status.Should().Be(ActivityStatusCode.Error);
        root.GetTagItem("extraction.success").Should().Be(false);
        failures.Should().NotBeEmpty();
    }

    private static (InvoiceExtractor Extractor, Document Document) CreateExtractor(IAiProvider ai)
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
        var extractor = new InvoiceExtractor(repository.Object, storage.Object, textExtractor.Object, ai,
            NullLogger<InvoiceExtractor>.Instance, new ValidationService(Array.Empty<IInvoiceValidationRule>()));
        return (extractor, document);
    }
}
