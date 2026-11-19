using DocumentProcessing.Application.Services;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace DocumentProcessing.Api.Extensions;

public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly DocumentAccessOptions _options;

    public HttpCurrentUser(IHttpContextAccessor httpContextAccessor, IOptions<DocumentAccessOptions> options)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
    }

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    public string? UserId => Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public bool HasUnrestrictedDocumentAccess =>
        Principal is { } user && _options.EffectiveUnrestrictedRoles.Any(user.IsInRole);

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;
}
