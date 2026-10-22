using DocumentProcessing.Api.Controllers;
using DocumentProcessing.Application.DTOs;
using DocumentProcessing.Application.Services;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Domain.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Moq;
using System.IO;
using System.Text.Json;
using Xunit;

namespace DocumentProcessing.IntegrationTests;

public class DocumentsControllerTests
{
    private readonly DocumentsController _controller;
    private readonly Mock<IDocumentService> _mockService;

    public DocumentsControllerTests()
    {
        _mockService = new Mock<IDocumentService>();
        _controller = new DocumentsController(
            _mockService.Object,
            new Mock<Microsoft.Extensions.Logging.ILogger<DocumentsController>>().Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { TraceIdentifier = "test-trace-id" }
            }
        };
    }

    [Fact]
    public void DocumentsController_RequiresAuthenticatedUsers()
    {
        var controllerAuthorization = typeof(DocumentsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>();
        var uploadMethod = typeof(DocumentsController).GetMethod(nameof(DocumentsController.UploadDocument))!;

        controllerAuthorization.Should().Contain(attribute => attribute.Policy == "CanReviewDocuments");
        uploadMethod.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true).Should().BeEmpty();
    }

    [Fact]
    public async Task UploadDocument_ValidPdf_ReturnsCreated()
    {
        // Arrange
        var pdfContent = "%PDF-1.7\nsome content"u8.ToArray();
        var formFile = new FormFile(new MemoryStream(pdfContent), 0, pdfContent.Length, "file", "invoice.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        _mockService.Setup(s => s.UploadDocumentAsync(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UploadDocumentResponse
            {
                Id = Guid.NewGuid(),
                Status = "Uploaded",
                CreatedAtUtc = DateTime.UtcNow
            });

        // Act
        var result = await _controller.UploadDocument(formFile, CancellationToken.None);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Which;
        createdResult.StatusCode.Should().Be(StatusCodes.Status201Created);
    }

    [Fact]
    public async Task UploadDocument_EmptyFile_ReturnsBadRequest()
    {
        // Arrange
        var formFile = new FormFile(new MemoryStream(Array.Empty<byte>()), 0, 0, "file", "empty.pdf");

        // Act
        var result = await _controller.UploadDocument(formFile, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UploadDocument_NoFile_ReturnsBadRequest()
    {
        // Act
        var result = await _controller.UploadDocument(null!, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetDocuments_ReturnsOk()
    {
        // Arrange
        _mockService.Setup(s => s.GetDocumentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DocumentDto>());

        // Act
        var result = await _controller.GetDocuments(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetDocumentById_ExistingId_ReturnsDocument()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var dto = new DocumentDto
        {
            Id = docId,
            OriginalFileName = "test.pdf",
            ContentType = "application/pdf",
            FileSize = 100,
            Status = "Uploaded",
            CreatedAtUtc = DateTime.UtcNow
        };
        _mockService.Setup(s => s.GetDocumentAsync(docId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        // Act
        var result = await _controller.GetDocument(docId, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Which;
        var returnedDto = okResult.Value.Should().BeOfType<DocumentDto>().Which;
        returnedDto.Id.Should().Be(docId);
    }

    [Fact]
    public async Task GetDocumentById_NonExistingId_ReturnsNotFound()
    {
        // Arrange
        _mockService.Setup(s => s.GetDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentDto?)null);

        // Act
        var result = await _controller.GetDocument(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }
}