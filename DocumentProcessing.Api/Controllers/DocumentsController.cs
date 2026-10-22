using DocumentProcessing.Application.DTOs;
using DocumentProcessing.Application.Services;
using DocumentProcessing.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace DocumentProcessing.Api.Controllers;

[ApiController]
[Route("api/documents")]
[Produces("application/json")]
[Authorize(Policy = "CanReviewDocuments")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly IDocumentRepository _documentRepository;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        IDocumentService documentService,
        IDocumentRepository documentRepository,
        IFileStorage fileStorage,
        ILogger<DocumentsController> logger)
    {
        _documentService = documentService;
        _documentRepository = documentRepository;
        _fileStorage = fileStorage;
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
                Message = "Geen bestand opgegeven.",
                Errors = new[] { "Bestand is verplicht." }
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
                Message = $"Document met ID {id} niet gevonden."
            });
        }

        return Ok(document);
    }

    /// <summary>
    /// Geeft het originele PDF-document terug via een geautoriseerd endpoint.
    /// Bestanden zijn niet direct via onbeveiligde URL's beschikbaar.
    /// </summary>
    [HttpGet("{id:guid}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocumentContent(Guid id, CancellationToken cancellationToken)
    {
        var document = await _documentRepository.GetByIdAsync(id, cancellationToken);

        if (document == null)
        {
            return NotFound(new ErrorResponse
            {
                TraceId = HttpContext.TraceIdentifier,
                Message = $"Document met ID {id} niet gevonden."
            });
        }

        try
        {
            var stream = await _fileStorage.OpenReadAsync(document.StorageKey, cancellationToken);
            return File(stream, MediaTypeNames.Application.Pdf, document.OriginalFileName);
        }
        catch (FileNotFoundException)
        {
            return NotFound(new ErrorResponse
            {
                TraceId = HttpContext.TraceIdentifier,
                Message = $"Het PDF-bestand voor document {id} kon niet worden gevonden."
            });
        }
    }
}