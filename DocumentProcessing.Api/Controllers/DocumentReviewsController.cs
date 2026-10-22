using DocumentProcessing.Application.DTOs;
using DocumentProcessing.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DocumentProcessing.Api.Controllers;

/// <summary>
/// API voor documentreview: ophalen van detail, indienen van correcties,
/// goedkeuren, afwijzen en auditlog.
/// Alle eindpunten vereisen authenticatie; schrijf-acties vereisen de juiste rol/beleid.
/// </summary>
[ApiController]
[Route("api/documents/{documentId}/review")]
[Produces("application/json")]
[Authorize(Policy = "CanReviewDocuments")]
public class DocumentReviewsController : ControllerBase
{
    private readonly IDocumentReviewService _reviewService;
    private readonly ILogger<DocumentReviewsController> _logger;

    public DocumentReviewsController(
        IDocumentReviewService reviewService,
        ILogger<DocumentReviewsController> logger)
    {
        _reviewService = reviewService;
        _logger = logger;
    }

    private string CurrentUserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
    private string CurrentUserName => User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;

    /// <summary>
    /// Haalt de detailweergave van een document op voor review.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(DocumentDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(Guid documentId, CancellationToken cancellationToken)
    {
        var detail = await _reviewService.GetDocumentDetailAsync(documentId, cancellationToken);
        if (detail == null)
        {
            return NotFound(new { message = "Document niet gevonden." });
        }
        return Ok(detail);
    }

    /// <summary>
    /// Dien gecorrigeerde waarden in. Originele AI-output wordt bewaard.
    /// </summary>
    [HttpPut("corrections")]
    [Authorize(Policy = "CanReviewDocuments")]
    [ProducesResponseType(typeof(ReviewActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SubmitCorrections(
        Guid documentId,
        [FromBody] SubmitCorrectionsRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _reviewService.SubmitCorrectionsAsync(
            documentId, request, CurrentUserId, CurrentUserName, cancellationToken);

        if (!response.Success)
        {
            return BadRequest(response);
        }
        return Ok(response);
    }

    /// <summary>
    /// Keurt een document goed. Alleen toegestaan als er geen kritieke validatiefouten zijn.
    /// Vereist de CanApproveReject-beleid.
    /// </summary>
    [HttpPost("approve")]
    [Authorize(Policy = "CanApproveReject")]
    [ProducesResponseType(typeof(ReviewActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Approve(
        Guid documentId,
        [FromBody] ApproveRequest? request,
        CancellationToken cancellationToken)
    {
        var response = await _reviewService.ApproveAsync(
            documentId, request ?? new ApproveRequest(), CurrentUserId, CurrentUserName, cancellationToken);

        if (!response.Success)
        {
            return BadRequest(response);
        }
        return Ok(response);
    }

    /// <summary>
    /// Wijst een document af. Een reden is verplicht.
    /// Vereist de CanApproveReject-beleid.
    /// </summary>
    [HttpPost("reject")]
    [Authorize(Policy = "CanApproveReject")]
    [ProducesResponseType(typeof(ReviewActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reject(
        Guid documentId,
        [FromBody] RejectRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _reviewService.RejectAsync(
            documentId, request, CurrentUserId, CurrentUserName, cancellationToken);

        if (!response.Success)
        {
            return BadRequest(response);
        }
        return Ok(response);
    }

    /// <summary>
    /// Haalt de auditlog op voor een documentreview.
    /// </summary>
    [HttpGet("audit-log")]
    [ProducesResponseType(typeof(IEnumerable<ReviewAuditEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAuditLog(Guid documentId, CancellationToken cancellationToken)
    {
        var log = await _reviewService.GetAuditLogAsync(documentId, cancellationToken);
        return Ok(log);
    }
}
