using DocumentProcessing.Infrastructure;
using DocumentProcessing.Infrastructure.Observability;
using DocumentProcessing.Infrastructure.Queue;
using DocumentProcessing.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.AddObservability("documentprocessing-worker");

if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("DefaultConnection")))
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is required. Set the ConnectionStrings__DefaultConnection environment variable, " +
        "run 'dotnet user-secrets set ConnectionStrings:DefaultConnection \"<connection string>\" --project DocumentProcessing.Worker', " +
        "or copy DocumentProcessing.Worker/appsettings.Development.example.json to appsettings.Development.json (Development only).");

// The API owns migrations; the worker only reads and writes rows.
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddInvoiceExtraction(builder.Configuration);

builder.Services.Configure<QueueProcessingOptions>(builder.Configuration.GetSection(QueueProcessingOptions.SectionName));
builder.Services.AddScoped<QueueCommandProcessor>();
builder.Services.AddHostedService<DocumentProcessingWorker>();

builder.Build().Run();
