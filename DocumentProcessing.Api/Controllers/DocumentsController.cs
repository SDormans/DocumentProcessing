using DocumentProcessing.Domain.Interfaces;
using DocumentProcessing.Domain.Entities;
using DocumentProcessing.Application.DTOs;
using DocumentProcessing.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DocumentProcessing.Api.Extensions;
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
    [EnableRateLimiting(RateLimitingSetup.UploadPolicy)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(UploadDocumentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
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

        _logger.LogInformation("Uploading PDF document ({Length} bytes)", file.Length);

        using var stream = file.OpenReadStream();

        // Validation failures surface as 400, 413 or 415 through ErrorHandlingMiddleware.
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

    /// <summary>
    /// Returns a page of documents, newest first. The total count is in the <c>X-Total-Count</c> header.
    /// </summary>
    /// <param name="skip">Documents to skip.</param>
    /// <param name="take">Page size, 1 to 200 (default 50).</param>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<DocumentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDocuments(
        [FromQuery] int skip = 0,
        [FromQuery] int take = DocumentService.DefaultPageSize,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0 || take < 1 || take > DocumentService.MaxPageSize)
            return BadRequest(Error($"skip must be 0 or more and take between 1 and {DocumentService.MaxPageSize}."));
        DocumentStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<DocumentStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                return BadRequest(Error($"Unknown status '{status}'."));
            statusFilter = parsed;
        }

        var page = await _documentService.GetDocumentPageAsync(new DocumentQuery(statusFilter, search, skip, take), cancellationToken);
        Response.Headers[TotalCountHeader] = page.TotalCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Ok(page.Items);
    }

    public const string TotalCountHeader = "X-Total-Count";

    /// <summary>Re-runs extraction for a document awaiting review (e.g. after a model or prompt change).</summary>
    [HttpPost("{id:guid}/reprocess")]
    [Authorize(Policy = "IsAdmin")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reprocess(Guid id, CancellationToken cancellationToken)
    {
        return await _documentService.ReprocessAsync(id, cancellationToken) switch
        {
            ReprocessResult.Queued => Accepted(),
            ReprocessResult.NotFound => NotFound(),
            _ => Conflict(new { message = "Only documents awaiting review can be reprocessed." })
        };
    }

    /// <summary>Approved invoice values as CSV, for import into accounting.</summary>
    [HttpGet("export.csv")]
    [Produces("text/csv")]
    public async Task<IActionResult> ExportApproved(
        [FromServices] IDocumentReviewService reviewService, CancellationToken cancellationToken)
    {
        var csv = await reviewService.ExportApprovedCsvAsync(cancellationToken);
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray(),
            "text/csv; charset=utf-8", "approved-invoices.csv");
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
    /// Re-queues a failed document with the given model type, e.g. to switch from an unavailable local model to the cloud model.
    /// </summary>
    [HttpPost("{id:guid}/retry-with-model")]
    [EnableRateLimiting(RateLimitingSetup.UploadPolicy)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RetryWithModel(Guid id, [FromBody] RetryWithModelRequest request, CancellationToken cancellationToken)
    {
        return await _documentService.RetryWithModelAsync(id, request.ModelType, cancellationToken) switch
        {
            ModelSwitchResult.Requeued => Accepted(new { id, status = "Queued", aiModelType = request.ModelType }),
            ModelSwitchResult.NotFailed => Conflict(Error("Only failed documents can be retried with another model.")),
            ModelSwitchResult.InvalidModel => BadRequest(Error($"Unknown model type '{request.ModelType}'. Use 'Cloud' or 'Local'.")),
            ModelSwitchResult.ModelUnavailable => BadRequest(Error($"The {request.ModelType} model is not configured on this server.")),
            _ => NotFound(Error("Document not found."))
        };
    }

    /// <summary>
    /// Re-queues a failed document for processing (admins only).
    /// </summary>
    [HttpPost("{id:guid}/requeue")]
    [Authorize(Policy = "IsAdmin")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequeueDocument(Guid id, CancellationToken cancellationToken)
    {
        return await _documentService.RequeueAsync(id, cancellationToken) switch
        {
            RequeueResult.Requeued => Accepted(new { id, status = "Queued" }),
            RequeueResult.NotFailed => Conflict(Error("Only failed documents can be re-queued.")),
            _ => NotFound(Error("Document not found."))
        };
    }

    /// <summary>The shared review queue: unassigned documents awaiting review from every owner, oldest first.</summary>
    [HttpGet("review-queue")]
    [Authorize(Policy = "CanApproveReject")]
    [ProducesResponseType(typeof(IEnumerable<DocumentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReviewQueue(
        [FromQuery] int skip = 0,
        [FromQuery] int take = DocumentService.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var page = await _documentService.GetReviewQueueAsync(skip, take, cancellationToken);
        Response.Headers[TotalCountHeader] = page.TotalCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Ok(page.Items);
    }

    /// <summary>Takes a document from the review queue; it is then visible to and reviewable by the claimer.</summary>
    [HttpPost("{id:guid}/claim")]
    [Authorize(Policy = "CanApproveReject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ClaimDocument(Guid id, CancellationToken cancellationToken) =>
        AssignmentResponse(await _documentService.ClaimAsync(id, cancellationToken));

    /// <summary>Assigns a document awaiting review to a user, or returns it to the queue with a null <c>userId</c>.</summary>
    [HttpPut("{id:guid}/assignee")]
    [Authorize(Policy = "IsAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AssignDocument(Guid id, [FromBody] AssignDocumentRequest request, CancellationToken cancellationToken)
    {
        if (request.UserId is { Length: > 450 })
            return BadRequest(Error("userId is too long."));
        return AssignmentResponse(await _documentService.AssignAsync(id, request.UserId, cancellationToken));
    }

    private IActionResult AssignmentResponse(AssignmentResult result) => result switch
    {
        AssignmentResult.Assigned => NoContent(),
        AssignmentResult.NotAwaitingReview => Conflict(Error("Only documents awaiting review can be assigned.")),
        AssignmentResult.AssignedToSomeoneElse => Conflict(Error("Another reviewer already claimed this document.")),
        _ => NotFound(Error("Document not found."))
    };

    public sealed record AssignDocumentRequest(string? UserId);

    /// <summary>
    /// Returns the original PDF document via an authorized endpoint.
    /// </summary>
    [HttpGet("{id:guid}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status422UnprocessableEntity)]
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

            if (fileDto.CouldNotSanitize)
                return UnprocessableEntity(Error("The PDF could not be made safe to display."));

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

    /// <summary>Erasure request: deletes the document, its file, results, review and audit trail.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "IsAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDocument(
        Guid id,
        [FromServices] IDocumentDeletion deletion,
        CancellationToken cancellationToken)
    {
        return await deletion.DeleteAsync(id, "AdminRequest", cancellationToken) ? NoContent() : NotFound();
    }

    private ErrorResponse Error(string message) => new() { TraceId = HttpContext.TraceIdentifier, Message = message };
}
