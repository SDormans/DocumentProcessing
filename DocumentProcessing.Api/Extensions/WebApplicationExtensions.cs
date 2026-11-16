using DocumentProcessing.Infrastructure.Data;
using DocumentProcessing.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace DocumentProcessing.Api.Extensions;

public static class WebApplicationExtensions
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/ready", async (AppDbContext dbContext, CancellationToken cancellationToken) =>
                await dbContext.Database.CanConnectAsync(cancellationToken)
                    ? Results.Ok(new { status = "ready" })
                    : Results.StatusCode(StatusCodes.Status503ServiceUnavailable))
            .AllowAnonymous();
        return endpoints;
    }

    /// <summary>Applies pending EF Core migrations and seeds Identity roles and the bootstrap account.</summary>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await MigrationBaseline.AdoptLegacyHistoryAsync(
            dbContext, scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(MigrationBaseline)));
        await dbContext.Database.MigrateAsync();
        await IdentitySeeder.SeedAsync(scope.ServiceProvider, app.Configuration);
    }
}
