using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace DocumentProcessing.Api.Extensions;

public static class RateLimitingSetup
{
    public const string LoginPolicy = "login";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter(LoginPolicy, limiter =>
            {
                limiter.PermitLimit = 5;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
                limiter.AutoReplenishment = true;
            });
        });
        return services;
    }
}
