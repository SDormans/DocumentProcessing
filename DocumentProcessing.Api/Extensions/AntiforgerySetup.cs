using Microsoft.AspNetCore.Antiforgery;

namespace DocumentProcessing.Api.Extensions;

/// <summary>
/// Double-submit anti-forgery for the cookie-authenticated API: the server issues a request token in the readable
/// <see cref="RequestTokenCookieName"/> cookie, and the SPA echoes it in the <see cref="HeaderName"/> header on every
/// state-changing request. <see cref="ValidateAntiforgeryOnUnsafeMethodsFilter"/> validates it globally.
/// </summary>
public static class AntiforgerySetup
{
    public const string HeaderName = "X-XSRF-TOKEN";
    public const string RequestTokenCookieName = "XSRF-TOKEN";
    public const string CookieName = "DocumentProcessing.Antiforgery";

    public static IServiceCollection AddApiAntiforgery(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddAntiforgery(options =>
        {
            options.HeaderName = HeaderName;
            options.Cookie.Name = CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = AuthSetup.GetCookieSecurePolicy(configuration, environment);
        });
        return services;
    }

    /// <summary>
    /// Issues a request token bound to <see cref="HttpContext.User"/>; call again whenever the signed-in user changes.
    /// </summary>
    public static void IssueRequestToken(this IAntiforgery antiforgery, HttpContext context)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Cookies.Append(RequestTokenCookieName, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            Path = "/"
        });
    }
}

/// <summary>
/// Validates the anti-forgery token on every state-changing MVC request unless the action opts out with
/// <see cref="Microsoft.AspNetCore.Mvc.IgnoreAntiforgeryTokenAttribute"/>. Equivalent to MVC's
/// <c>AutoValidateAntiforgeryTokenAttribute</c>, which needs the view services this API does not register.
/// </summary>
public sealed class ValidateAntiforgeryOnUnsafeMethodsFilter : Microsoft.AspNetCore.Mvc.Filters.IAsyncAuthorizationFilter
{
    private readonly IAntiforgery _antiforgery;
    private readonly ILogger<ValidateAntiforgeryOnUnsafeMethodsFilter> _logger;

    public ValidateAntiforgeryOnUnsafeMethodsFilter(IAntiforgery antiforgery, ILogger<ValidateAntiforgeryOnUnsafeMethodsFilter> logger)
    {
        _antiforgery = antiforgery;
        _logger = logger;
    }

    public async Task OnAuthorizationAsync(Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext context)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
            return;

        if (context.ActionDescriptor.EndpointMetadata.OfType<Microsoft.AspNetCore.Mvc.IgnoreAntiforgeryTokenAttribute>().Any())
            return;

        try
        {
            await _antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException ex)
        {
            _logger.LogWarning("Anti-forgery validation failed for {Method} {Path}: {Reason}", method, context.HttpContext.Request.Path, ex.Message);
            context.Result = new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new { message = "Missing or invalid anti-forgery token." });
        }
    }
}
