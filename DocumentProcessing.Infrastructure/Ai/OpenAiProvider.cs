using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DocumentProcessing.Application.Services.Invoice;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Infrastructure.Ai;

/// <summary>
/// <see cref="IAiProvider"/> for OpenAI-compatible Chat Completions APIs using strict JSON-schema
/// structured outputs (<see cref="InvoiceExtractionSchema"/>). Retries and timeouts are applied by
/// the HTTP resilience pipeline configured in <see cref="AiServiceCollectionExtensions"/>; transient
/// failures that survive the retries surface as exceptions so the caller can reschedule the work.
/// </summary>
public class OpenAiProvider : IAiProvider
{
    private const string SystemPrompt =
        "You are an invoice data extraction engine. Respond only with JSON that matches the provided schema.";

    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly ILogger<OpenAiProvider> _logger;

    public OpenAiProvider(HttpClient httpClient, IOptions<AiOptions> options, ILogger<OpenAiProvider> logger)
        : this(httpClient, options.Value, logger)
    {
    }

    public OpenAiProvider(HttpClient httpClient, AiOptions options, ILogger<OpenAiProvider> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<InvoiceExtractionResult> ExtractAsync(string prompt, CancellationToken cancellationToken = default)
    {
        using var activity = AiTelemetry.ActivitySource.StartActivity("ai.extract", ActivityKind.Client);
        activity?.SetTag("gen_ai.system", "openai");
        activity?.SetTag("gen_ai.request.model", _options.ModelName);

        var stopwatch = Stopwatch.StartNew();
        var outcome = "error";
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("chat/completions", BuildRequest(prompt), cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                outcome = "rejected";
                _logger.LogError("AI provider rejected the request: {StatusCode}.", (int)response.StatusCode);
                return InvoiceExtractionResult.FailureResult("AI provider rejected the request (HTTP 400).", body);
            }

            response.EnsureSuccessStatusCode();

            var completion = JsonNode.Parse(body);
            var cost = RecordUsage(completion, activity);

            var choice = completion?["choices"] is JsonArray { Count: > 0 } choices ? choices[0] : null;
            var message = choice?["message"];
            var finishReason = choice?["finish_reason"]?.GetValue<string>();
            var refusal = message?["refusal"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(refusal))
            {
                outcome = "refused";
                return WithCost(InvoiceExtractionResult.FailureResult($"AI provider refused: {refusal}", body), cost);
            }

            if (finishReason == "length")
            {
                outcome = "truncated";
                return WithCost(InvoiceExtractionResult.FailureResult("AI response was truncated (max output tokens reached).", body), cost);
            }

            var content = message?["content"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(content))
            {
                outcome = "empty";
                return WithCost(InvoiceExtractionResult.FailureResult("AI response did not contain any content.", body), cost);
            }

            var result = InvoiceExtractionSchema.Parse(content);
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
            stopwatch.Stop();
            var tags = new TagList { { "model", _options.ModelName }, { "outcome", outcome } };
            AiTelemetry.Requests.Add(1, tags);
            AiTelemetry.Duration.Record(stopwatch.Elapsed.TotalSeconds, tags);
            activity?.SetTag("outcome", outcome);
        }
    }

    private object BuildRequest(string prompt) => new
    {
        model = _options.ModelName,
        temperature = 0,
        max_tokens = _options.MaxOutputTokens,
        messages = new object[]
        {
            new { role = "system", content = SystemPrompt },
            new { role = "user", content = prompt }
        },
        response_format = new
        {
            type = "json_schema",
            json_schema = new
            {
                name = InvoiceExtractionSchema.Name,
                strict = true,
                schema = InvoiceExtractionSchema.CreateSchema()
            }
        }
    };

    private static InvoiceExtractionResult WithCost(InvoiceExtractionResult result, decimal? cost)
    {
        result.EstimatedCost = cost;
        return result;
    }

    private decimal? RecordUsage(JsonNode? completion, Activity? activity)
    {
        var usage = completion?["usage"];
        if (usage is null)
            return null;

        var input = usage["prompt_tokens"]?.GetValue<long>() ?? 0;
        var output = usage["completion_tokens"]?.GetValue<long>() ?? 0;
        var cost = input / 1_000_000m * _options.InputCostPerMillionTokens
                 + output / 1_000_000m * _options.OutputCostPerMillionTokens;

        var tags = new TagList { { "model", _options.ModelName } };
        AiTelemetry.InputTokens.Add(input, tags);
        AiTelemetry.OutputTokens.Add(output, tags);
        AiTelemetry.Cost.Add((double)cost, tags);

        activity?.SetTag("gen_ai.usage.input_tokens", input);
        activity?.SetTag("gen_ai.usage.output_tokens", output);
        activity?.SetTag("ai.cost", (double)cost);

        _logger.LogInformation(
            "AI request used {InputTokens} input and {OutputTokens} output tokens on {Model} (estimated cost {Cost}).",
            input, output, _options.ModelName, cost);
        return cost;
    }

    internal static void ConfigureClient(HttpClient client, AiOptions options)
    {
        client.BaseAddress = new Uri(AiProviderRegistry.WithTrailingSlash(options.Endpoint));
        client.Timeout = Timeout.InfiniteTimeSpan;

        if (string.IsNullOrWhiteSpace(options.ApiKey))
            return;

        if (string.Equals(options.ApiKeyHeader, "Authorization", StringComparison.OrdinalIgnoreCase))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        else
            client.DefaultRequestHeaders.Add(options.ApiKeyHeader, options.ApiKey);
    }
}
