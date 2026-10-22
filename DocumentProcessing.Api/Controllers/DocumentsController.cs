using DocumentProcessing.Application.DTOs;
using DocumentProcessing.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentProcessing.Api.Controllers;

[ApiController]
[Route("api/documents")]
[Produces("application/json")]
[Authorize(Policy = "CanReviewDocuments")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IDocumentService documentService,
        ILogger<DocumentsController> logger)
    {
        _documentService = documentService;
        _logger = logger;
    }

    /// <summary>
    /// Uploads a PDF document. Validates file type, size, and PDF signature.
    /// </summary>
    /// <param name="file">The PDF file to upload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The uploaded document ID and status.</returns>
    /// <response code="201">Document uploaded successfully.</response>
    /// <response code="400">Validation failed.</response>
    /// <response code="500">Internal server error.</response>
    [HttpPost]
    [AllowAnonymous]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(UploadDocumentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UploadDocument(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new ErrorResponse
            {
                TraceId = HttpContext.TraceIdentifier,
                Message = "No file provided.",
                Errors = new[] { "File is required." }
            });
        }

        _logger.LogInformation("Uploading document: {FileName} ({ContentType}, {Length} bytes)",
            file.FileName, file.ContentType, file.Length);

        using var stream = file.OpenReadStream();

        try
        {
            var response = await _documentService.UploadDocumentAsync(
                stream,
                file.FileName,
                file.ContentType,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetDocument),
                new { id = response.Id },
                response);
        }
        catch (Application.Exceptions.ValidationException ex)
        {
            return BadRequest(new ErrorResponse
            {
                TraceId = HttpContext.TraceIdentifier,
                Message = ex.Message,
                Errors = ex.Errors
            });
        }
    }

    /// <summary>
    /// Returns a list of all uploaded documents.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<DocumentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDocuments(CancellationToken cancellationToken)
    {
        var documents = await _documentService.GetDocumentsAsync(cancellationToken);
        return Ok(documents);
    }

    /// <summary>
    /// Returns a single document by its ID.
    /// </summary>
    /// <param name="id">The document ID.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(DocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocument(Guid id, CancellationToken cancellationToken)
    {
        var document = await _documentService.GetDocumentAsync(id, cancellationToken);

        if (document == null)
        {
            return NotFound(new ErrorResponse
            {
                TraceId = HttpContext.TraceIdentifier,
                Message = $"Document with ID {id} not found."
            });
        }

        return Ok(document);
    }

    /// <summary>
    /// Returns the original PDF document via an authorized endpoint.
    /// </summary>
    [HttpGet("{id:guid}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocumentContent(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var fileDto = await _documentService.GetDocumentFileAsync(id, cancellationToken);
            if (fileDto == null)
            {
                return NotFound(new ErrorResponse
                {
                    TraceId = HttpContext.TraceIdentifier,
                    Message = $"Document with ID {id} not found."
                });
            }

            return File(fileDto.Stream, fileDto.ContentType, fileDto.FileName);
        }
        catch (FileNotFoundException)
        {
            return NotFound(new ErrorResponse
            {
                TraceId = HttpContext.TraceIdentifier,
                Message = $"The PDF file for document {id} could not be found."
            });
        }
    }
}