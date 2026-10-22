using DocumentProcessing.Application.DTOs;
using DocumentProcessing.Application.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DocumentProcessing.Api.Controllers;

/// <summary>
/// Authenticatie-eindpunten voor de review-interface.
/// In development worden gebruikers beheerd door DevelopmentUserService (in-memory, seeded).
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IUserService userService, ILogger<AuthController> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    /// <summary>
    /// Logt een gebruiker in en stelt een authenticatiecookie in.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
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
            _logger.LogWarning("Mislukte loginpoging voor gebruiker {Username}", request.Username);
            return Unauthorized(new { message = "Ongeldige gebruikersnaam of wachtwoord." });
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role)
        };

        foreach (var role in user.Roles)
        {
            if (!claims.Any(c => c.Type == ClaimTypes.Role && c.Value == role))
                claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        _logger.LogInformation("Gebruiker {UserName} (ID: {UserId}) ingelogd", user.UserName, user.UserId);

        return Ok(user);
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
            return BadRequest(new { message = "Gebruikersnaam, wachtwoord en e-mail zijn verplicht." });
        }

        if (await _userService.UserExistsAsync(request.Username))
        {
            return BadRequest(new { message = "Gebruiker bestaat al." });
        }

        try
        {
            var user = await _userService.CreateUserAsync(
                request.Username, request.Password, request.Email, request.Role ?? "Reviewer");

            _logger.LogInformation("Gebruiker {Username} aangemaakt door {CreatedBy}", request.Username,
                User.FindFirst(ClaimTypes.Name)?.Value ?? "onbekend");

            return CreatedAtAction(nameof(Me), user);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Geeft een overzicht van de beschikbare rollen en development-gebruikers.
    /// </summary>
    [HttpGet("development-info")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult DevelopmentInfo()
    {
        return Ok(new
        {
            message = "Development authenticatie-informatie",
            users = new[]
            {
                new { username = "admin", password = "Admin123!", role = "Admin", email = "admin@documentprocessing.local" },
                new { username = "reviewer", password = "Reviewer123!", role = "Reviewer", email = "reviewer@documentprocessing.local" }
            },
            policies = new[] { "CanReviewDocuments", "CanApproveReject", "IsAdmin" },
            loginEndpoint = "/api/auth/login",
            logoutEndpoint = "/api/auth/logout",
            meEndpoint = "/api/auth/me",
            registerEndpoint = "/api/auth/register (alleen Admin)"
        });
    }
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Role { get; set; }
}