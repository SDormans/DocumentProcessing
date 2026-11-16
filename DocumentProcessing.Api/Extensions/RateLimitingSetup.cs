using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace DocumentProcessing.Api.Extensions;

public static class RateLimitingSetup
{
    public const string LoginPolicy = "login";
    public const string UploadPolicy = "upload";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new RateLimitingOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }
                return ValueTask.CompletedTask;
            };

            // Per client IP, so one client's failed logins cannot lock everybody else out.
            limiter.AddPolicy(LoginPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                $"ip:{ClientIp(context)}",
                _ => FixedWindow(options.Login)));

            // Per signed-in user (uploads require authentication); IP only as a fallback.
            limiter.AddPolicy(UploadPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value is { } userId ? $"user:{userId}" : $"ip:{ClientIp(context)}",
                _ => FixedWindow(options.Upload)));
        });
        return services;
    }

    private static string ClientIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static FixedWindowRateLimiterOptions FixedWindow(RateLimitingOptions.Window window) => new()
    {
        PermitLimit = window.PermitLimit,
        Window = window.Period,
        QueueLimit = 0,
        AutoReplenishment = true
    };
}

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public Window Login { get; set; } = new() { PermitLimit = 5, Period = TimeSpan.FromMinutes(1) };

    public Window Upload { get; set; } = new() { PermitLimit = 20, Period = TimeSpan.FromMinutes(1) };

    public class Window
    {
        public int PermitLimit { get; set; }
        public TimeSpan Period { get; set; }
    }
}
