namespace DocumentProcessing.Infrastructure.Ai;

public class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary><c>Fake</c> (default, deterministic sample data) or <c>OpenAI</c> (any OpenAI-compatible Chat Completions API).</summary>
    public string Provider { get; set; } = "Fake";

    /// <summary>Base URL of the API, e.g. <c>https://api.openai.com/v1/</c> or <c>https://{resource}.openai.azure.com/openai/v1/</c>.</summary>
    public string Endpoint { get; set; } = "https://api.openai.com/v1/";

    public string? ApiKey { get; set; }

    /// <summary>Header used to send <see cref="ApiKey"/>: <c>Authorization</c> (Bearer) or e.g. <c>api-key</c> for Azure OpenAI.</summary>
    public string ApiKeyHeader { get; set; } = "Authorization";

    public string ModelName { get; set; } = "gpt-4o-mini";

    public int MaxOutputTokens { get; set; } = 4096;

    /// <summary>Timeout for a single HTTP attempt.</summary>
    public int AttemptTimeoutSeconds { get; set; } = 60;

    /// <summary>Overall timeout including retries.</summary>
    public int TotalTimeoutSeconds { get; set; } = 180;

    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Price per one million input tokens, used for cost telemetry.</summary>
    public decimal InputCostPerMillionTokens { get; set; }

    /// <summary>Price per one million output tokens, used for cost telemetry.</summary>
    public decimal OutputCostPerMillionTokens { get; set; }
}
