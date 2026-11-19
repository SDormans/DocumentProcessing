using DocumentProcessing.Application.DTOs;
using DocumentProcessing.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DocumentProcessing.Api.Controllers;

/// <summary>
/// Document review API: detail, corrections,
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
    /// Returns the review detail of a document.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(DocumentDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(Guid documentId, CancellationToken cancellationToken)
    {
        var detail = await _reviewService.GetDocumentDetailAsync(documentId, cancellationToken);
        if (detail == null)
        {
            return NotFound(new { message = "Document not found." });
        }
        return Ok(detail);
    }

    /// <summary>
    /// Submits corrected values. The original AI output is kept.
    /// </summary>
    [HttpPut("corrections")]
    [Authorize(Policy = "CanReviewDocuments")]
    [ProducesResponseType(typeof(ReviewActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ReviewActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ReviewActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitCorrections(
        Guid documentId,
        [FromBody] SubmitCorrectionsRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _reviewService.SubmitCorrectionsAsync(
            documentId, request, CurrentUserId, CurrentUserName, cancellationToken);

        return ToActionResult(response);
    }

    /// <summary>
    /// Approves a document. Only allowed when no validation errors remain.
    /// Vereist de CanApproveReject-beleid.
    /// </summary>
    [HttpPost("approve")]
    [Authorize(Policy = "CanApproveReject")]
    [ProducesResponseType(typeof(ReviewActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ReviewActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ReviewActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(
        Guid documentId,
        [FromBody] ApproveRequest? request,
        CancellationToken cancellationToken)
    {
        var response = await _reviewService.ApproveAsync(
            documentId, request ?? new ApproveRequest(), CurrentUserId, CurrentUserName, cancellationToken);

        return ToActionResult(response);
    }

    /// <summary>
    /// Rejects a document. A reason is required.
    /// Vereist de CanApproveReject-beleid.
    /// </summary>
    [HttpPost("reject")]
    [Authorize(Policy = "CanApproveReject")]
    [ProducesResponseType(typeof(ReviewActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ReviewActionResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ReviewActionResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(
        Guid documentId,
        [FromBody] RejectRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _reviewService.RejectAsync(
            documentId, request, CurrentUserId, CurrentUserName, cancellationToken);

        return ToActionResult(response);
    }

    /// <summary>
    /// Returns the audit log of a document review.
    /// </summary>
    [HttpGet("audit-log")]
    [ProducesResponseType(typeof(IEnumerable<ReviewAuditEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAuditLog(Guid documentId, CancellationToken cancellationToken)
    {
        var log = await _reviewService.GetAuditLogAsync(documentId, cancellationToken);
        if (log == null)
        {
            return NotFound(new { message = "Document not found." });
        }
        return Ok(log);
    }

    private IActionResult ToActionResult(ReviewActionResponse response) => response.Error switch
    {
        _ when response.Success => Ok(response),
        ReviewActionError.NotFound => NotFound(response),
        ReviewActionError.Forbidden => StatusCode(StatusCodes.Status403Forbidden, response),
        ReviewActionError.Conflict => Conflict(response),
        _ => BadRequest(response)
    };
}
