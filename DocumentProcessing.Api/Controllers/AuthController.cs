using DocumentProcessing.Api.Extensions;
using DocumentProcessing.Application.DTOs;
using DocumentProcessing.Application.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace DocumentProcessing.Api.Controllers;

/// <summary>
/// Authenticatie-eindpunten voor de review-interface.
/// Users are stored with ASP.NET Core Identity in PostgreSQL; an initial administrator can be bootstrapped through configuration.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<AuthController> _logger;
    private readonly IAntiforgery _antiforgery;

    public AuthController(IUserService userService, ILogger<AuthController> logger, IAntiforgery antiforgery)
    {
        _userService = userService;
        _logger = logger;
        _antiforgery = antiforgery;
    }

    /// <summary>
    /// Logt een gebruiker in en stelt een authenticatiecookie in.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    // The SPA has no token before signing in; JSON-only bodies and no CORS keep cross-site posts from reaching this.
    [IgnoreAntiforgeryToken]
    [EnableRateLimiting(RateLimitingSetup.LoginPolicy)]
    [ProducesResponseType(typeof(UserInfoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Gebruikersnaam en wachtwoord zijn verplicht." });
        }

        var user = await _userService.ValidateUserAsync(request.Username, request.Password, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("Login attempt failed.");
            return Unauthorized(new { message = "Ongeldige gebruikersnaam of wachtwoord." });
        }

        var usedMfa = false;
        if (await _userService.IsTwoFactorEnabledAsync(user.UserId, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(request.TwoFactorCode))
                return Unauthorized(new { message = "Two-factor code required.", requiresTwoFactor = true });
            if (!await _userService.VerifyTwoFactorCodeAsync(user.UserId, request.TwoFactorCode, cancellationToken))
            {
                _logger.LogWarning("Two-factor code rejected for user {UserId}.", user.UserId);
                return Unauthorized(new { message = "Invalid two-factor code.", requiresTwoFactor = true });
            }
            usedMfa = true;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role)
        };

        if (usedMfa)
            claims.Add(new Claim(AuthSetup.MfaClaimType, AuthSetup.MfaClaimValue));

        foreach (var role in user.Roles)
        {
            if (!claims.Any(c => c.Type == ClaimTypes.Role && c.Value == role))
                claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        // The token is bound to the user, so issue it for the identity that was just signed in.
        HttpContext.User = principal;
        _antiforgery.IssueRequestToken(HttpContext);

        _logger.LogInformation("User {UserId} signed in.", user.UserId);

        return Ok(user);
    }

    /// <summary>Starts authenticator-app enrolment: returns a new key. Two-factor sign-in is off until enabled.</summary>
    [HttpPost("mfa/setup")]
    [Authorize]
    [ProducesResponseType(typeof(AuthenticatorSetup), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetupTwoFactor(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        return Ok(await _userService.ResetAuthenticatorAsync(userId, "DocumentProcessing", cancellationToken));
    }

    /// <summary>Confirms enrolment with a code from the app; later sign-ins then need a code.</summary>
    [HttpPost("mfa/enable")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EnableTwoFactor([FromBody] EnableTwoFactorRequest request, CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        if (!await _userService.EnableTwoFactorAsync(userId, request.Code, cancellationToken))
            return BadRequest(new { message = "Invalid two-factor code." });
        _logger.LogInformation("User {UserId} enabled two-factor sign-in.", userId);
        return NoContent();
    }

    /// <summary>
    /// Logt de huidige gebruiker uit.
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { message = "Uitgelogd." });
    }

    /// <summary>
    /// Geeft informatie over de momenteel ingelogde gebruiker.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserInfoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Me()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        var userName = User.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;
        var email = User.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        // The SPA calls this on load, which refreshes the anti-forgery token for an existing session.
        _antiforgery.IssueRequestToken(HttpContext);

        return Ok(new UserInfoDto
        {
            UserId = userId,
            UserName = userName,
            Email = email,
            Role = roles.FirstOrDefault() ?? string.Empty,
            Roles = roles
        });
    }

    /// <summary>
    /// Maakt een nieuwe gebruiker aan (alleen voor development/beheer).
    /// </summary>
    [HttpPost("register")]
    [Authorize(Policy = "IsAdmin")]
    [ProducesResponseType(typeof(UserInfoDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { message = "Username, password, and email are required." });
        }

        if (await _userService.UserExistsAsync(request.Username, cancellationToken))
        {
            return BadRequest(new { message = "The user already exists." });
        }

        var role = request.Role ?? "Reviewer";
        if (role is not ("Admin" or "Reviewer"))
        {
            return BadRequest(new { message = "The requested role is not supported." });
        }

        try
        {
            var user = await _userService.CreateUserAsync(
                request.Username, request.Password, request.Email, role, cancellationToken);

            _logger.LogInformation("User {UserId} created by {CreatedBy}.", user.UserId,
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown");

            return CreatedAtAction(nameof(Me), user);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException)
        {
            return Conflict(new { message = "The user could not be created." });
        }
    }
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Authenticator-app code; required once two-factor sign-in is enabled.</summary>
    public string? TwoFactorCode { get; set; }
}

public class EnableTwoFactorRequest
{
    public string Code { get; set; } = string.Empty;
}

public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Role { get; set; }
}