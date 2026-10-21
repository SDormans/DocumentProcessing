using DocumentProcessing.Application.Services;
using DocumentProcessing.Application.Exceptions;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Interfaces;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace DocumentProcessing.UnitTests.Validators;

public class DocumentValidationTests
{
    private readonly Mock<IDocumentRepository> _mockRepository;
    private readonly Mock<IFileStorage> _mockFileStorage;
    private readonly Mock<ILogger<DocumentService>> _mockLogger;
    private readonly DocumentService _service;

    public DocumentValidationTests()
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
    public async Task UploadDocumentAsync_EmptyFile_ThrowsValidationException()
    {
        // Arrange
        var stream = new MemoryStream(Array.Empty<byte>());

        // Act
        var act = () => _service.UploadDocumentAsync(stream, "test.pdf", "application/pdf", CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.And.Errors.Should().Contain("Bestand mag niet leeg zijn.");
    }

    [Fact]
    public async Task UploadDocumentAsync_NonPdfExtension_ThrowsValidationException()
    {
        // Arrange
        var pdfContent = "%PDF-1.7"u8.ToArray();
        var stream = new MemoryStream(pdfContent);

        // Act
        var act = () => _service.UploadDocumentAsync(stream, "test.txt", "text/plain", CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.And.Errors.Should().Contain("Alleen PDF-bestanden zijn toegestaan.");
    }

    [Fact]
    public async Task UploadDocumentAsync_InvalidPdfSignature_ThrowsValidationException()
    {
        // Arrange
        var notPdf = "This is not a PDF"u8.ToArray();
        var stream = new MemoryStream(notPdf);

        // Act
        var act = () => _service.UploadDocumentAsync(stream, "test.pdf", "application/pdf", CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.And.Errors.Should().Contain(e => e.Contains("PDF-signatuur"));
    }

    [Fact]
    public async Task UploadDocumentAsync_ValidPdf_Succeeds()
    {
        // Arrange
        var pdfContent = "%PDF-1.7\nsome pdf content"u8.ToArray();
        var stream = new MemoryStream(pdfContent);

        _mockFileStorage
            .Setup(s => s.StoreAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("abc123.pdf");

        _mockRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var response = await _service.UploadDocumentAsync(stream, "test.pdf", "application/pdf", CancellationToken.None);

        // Assert
        response.Id.Should().NotBe(Guid.Empty);
        response.Status.Should().Be("Uploaded");
        _mockFileStorage.Verify(s => s.StoreAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepository.Verify(r => r.AddAsync(It.IsAny<DocumentProcessing.Domain.Entities.Document>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadDocumentAsync_NullFileName_ThrowsValidationException()
    {
        // Arrange
        var stream = new MemoryStream("%PDF-1.7"u8.ToArray());

        // Act
        var act = () => _service.UploadDocumentAsync(stream, "", "application/pdf", CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.And.Errors.Should().Contain("Bestandsnaam is verplicht.");
    }

    [Fact]
    public async Task UploadDocumentAsync_StorageThrows_ThrowsStorageException()
    {
        // Arrange
        var stream = new MemoryStream("%PDF-1.7"u8.ToArray());

        _mockFileStorage
            .Setup(s => s.StoreAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Disk full"));

        // Act
        var act = () => _service.UploadDocumentAsync(stream, "test.pdf", "application/pdf", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<StorageException>();
    }

    [Fact]
    public async Task UploadDocumentAsync_FileTooLarge_ThrowsValidationException()
    {
        // Arrange
        var smallLimit = 10L; // 10 bytes
        var oversizedContent = new byte[100];
        var stream = new MemoryStream(oversizedContent);

        var serviceWithSmallLimit = new DocumentService(
            _mockRepository.Object,
            _mockFileStorage.Object,
            _mockLogger.Object,
            Options.Create(new DocumentServiceOptions { MaxFileSizeBytes = smallLimit }));

        // Act
        var act = () => serviceWithSmallLimit.UploadDocumentAsync(stream, "test.pdf", "application/pdf", CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.And.Errors.Should().Contain(e => e.Contains("te groot"));
    }

    [Fact]
    public async Task UploadDocumentAsync_DatabaseFailure_CleansUpStoredFile()
    {
        // Arrange
        var stream = new MemoryStream("%PDF-1.7"u8.ToArray());

        _mockFileStorage
            .Setup(s => s.StoreAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("storage/abc.pdf");

        _mockRepository
            .Setup(r => r.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Database error"));

        _mockRepository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Database error"));

        // Act
        var act = () => _service.UploadDocumentAsync(stream, "test.pdf", "application/pdf", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<StorageException>();
        // Verify cleanup was attempted
        _mockFileStorage.Verify(s => s.DeleteAsync("storage/abc.pdf", It.IsAny<CancellationToken>()), Times.Once);
    }
}