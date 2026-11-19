using DocumentProcessing.Api.Extensions;
using DocumentProcessing.Api.Middleware;
using DocumentProcessing.Infrastructure;
using DocumentProcessing.Infrastructure.Observability;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservability(
    "documentprocessing-api",
    tracing => tracing.AddAspNetCoreInstrumentation(options =>
        options.Filter = context => !context.Request.Path.StartsWithSegments("/health")),
    metrics => metrics.AddAspNetCoreInstrumentation());

builder.Services
    .AddPersistence(builder.Configuration)
    .AddUserStore()
    .AddDocumentProcessing(builder.Configuration)
    .AddUploadLimits(builder.Configuration)
    .AddApiRateLimiting(builder.Configuration)
    .AddApiAntiforgery(builder.Configuration, builder.Environment)
    .AddCookieAuthentication(builder.Configuration, builder.Environment)
    .AddAuthorizationPolicies()
    .AddTrustedForwardedHeaders(builder.Configuration);

builder.Services.AddControllers(options => options.Filters.Add<ValidateAntiforgeryOnUnsafeMethodsFilter>());
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapOpenApi();
}

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthEndpoints();
app.MapControllers();

await app.MigrateDatabaseAsync();

app.Run();

public partial class Program { }
