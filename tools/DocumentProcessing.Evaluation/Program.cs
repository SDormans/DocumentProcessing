// Runs the production prompt, AI provider and validation rules over the synthetic corpus and
// writes predictions in the format tools/evaluation/evaluate.mjs scores.
//
//   dotnet run --project tools/DocumentProcessing.Evaluation -- --output predictions.jsonl [--dataset invoices.jsonl] [--limit 10]
//
// The provider comes from the usual configuration, e.g. Ai__Provider=OpenAI, Ai__ApiKey, Ai__ModelName.
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddInvoiceExtraction(builder.Configuration);
using var host = builder.Build();

var root = FindRepositoryRoot();
var dataset = builder.Configuration["dataset"] ?? Path.Combine(root, "evaluation-data", "invoices.jsonl");
var output = builder.Configuration["output"] ?? Path.Combine(root, "evaluation-data", "generated", "predictions.jsonl");
var limit = int.TryParse(builder.Configuration["limit"], out var parsedLimit) ? parsedLimit : int.MaxValue;
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);

var lines = (await File.ReadAllLinesAsync(dataset)).Where(l => !string.IsNullOrWhiteSpace(l)).Take(limit).ToList();
await using var writer = new StreamWriter(output);
var failures = 0;
foreach (var line in lines)
{
    var invoice = JsonNode.Parse(line)!;
    var id = invoice["id"]!.GetValue<string>();
    using var scope = host.Services.CreateScope();
    var ai = scope.ServiceProvider.GetRequiredService<IAiProvider>();
    var validation = scope.ServiceProvider.GetRequiredService<ValidationService>();

    var stopwatch = Stopwatch.StartNew();
    InvoiceExtractionResult result;
    try
    {
        result = await ai.ExtractAsync(InvoiceExtractor.BuildPrompt(invoice["documentText"]!.GetValue<string>()));
    }
    catch (Exception ex)
    {
        result = InvoiceExtractionResult.FailureResult(ex.Message);
    }
    var inferenceMs = stopwatch.Elapsed.TotalMilliseconds;
    await validation.ValidateAsync(result);
    var valid = result.Success && !validation.HasErrorsOrCritical();
    if (!result.Success)
        failures++;

    var prediction = new JsonObject
    {
        ["id"] = id,
        ["success"] = result.Success,
        ["valid"] = valid,
        ["requiresHumanReview"] = !valid || result.Warnings.Count > 0,
        ["fields"] = new JsonObject
        {
            ["invoiceNumber"] = result.InvoiceNumber,
            ["supplierName"] = result.SupplierName,
            ["invoiceDate"] = result.InvoiceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["dueDate"] = result.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["currency"] = result.Currency,
            ["subtotal"] = result.Subtotal,
            ["taxAmount"] = result.TaxAmount,
            ["totalAmount"] = result.TotalAmount,
            ["lineItems"] = null
        },
        // The extraction schema returns line items as text, not structured rows, so they aren't scored.
        ["unscoredFields"] = new JsonArray("lineItems"),
        ["durationMs"] = stopwatch.Elapsed.TotalMilliseconds,
        ["inferenceDurationMs"] = inferenceMs
    };
    if (result.EstimatedCost is { } cost)
        prediction["aiCostUsd"] = cost;
    await writer.WriteLineAsync(prediction.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    Console.WriteLine($"{id}: success={result.Success} valid={valid} {inferenceMs:0} ms");
}

Console.WriteLine($"Wrote {lines.Count} predictions to {output} ({failures} failed extractions).");
return 0;

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory != null && !File.Exists(Path.Combine(directory.FullName, "DocumentProcessing.slnx")))
        directory = directory.Parent;
    return directory?.FullName ?? Directory.GetCurrentDirectory();
}
