using DocumentProcessing.Application.Services;
using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace DocumentProcessing.UnitTests.Services;

public class DocumentOwnershipTests
{
    private readonly Mock<IDocumentRepository> _documents = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly Mock<IDocumentReviewRepository> _reviews = new();
    private readonly Document _owned = Document.Create("mine.pdf", "application/pdf", 10, "h1", "k1", ownerId: "alice");
    private readonly Document _foreign = Document.Create("theirs.pdf", "application/pdf", 10, "h2", "k2", ownerId: "bob");
    private readonly Document _legacy = Document.Create("legacy.pdf", "application/pdf", 10, "h3", "k3");

    public DocumentOwnershipTests()
    {
        foreach (var d in new[] { _owned, _foreign, _legacy })
            _documents.Setup(r => r.GetByIdAsync(d.Id, It.IsAny<CancellationToken>())).ReturnsAsync(d);
        _documents.Setup(r => r.GetPageAsync(It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PagedResult<Document>([], 0));
        _storage.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Stream.Null);
    }

    private DocumentService DocumentService(ICurrentUser user) => new(
        _documents.Object, _storage.Object, Mock.Of<IProcessingQueue>(), NullLogger<DocumentService>.Instance,
        Options.Create(new DocumentServiceOptions()), user);

    private DocumentReviewService ReviewService(ICurrentUser user) => new(
        _documents.Object, _reviews.Object, _storage.Object, Mock.Of<IInvoiceResultRepository>(),
        new ValidationService(Array.Empty<IInvoiceValidationRule>()), NullLogger<DocumentReviewService>.Instance, user);

    [Fact]
    public async Task Upload_RecordsCurrentUserAsOwner()
    {
        Document? added = null;
        _documents.Setup(r => r.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .Callback<Document, CancellationToken>((d, _) => added = d);
        _storage.Setup(s => s.StoreAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("stored.pdf");

        await DocumentService(new TestCurrentUser("alice", false))
            .UploadDocumentAsync(new MemoryStream("%PDF-1.7 x"u8.ToArray()), "a.pdf", "application/pdf");

        added!.OwnerId.Should().Be("alice");
    }

    [Fact]
    public async Task RestrictedUser_SeesOnlyOwnDocuments()
    {
        var service = DocumentService(new TestCurrentUser("alice", false));

        (await service.GetDocumentAsync(_owned.Id)).Should().NotBeNull();
        (await service.GetDocumentAsync(_foreign.Id)).Should().BeNull();
        (await service.GetDocumentAsync(_legacy.Id)).Should().BeNull();
        (await service.GetDocumentFileAsync(_foreign.Id)).Should().BeNull();

        await service.GetDocumentsAsync();
        _documents.Verify(r => r.GetPageAsync("alice", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _documents.Verify(r => r.GetPageAsync(null, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnrestrictedUser_SeesAllDocuments()
    {
        var service = DocumentService(new TestCurrentUser("admin", true));

        (await service.GetDocumentAsync(_foreign.Id)).Should().NotBeNull();
        (await service.GetDocumentAsync(_legacy.Id)).Should().NotBeNull();

        await service.GetDocumentsAsync();
        _documents.Verify(r => r.GetPageAsync(null, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReviewActions_OnForeignDocument_AreTreatedAsNotFound()
    {
        var service = ReviewService(new TestCurrentUser("alice", false));

        (await service.GetDocumentDetailAsync(_foreign.Id)).Should().BeNull();
        (await service.ApproveAsync(_foreign.Id, new(), "alice", "alice")).Success.Should().BeFalse();
        (await service.RejectAsync(_foreign.Id, new() { Reason = "x" }, "alice", "alice")).Success.Should().BeFalse();
        (await service.SubmitCorrectionsAsync(_foreign.Id, new() { CorrectedValues = new Dictionary<string, string> { ["SupplierName"] = "x" } }, "alice", "alice"))
            .Success.Should().BeFalse();
        (await service.GetAuditLogAsync(_foreign.Id)).Should().BeNull();

        _reviews.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _reviews.Verify(r => r.GetByDocumentIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
