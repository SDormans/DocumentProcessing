namespace DocumentProcessing.Infrastructure.Ai;

public class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>
    /// <c>Fake</c> (deterministic sample data), <c>OpenAI</c> (any OpenAI-compatible Chat Completions API, including
    /// Azure OpenAI, Ollama and llama.cpp), <c>Anthropic</c> (Messages API) or <c>None</c> (this model type is disabled).
    /// </summary>
    public string Provider { get; set; } = "Fake";

    /// <summary>
    /// Base URL of the API, e.g. <c>https://api.openai.com/v1/</c>, <c>https://{resource}.openai.azure.com/openai/v1/</c>,
    /// <c>http://localhost:11434/v1/</c> (Ollama) or <c>http://localhost:8080/v1/</c> (llama.cpp). Empty uses the provider default.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

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

/// <summary>
/// <c>Ai:*</c>: the cloud model (<c>Ai:Cloud</c>), the local model (<c>Ai:Local</c>), which one new uploads use by
/// default and what happens when the chosen one fails. A flat <c>Ai:Provider</c>/<c>Ai:Endpoint</c>/... configuration
/// without an <c>Ai:Cloud</c> section is read as the cloud model, so older configuration keeps working.
/// </summary>
public class AiSettings
{
    public const string SectionName = "Ai";

    public string DefaultModelType { get; set; } = "Cloud";

    public AiFallbackOptions Fallback { get; set; } = new();

    public AiOptions Cloud { get; set; } = new();

    public AiOptions Local { get; set; } = new() { Provider = "None" };

    public static AiSettings Bind(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var settings = new AiSettings();
        Microsoft.Extensions.Configuration.ConfigurationBinder.Bind(section, settings);
        var cloud = section.GetSection("Cloud");
        if (!Microsoft.Extensions.Configuration.ConfigurationExtensions.Exists(cloud))
        {
            settings.Cloud = new AiOptions();
            Microsoft.Extensions.Configuration.ConfigurationBinder.Bind(section, settings.Cloud);
        }
        return settings;
    }
}

public class AiFallbackOptions
{
    /// <summary><c>None</c> (default) or <c>Automatic</c>. Automatic can send a document chosen for the local model to the cloud.</summary>
    public string Mode { get; set; } = "None";
}
