using DocumentProcessing.Infrastructure;
using DocumentProcessing.Infrastructure.Observability;
using DocumentProcessing.Infrastructure.Queue;
using DocumentProcessing.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.AddObservability("documentprocessing-worker");

if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("DefaultConnection")))
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

// The API owns migrations; the worker only reads and writes rows.
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddInvoiceExtraction(builder.Configuration);

builder.Services.Configure<QueueProcessingOptions>(builder.Configuration.GetSection(QueueProcessingOptions.SectionName));
builder.Services.AddScoped<QueueCommandProcessor>();
builder.Services.AddHostedService<DocumentProcessingWorker>();

builder.Build().Run();
