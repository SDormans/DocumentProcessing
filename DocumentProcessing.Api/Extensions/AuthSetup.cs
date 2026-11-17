using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DocumentProcessing.Api.Extensions;

public static class AuthSetup
{
    public const string CookieName = "DocumentProcessing.Auth";

    public static IServiceCollection AddCookieAuthentication(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.None
                    : CookieSecurePolicy.Always;
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

    /// <summary>Claim added at sign-in when an authenticator code was checked.</summary>
    public const string MfaClaimType = "amr";
    public const string MfaClaimValue = "mfa";

    /// <param name="requireAdminMfa">When true, admin endpoints require a session signed in with an authenticator code.</param>
    public static IServiceCollection AddAuthorizationPolicies(this IServiceCollection services, bool requireAdminMfa = false)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy("CanReviewDocuments", policy =>
                policy.RequireAuthenticatedUser());
            options.AddPolicy("CanApproveReject", policy =>
                policy.RequireAuthenticatedUser()
                    .RequireRole("Admin", "Reviewer"));
            options.AddPolicy("IsAdmin", policy =>
            {
                policy.RequireAuthenticatedUser().RequireRole("Admin");
                if (requireAdminMfa)
                    policy.RequireClaim(MfaClaimType, MfaClaimValue);
            });
        });
        return services;
    }
}
