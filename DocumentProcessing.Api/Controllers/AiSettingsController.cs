using DocumentProcessing.Application.Services;
using DocumentProcessing.Application.Services.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentProcessing.Api.Controllers;

/// <summary>The signed-in user's choice between the cloud and the local AI model.</summary>
[ApiController]
[Route("api/settings/ai")]
[Produces("application/json")]
[Authorize(Policy = "CanReviewDocuments")]
public class AiSettingsController : ControllerBase
{
    private readonly AiModelSettingsService _settings;
    private readonly ICurrentUser _currentUser;

    public AiSettingsController(AiModelSettingsService settings, ICurrentUser currentUser)
    {
        _settings = settings;
        _currentUser = currentUser;
    }

    /// <summary>Returns the selected model type and the configured options; <c>check=true</c> also probes each enabled model.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(AiModelSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] bool check, CancellationToken cancellationToken) =>
        Ok(await _settings.GetAsync(_currentUser.UserId, check, cancellationToken));

    /// <summary>Saves the model type used for this user's future uploads.</summary>
    [HttpPut]
    [ProducesResponseType(typeof(AiModelSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Put([FromBody] UpdateAiModelSettingsRequest request, CancellationToken cancellationToken)
    {
        var result = await _settings.SelectAsync(_currentUser.UserId, request.ModelType, cancellationToken);
        return result.Success ? Ok(result.Settings) : BadRequest(new { message = result.Error });
    }
}
