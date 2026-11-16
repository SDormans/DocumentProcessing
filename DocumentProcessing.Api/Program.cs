using DocumentProcessing.Api.Extensions;
using DocumentProcessing.Api.Middleware;
using DocumentProcessing.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddPersistence(builder.Configuration)
    .AddUserStore()
    .AddDocumentProcessing(builder.Configuration)
    .AddUploadLimits(builder.Configuration)
    .AddApiRateLimiting()
    .AddCookieAuthentication(builder.Environment)
    .AddAuthorizationPolicies();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

var app = builder.Build();

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
