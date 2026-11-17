using DocumentProcessing.Application.DTOs;
using DocumentProcessing.Application.Services;
using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Enums;
using DocumentProcessing.Domain.Exceptions;
using DocumentProcessing.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace DocumentProcessing.UnitTests.Services;

public class DocumentReviewServiceTests
{
    private readonly Mock<IDocumentRepository> _documents = new();
    private readonly Mock<IDocumentReviewRepository> _reviews = new();
    private readonly Mock<IInvoiceResultRepository> _results = new();
    private readonly Document _document = new(Guid.NewGuid(), "invoice.pdf", "application/pdf", 10, "hash", "key", DocumentStatus.NeedsReview, DateTime.UtcNow);
    private DocumentReview? _added;

    public DocumentReviewServiceTests()
    {
        _documents.Setup(r => r.GetByIdAsync(_document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_document);
        _reviews.Setup(r => r.GetByDocumentIdAsync(_document.Id, It.IsAny<CancellationToken>())).ReturnsAsync((DocumentReview?)null);
        _reviews.Setup(r => r.AddAsync(It.IsAny<DocumentReview>(), It.IsAny<CancellationToken>()))
            .Callback<DocumentReview, CancellationToken>((review, _) => _added = review)
            .Returns(Task.CompletedTask);
        _results.Setup(r => r.GetByDocumentIdAsync(_document.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvoiceExtractionResultEntity { DocumentId = _document.Id, ExtractionVersion = 1, SupplierName = "Acme" });
    }

    private DocumentReviewService Service => new(
        _documents.Object,
        _reviews.Object,
        Mock.Of<IFileStorage>(),
        _results.Object,
        new ValidationService(Array.Empty<IInvoiceValidationRule>()),
        NullLogger<DocumentReviewService>.Instance,
        Mock.Of<ICurrentUser>(u => u.HasUnrestrictedDocumentAccess));

    [Fact]
    public async Task Approve_WithoutPriorCorrections_CreatesReviewFromExtraction()
    {
        var response = await Service.ApproveAsync(_document.Id, new ApproveRequest(), "user-1", "reviewer");

        response.Success.Should().BeTrue();
        _added.Should().NotBeNull();
        _added!.Status.Should().Be(ReviewStatus.Approved);
        _added.OriginalAiOutputJson.Should().Contain("Acme");
        _added.ApprovedValuesJson.Should().Contain("\"SupplierName\":\"Acme\"");
        _document.Status.Should().Be(DocumentStatus.Completed);
    }

    [Fact]
    public async Task Approve_ValidatesTheCorrectedValues_NotTheOriginalExtraction()
    {
        var review = new DocumentReview(Guid.NewGuid(), _document.Id, "{}");
        review.SetCorrections("{\"SupplierName\":\"\"}", "user-1");
        _reviews.Setup(r => r.GetByDocumentIdAsync(_document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(review);
        var service = ServiceWith(new MissingRequiredFieldsValidationRule(new[] { "SupplierName" }));

        var response = await service.ApproveAsync(_document.Id, new ApproveRequest(), "user-1", "reviewer");

        response.Success.Should().BeFalse();
        response.Error.Should().Be(ReviewActionError.Invalid);
        response.Errors.Should().ContainSingle().Which.Should().Contain("SupplierName");
        review.Status.Should().Be(ReviewStatus.Corrected);
        _document.Status.Should().Be(DocumentStatus.NeedsReview);
    }

    [Fact]
    public async Task Approve_SucceedsWhenCorrectionsFixTheValidationErrors()
    {
        _results.Setup(r => r.GetByDocumentIdAsync(_document.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvoiceExtractionResultEntity { DocumentId = _document.Id, ExtractionVersion = 1 });
        var review = new DocumentReview(Guid.NewGuid(), _document.Id, "{}");
        review.SetCorrections("{\"SupplierName\":\"Acme\"}", "user-1");
        _reviews.Setup(r => r.GetByDocumentIdAsync(_document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(review);

        var response = await ServiceWith(new MissingRequiredFieldsValidationRule(new[] { "SupplierName" })).ApproveAsync(_document.Id, new ApproveRequest(), "user-1", "reviewer");

        response.Success.Should().BeTrue();
        review.ApprovedValuesJson.Should().Contain("\"SupplierName\":\"Acme\"");
    }

    [Fact]
    public async Task Corrections_AreMergedWithEarlierOnes_AndShownInTheDetail()
    {
        var review = new DocumentReview(Guid.NewGuid(), _document.Id, "{}");
        _reviews.Setup(r => r.GetByDocumentIdAsync(_document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(review);

        (await Service.SubmitCorrectionsAsync(_document.Id, Corrections(("InvoiceNumber", "INV-1")), "user-1", "reviewer")).Success.Should().BeTrue();
        (await Service.SubmitCorrectionsAsync(_document.Id, Corrections(("TotalAmount", "121.5")), "user-1", "reviewer")).Success.Should().BeTrue();

        var detail = await Service.GetDocumentDetailAsync(_document.Id);
        var fields = detail!.ExtractedFields.ToDictionary(f => f.FieldName);
        fields.Keys.Should().Equal(ReviewFields.Names);
        fields["SupplierName"].EffectiveValue.Should().Be("Acme");
        fields["SupplierName"].IsCorrected.Should().BeFalse();
        fields["InvoiceNumber"].CorrectedValue.Should().Be("INV-1");
        fields["InvoiceNumber"].IsCorrected.Should().BeTrue();
        fields["TotalAmount"].EffectiveValue.Should().Be("121.5");
    }

    [Theory]
    [InlineData("Unknown", "x", "Unknown field")]
    [InlineData("InvoiceDate", "01-10-2026", "yyyy-MM-dd")]
    [InlineData("TotalAmount", "12,50", "number")]
    [InlineData("Currency", "Euro", "currency code")]
    public async Task Corrections_WithInvalidValues_AreRejectedWithoutSaving(string field, string value, string expectedError)
    {
        var response = await Service.SubmitCorrectionsAsync(_document.Id, Corrections((field, value)), "user-1", "reviewer");

        response.Success.Should().BeFalse();
        response.Error.Should().Be(ReviewActionError.Invalid);
        response.Errors.Should().ContainSingle().Which.Should().Contain(expectedError);
        _added.Should().BeNull();
        _reviews.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Corrections_LongerThanTheLimit_AreRejected()
    {
        var response = await Service.SubmitCorrectionsAsync(
            _document.Id, Corrections(("SupplierName", new string('x', ReviewFields.MaxValueLength + 1))), "user-1", "reviewer");

        response.Error.Should().Be(ReviewActionError.Invalid);
    }

    [Fact]
    public async Task Actions_OnMissingDocument_AreReportedAsNotFound()
    {
        var missing = Guid.NewGuid();

        (await Service.ApproveAsync(missing, new ApproveRequest(), "user-1", "reviewer")).Error.Should().Be(ReviewActionError.NotFound);
        (await Service.RejectAsync(missing, new RejectRequest { Reason = "x" }, "user-1", "reviewer")).Error.Should().Be(ReviewActionError.NotFound);
        (await Service.SubmitCorrectionsAsync(missing, Corrections(("SupplierName", "x")), "user-1", "reviewer")).Error.Should().Be(ReviewActionError.NotFound);
        (await Service.GetAuditLogAsync(missing)).Should().BeNull();
    }

    private static SubmitCorrectionsRequest Corrections(params (string Field, string Value)[] values) =>
        new() { CorrectedValues = values.ToDictionary(v => v.Field, v => v.Value) };

    private DocumentReviewService ServiceWith(params IInvoiceValidationRule[] rules) => new(
        _documents.Object,
        _reviews.Object,
        Mock.Of<IFileStorage>(),
        _results.Object,
        new ValidationService(rules),
        NullLogger<DocumentReviewService>.Instance,
        Mock.Of<ICurrentUser>(u => u.HasUnrestrictedDocumentAccess));

    [Fact]
    public async Task Reject_WithoutPriorCorrections_CreatesReview()
    {
        var response = await Service.RejectAsync(_document.Id, new RejectRequest { Reason = "Not an invoice" }, "user-1", "reviewer");

        response.Success.Should().BeTrue();
        _added!.Status.Should().Be(ReviewStatus.Rejected);
        _document.Status.Should().Be(DocumentStatus.Failed);
    }

    [Theory]
    [InlineData(DocumentStatus.Queued)]
    [InlineData(DocumentStatus.Processing)]
    [InlineData(DocumentStatus.Failed)]
    [InlineData(DocumentStatus.Completed)]
    public async Task Decisions_OnDocumentNotAwaitingReview_AreRefusedWithoutSaving(DocumentStatus status)
    {
        var document = new Document(Guid.NewGuid(), "invoice.pdf", "application/pdf", 10, "hash", "key", status, DateTime.UtcNow);
        _documents.Setup(r => r.GetByIdAsync(document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(document);

        (await Service.ApproveAsync(document.Id, new ApproveRequest(), "user-1", "reviewer")).Success.Should().BeFalse();
        (await Service.RejectAsync(document.Id, new RejectRequest { Reason = "x" }, "user-1", "reviewer")).Success.Should().BeFalse();
        (await Service.SubmitCorrectionsAsync(document.Id, new SubmitCorrectionsRequest { CorrectedValues = new Dictionary<string, string> { ["SupplierName"] = "x" } }, "user-1", "reviewer"))
            .Success.Should().BeFalse();

        _added.Should().BeNull();
        document.Status.Should().Be(status);
        _reviews.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Approve_WithoutExtractionResult_IsRefused()
    {
        _results.Setup(r => r.GetByDocumentIdAsync(_document.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((InvoiceExtractionResultEntity?)null);

        var response = await Service.ApproveAsync(_document.Id, new ApproveRequest(), "user-1", "reviewer");

        response.Success.Should().BeFalse();
        _document.Status.Should().Be(DocumentStatus.NeedsReview);
        _reviews.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Approve_LosingConcurrentDecision_ReturnsFailure()
    {
        _reviews.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("conflict", new Exception()));

        var response = await Service.ApproveAsync(_document.Id, new ApproveRequest(), "user-1", "reviewer");

        response.Success.Should().BeFalse();
        _documents.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
