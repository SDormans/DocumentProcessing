using DocumentProcessing.Application.Services;
using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Infrastructure.Ai;
using DocumentProcessing.Application.Services.TextExtraction;
using DocumentProcessing.Domain.Interfaces;
using DocumentProcessing.Infrastructure.Data;
using DocumentProcessing.Infrastructure.Ocr;
using DocumentProcessing.Infrastructure.Storage;
using DocumentProcessing.Infrastructure.TextExtraction;
using DocumentProcessing.Api.Middleware;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.RateLimiting;
using DocumentProcessing.Infrastructure.Queue;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

if (!builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "This sample application uses an in-memory demonstration user store and cannot run outside Development.");
}

// Services
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
    });
});

builder.Services.AddScoped<IDocumentRepository>(
    sp => sp.GetRequiredService<AppDbContext>());
builder.Services.AddScoped<IQueueCommandRepository, QueueCommandRepository>();
builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<ValidationService>();
builder.Services.AddScoped<IDocumentReviewRepository>(
    sp => sp.GetRequiredService<AppDbContext>());
builder.Services.AddScoped<IInvoiceResultRepository>(
    sp => sp.GetRequiredService<AppDbContext>());
builder.Services.AddScoped<IDocumentReviewService, DocumentReviewService>();
builder.Services.AddSingleton<IUserService>(_ => new DevelopmentUserService(
    builder.Configuration["Authentication:Bootstrap:Username"],
    builder.Configuration["Authentication:Bootstrap:Password"],
    builder.Configuration["Authentication:Bootstrap:Email"],
    builder.Configuration["Authentication:Bootstrap:Role"]));

// Text extraction services
builder.Services.AddScoped<IDocumentTextExtractor, PdfPigTextExtractor>();
builder.Services.AddScoped<ITextExtractionDetector, TextExtractionDetector>();
builder.Services.AddScoped<IOcrProvider, TesseractOcrProvider>();
builder.Services.AddScoped<IDocumentTextExtractionService, DocumentTextExtractionService>();

// AI providers
builder.Services.AddScoped<IAiProvider, FakeAiProvider>();
builder.Services.AddScoped<IInvoiceExtractor, InvoiceExtractor>();

// DocumentService options from configuration
builder.Services.Configure<DocumentServiceOptions>(builder.Configuration.GetSection("DocumentService"));
builder.Services.Configure<TextExtractionOptions>(builder.Configuration.GetSection("TextExtraction"));

var maxFileSizeBytes = builder.Configuration.GetValue<long>(
    "DocumentService:MaxFileSizeBytes",
    10 * 1024 * 1024);
if (maxFileSizeBytes <= 0)
{
    throw new InvalidOperationException("DocumentService:MaxFileSizeBytes must be greater than zero.");
}

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = checked(maxFileSizeBytes + 128 * 1024);
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("login", limiterOptions =>
    {
        limiterOptions.PermitLimit = 5;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
        limiterOptions.AutoReplenishment = true;
    });
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "DocumentProcessing.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
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

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanReviewDocuments", policy =>
        policy.RequireAuthenticatedUser());
    options.AddPolicy("CanApproveReject", policy =>
        policy.RequireAuthenticatedUser()
            .RequireAssertion(context =>
            {
                var role = context.User.FindFirst(ClaimTypes.Role)?.Value;
                return role == "Admin" || role == "Reviewer";
            }));
    options.AddPolicy("IsAdmin", policy =>
        policy.RequireAuthenticatedUser()
            .RequireRole("Admin"));
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Middleware pipeline
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
app.MapGet("/health/ready", async (AppDbContext dbContext, CancellationToken cancellationToken) =>
    await dbContext.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ready" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous();
app.MapControllers();

// Ensure database is created and migrated
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.Run();

public partial class Program { }
