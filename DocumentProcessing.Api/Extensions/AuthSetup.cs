using DocumentProcessing.Application.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DocumentProcessing.Api.Extensions;

public static class AuthSetup
{
    public const string CookieName = "DocumentProcessing.Auth";

    /// <summary>
    /// Secure policy for the auth and antiforgery cookies: <c>Authentication:CookieSecurePolicy</c>
    /// (<c>Always</c>, <c>SameAsRequest</c> or <c>None</c>); defaults to <c>None</c> in Development, else <c>Always</c>.
    /// </summary>
    public static CookieSecurePolicy GetCookieSecurePolicy(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["Authentication:CookieSecurePolicy"];
        if (string.IsNullOrWhiteSpace(configured))
            return environment.IsDevelopment() ? CookieSecurePolicy.None : CookieSecurePolicy.Always;
        if (Enum.TryParse<CookieSecurePolicy>(configured, ignoreCase: true, out var policy) && Enum.IsDefined(policy))
            return policy;
        throw new InvalidOperationException(
            $"Authentication:CookieSecurePolicy '{configured}' is invalid. Use Always, SameAsRequest or None.");
    }

    public static IServiceCollection AddCookieAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var securePolicy = GetCookieSecurePolicy(configuration, environment);
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = securePolicy;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.LoginPath = "/api/auth/login";
                options.LogoutPath = "/api/auth/logout";
                options.AccessDeniedPath = "/api/auth/forbidden";
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });
        return services;
    }

    public static IServiceCollection AddAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy("CanReviewDocuments", policy =>
                policy.RequireAuthenticatedUser());
            options.AddPolicy("CanApproveReject", policy =>
                policy.RequireAuthenticatedUser()
                    .RequireRole(ReviewRoles.CanApproveOrReject));
            options.AddPolicy("IsAdmin", policy =>
                policy.RequireAuthenticatedUser()
                    .RequireRole("Admin"));
        });
        return services;
    }
}
