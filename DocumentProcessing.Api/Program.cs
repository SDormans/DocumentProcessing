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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

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
builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped<IDocumentService, DocumentService>();

// Text extraction services
builder.Services.AddScoped<IDocumentTextExtractor, PdfPigTextExtractor>();
builder.Services.AddScoped<ITextExtractionDetector, TextExtractionDetector>();
builder.Services.AddScoped<IOcrProvider, TesseractOcrProvider>();
builder.Services.AddScoped<IDocumentTextExtractionService, DocumentTextExtractionService>();

// AI providers
builder.Services.AddScoped<IAiProvider, FakeAiProvider>(); // Use RealAiProvider when configured
builder.Services.AddScoped<IInvoiceExtractor, InvoiceExtractor>();

// DocumentService options from configuration
builder.Services.Configure<DocumentServiceOptions>(builder.Configuration.GetSection("DocumentService"));
builder.Services.Configure<TextExtractionOptions>(builder.Configuration.GetSection("TextExtraction"));

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
app.UseAuthorization();
app.MapControllers();

// Ensure database is created and migrated
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.Run();

public partial class Program { }