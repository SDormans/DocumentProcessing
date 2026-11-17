using DocumentProcessing.Infrastructure.Data;
using DocumentProcessing.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace DocumentProcessing.Api.Extensions;

public static class WebApplicationExtensions
{
    /// <summary>
    /// <c>/health/live</c>: the process is up and serving requests (no dependencies checked; use for liveness probes).
    /// <c>/health/ready</c>: the database is reachable (use for readiness probes and startup ordering).
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
            .AllowAnonymous();
        endpoints.MapGet("/health/ready", async (AppDbContext dbContext, CancellationToken cancellationToken) =>
                await dbContext.Database.CanConnectAsync(cancellationToken)
                    ? Results.Ok(new { status = "ready" })
                    : Results.StatusCode(StatusCodes.Status503ServiceUnavailable))
            .AllowAnonymous();
        return endpoints;
    }

    /// <summary>
    /// Waits for PostgreSQL, applies pending EF Core migrations and seeds Identity roles and the bootstrap account.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var startupOptions = app.Configuration.GetSection(DatabaseStartupOptions.SectionName).Get<DatabaseStartupOptions>()
            ?? new DatabaseStartupOptions();
        await DatabaseStartup.WaitForServerAsync(
            dbContext, startupOptions, loggerFactory.CreateLogger(nameof(DatabaseStartup)), app.Lifetime.ApplicationStopping);
        await MigrationBaseline.AdoptLegacyHistoryAsync(dbContext, loggerFactory.CreateLogger(nameof(MigrationBaseline)));
        await dbContext.Database.MigrateAsync();
        await IdentitySeeder.SeedAsync(scope.ServiceProvider, app.Configuration);
    }
}
