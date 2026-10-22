using DocumentProcessing.Application.DTOs;
using DocumentProcessing.Application.Services;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace DocumentProcessing.UnitTests.Services;

public class DocumentServiceTests
{
    private readonly Mock<IDocumentRepository> _mockRepository;
    private readonly Mock<IFileStorage> _mockFileStorage;
    private readonly Mock<ILogger<DocumentService>> _mockLogger;
    private readonly DocumentService _service;

    public DocumentServiceTests()
    {
        _mockRepository = new Mock<IDocumentRepository>();
        _mockFileStorage = new Mock<IFileStorage>();
        _mockLogger = new Mock<ILogger<DocumentService>>();
        _service = new DocumentService(
            _mockRepository.Object,
            _mockFileStorage.Object,
            _mockLogger.Object,
            Options.Create(new DocumentServiceOptions { MaxFileSizeBytes = 10 * 1024 * 1024 }));
    }

    [Fact]
    public async Task GetDocumentAsync_ExistingId_ReturnsDto()
    {
        // Arrange
        var document = Document.Create("test.pdf", "application/pdf", 100, "hash123", "storage/abc.pdf");
        _mockRepository.Setup(r => r.GetByIdAsync(document.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        // Act
        var result = await _service.GetDocumentAsync(document.Id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(document.Id);
        result.OriginalFileName.Should().Be("test.pdf");
        result.Status.Should().Be("Uploaded");
    }

    [Fact]
    public async Task GetDocumentAsync_NonExistingId_ReturnsNull()
    {
        // Arrange
        _mockRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Document?)null);

        // Act
        var result = await _service.GetDocumentAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDocumentsAsync_ReturnsAllDocuments()
    {
        // Arrange
        var documents = new List<Document>
        {
            Document.Create("a.pdf", "application/pdf", 100, "hash1", "s1.pdf"),
            Document.Create("b.pdf", "application/pdf", 200, "hash2", "s2.pdf")
        };
        // Ensure different timestamps for ordering assertion
        documents[0].UpdateStatus(DocumentStatus.Uploaded); // touch to ensure CreatedAtUtc stays
        documents[1].UpdateStatus(DocumentStatus.Uploaded);
        _mockRepository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(documents as IReadOnlyList<Document>);

        // Act
        var result = await _service.GetDocumentsAsync(CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        // Ordering is handled by the repository (ORDER BY CreatedAtUtc DESC),
        // so we just verify count and content
        result.Should().BeEquivalentTo(documents.Select(d => new DocumentDto
        {
            Id = d.Id,
            OriginalFileName = d.OriginalFileName,
            ContentType = d.ContentType,
            FileSize = d.FileSize,
            Status = d.Status.ToString(),
            CreatedAtUtc = d.CreatedAtUtc
        }));
    }

    [Fact]
    public void Document_Create_SetsCorrectProperties()
    {
        // Act
        var document = Document.Create("invoice.pdf", "application/pdf", 5000, "abc-hash", "storage/key.pdf");

        // Assert
        document.Id.Should().NotBe(Guid.Empty);
        document.OriginalFileName.Should().Be("invoice.pdf");
        document.ContentType.Should().Be("application/pdf");
        document.FileSize.Should().Be(5000);
        document.ContentHash.Should().Be("abc-hash");
        document.StorageKey.Should().Be("storage/key.pdf");
        document.Status.Should().Be(DocumentStatus.Uploaded);
        document.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Document_UpdateStatus_ChangesStatus()
    {
        // Arrange
        var document = Document.Create("test.pdf", "application/pdf", 100, "hash", "key.pdf");

        // Act
        document.UpdateStatus(DocumentStatus.Processing);

        // Assert
        document.Status.Should().Be(DocumentStatus.Processing);
    }
}