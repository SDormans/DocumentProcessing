using DocumentProcessing.Domain.Interfaces;
using DocumentProcessing.Infrastructure;
using DocumentProcessing.Infrastructure.Data;
using DocumentProcessing.Infrastructure.Queue;
using DocumentProcessing.Infrastructure.Storage;
using DocumentProcessing.Worker;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

// The API owns migrations; the worker only reads and writes rows.
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<IDocumentRepository>(sp => sp.GetRequiredService<AppDbContext>());
builder.Services.AddScoped<IQueueCommandRepository, QueueCommandRepository>();
builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
builder.Services.AddInvoiceExtraction(builder.Configuration);

builder.Services.Configure<QueueProcessingOptions>(builder.Configuration.GetSection(QueueProcessingOptions.SectionName));
builder.Services.AddScoped<QueueCommandProcessor>();
builder.Services.AddHostedService<DocumentProcessingWorker>();

builder.Build().Run();
