using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DocumentProcessing.Application.Services.Invoice;
using Microsoft.Extensions.Logging;

namespace DocumentProcessing.Infrastructure.Ai;

/// <summary>
/// <see cref="IAiProvider"/> for the Anthropic Messages API. The schema is enforced by forcing a single tool call whose
/// <c>input_schema</c> is <see cref="InvoiceExtractionSchema"/>; the tool input is then parsed like any other response.
/// </summary>
public class AnthropicProvider : IAiProvider
{
    public const string DefaultEndpoint = "https://api.anthropic.com/v1/";
    public const string ApiVersion = "2023-06-01";
    private const string ToolName = "record_invoice";

    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly ILogger<AnthropicProvider> _logger;

    public AnthropicProvider(HttpClient httpClient, AiOptions options, ILogger<AnthropicProvider> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<InvoiceExtractionResult> ExtractAsync(string prompt, CancellationToken cancellationToken = default)
    {
        using var activity = AiTelemetry.ActivitySource.StartActivity("ai.extract", ActivityKind.Client);
        activity?.SetTag("gen_ai.system", "anthropic");
        activity?.SetTag("gen_ai.request.model", _options.ModelName);
        var stopwatch = Stopwatch.StartNew();
        var outcome = "error";
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("messages", BuildRequest(prompt), cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                outcome = "rejected";
                _logger.LogError("AI provider rejected the request: {StatusCode}.", (int)response.StatusCode);
                return InvoiceExtractionResult.FailureResult("AI provider rejected the request (HTTP 400).", body);
            }
            response.EnsureSuccessStatusCode();

            var message = JsonNode.Parse(body);
            var cost = RecordUsage(message, activity);
            if (message?["stop_reason"]?.GetValue<string>() == "max_tokens")
            {
                outcome = "truncated";
                return WithCost(InvoiceExtractionResult.FailureResult("AI response was truncated (max output tokens reached).", body), cost);
            }

            var toolInput = (message?["content"] as JsonArray)?
                .FirstOrDefault(c => c?["type"]?.GetValue<string>() == "tool_use")?["input"];
            if (toolInput is null)
            {
                outcome = "empty";
                return WithCost(InvoiceExtractionResult.FailureResult("AI response did not contain the structured invoice data.", body), cost);
            }

            var result = InvoiceExtractionSchema.Parse(toolInput.ToJsonString());
            result.EstimatedCost = cost;
            outcome = result.Success ? "success" : "schema_violation";
            return result;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            outcome = "invalid_response";
            _logger.LogError(ex, "AI provider returned an unparseable response.");
            return InvoiceExtractionResult.FailureResult("AI provider returned an unparseable response.");
        }
        finally
        {
            var tags = new TagList { { "model", _options.ModelName }, { "outcome", outcome } };
            AiTelemetry.Requests.Add(1, tags);
            AiTelemetry.Duration.Record(stopwatch.Elapsed.TotalSeconds, tags);
            activity?.SetTag("outcome", outcome);
        }
    }

    private object BuildRequest(string prompt) => new
    {
        model = _options.ModelName,
        max_tokens = _options.MaxOutputTokens,
        temperature = 0,
        system = "You are an invoice data extraction engine. Record the invoice by calling the tool exactly once.",
        messages = new object[] { new { role = "user", content = prompt } },
        tools = new object[]
        {
            new { name = ToolName, description = "Records the extracted invoice fields.", input_schema = InvoiceExtractionSchema.CreateSchema() }
        },
        tool_choice = new { type = "tool", name = ToolName }
    };

    private static InvoiceExtractionResult WithCost(InvoiceExtractionResult result, decimal? cost)
    {
        result.EstimatedCost = cost;
        return result;
    }

    private decimal? RecordUsage(JsonNode? message, Activity? activity)
    {
        var usage = message?["usage"];
        if (usage is null)
            return null;
        var input = usage["input_tokens"]?.GetValue<long>() ?? 0;
        var output = usage["output_tokens"]?.GetValue<long>() ?? 0;
        var cost = input / 1_000_000m * _options.InputCostPerMillionTokens + output / 1_000_000m * _options.OutputCostPerMillionTokens;
        var tags = new TagList { { "model", _options.ModelName } };
        AiTelemetry.InputTokens.Add(input, tags);
        AiTelemetry.OutputTokens.Add(output, tags);
        AiTelemetry.Cost.Add((double)cost, tags);
        activity?.SetTag("gen_ai.usage.input_tokens", input);
        activity?.SetTag("gen_ai.usage.output_tokens", output);
        return cost;
    }

    internal static void ConfigureClient(HttpClient client, AiOptions options)
    {
        client.BaseAddress = new Uri(AiProviderRegistry.WithTrailingSlash(options.Endpoint));
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.Add("anthropic-version", ApiVersion);
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            client.DefaultRequestHeaders.Add("x-api-key", options.ApiKey);
    }
}
