using System.Net;
using System.Text.Json.Nodes;
using DocumentProcessing.Application.Services.Ai;
using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentProcessing.Infrastructure.Ai;

/// <summary>
/// Resolves the provider for a model type from keyed <see cref="IAiProvider"/> registrations and probes availability.
/// </summary>
public class AiProviderRegistry : IAiProviderRegistry
{
    public const string ProviderNone = "None";
    public const string ProviderFake = "Fake";
    public const string ProviderOpenAi = "OpenAI";
    public const string ProviderAnthropic = "Anthropic";
    public const string DefaultLocalEndpoint = "http://localhost:11434/v1/";
    public const string DefaultOpenAiEndpoint = "https://api.openai.com/v1/";
    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly IServiceProvider _services;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ValidatedAiSettings _settings;

    public AiProviderRegistry(ValidatedAiSettings settings, IServiceProvider services, IHttpClientFactory? httpClientFactory = null)
    {
        _settings = settings;
        _services = services;
        _httpClientFactory = httpClientFactory;
    }

    public AiModelType DefaultModelType => _settings.DefaultModelType;
    public AiFallbackMode FallbackMode => _settings.FallbackMode;
    public IReadOnlyList<AiModelOption> Options => _settings.Options;

    public bool IsEnabled(AiModelType modelType) => Options.Any(o => o.ModelType == modelType && o.Enabled);

    public IAiProvider Get(AiModelType modelType)
    {
        if (!IsEnabled(modelType))
            throw new AiModelUnavailableException(modelType,
                $"The {modelType} model is not configured. {Options.FirstOrDefault(o => o.ModelType == modelType)?.DisabledReason}".Trim(),
                isConfigurationError: true);
        return _services.GetRequiredKeyedService<IAiProvider>(modelType);
    }

    public async Task<AiModelAvailability> CheckAvailabilityAsync(AiModelType modelType, CancellationToken cancellationToken = default)
    {
        var option = Options.First(o => o.ModelType == modelType);
        if (!option.Enabled)
            return new(modelType, false, option.DisabledReason ?? "Not configured.");
        if (option.Provider == ProviderFake || _httpClientFactory is null)
            return new(modelType, true, "Fake model: returns empty results without calling an AI service.");

        var options = _settings.For(modelType);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        try
        {
            using var response = await _httpClientFactory.CreateClient(ProbeClientName(modelType)).GetAsync("models", timeout.Token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new(modelType, false, $"The {modelType} model's API key was rejected by {options.Endpoint}.");
            if (!response.IsSuccessStatusCode)
                return new(modelType, false, $"{options.Endpoint} answered HTTP {(int)response.StatusCode}.");
            if (modelType == AiModelType.Local && option.Provider == ProviderOpenAi
                && !ListsModel(await response.Content.ReadAsStringAsync(timeout.Token), option.ModelName))
                return new(modelType, false, $"Model '{option.ModelName}' is not installed on the local runtime at {options.Endpoint}. For Ollama, run 'ollama pull {option.ModelName}'.");
            return new(modelType, true, $"{option.Provider} model '{option.ModelName}' is reachable.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new(modelType, false, $"The {modelType} model at {options.Endpoint} is not reachable: {(ex is TaskCanceledException ? "timed out" : ex.Message)}");
        }
    }

    internal static bool ListsModel(string body, string modelName)
    {
        try
        {
            var ids = (JsonNode.Parse(body)?["data"] as JsonArray)?
                .Select(m => m?["id"]?.GetValue<string>()).Where(id => id is not null).ToList();
            if (ids is null || ids.Count == 0)
                return true; // Runtime does not list models; let the first request decide.
            return ids.Any(id => string.Equals(id, modelName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, modelName + ":latest", StringComparison.OrdinalIgnoreCase)
                || string.Equals(id + ":latest", modelName, StringComparison.OrdinalIgnoreCase));
        }
        catch (System.Text.Json.JsonException)
        {
            return true;
        }
    }

    public static string ClientName(AiModelType modelType) => $"ai-{modelType.ToString().ToLowerInvariant()}";
    public static string ProbeClientName(AiModelType modelType) => ClientName(modelType) + "-probe";

    internal static string WithTrailingSlash(string endpoint) => endpoint.EndsWith('/') ? endpoint : endpoint + "/";
}

/// <summary><see cref="AiSettings"/> after defaults are applied and every value is checked.</summary>
public sealed class ValidatedAiSettings
{
    private ValidatedAiSettings(AiModelType defaultModelType, AiFallbackMode fallbackMode, AiOptions cloud, AiOptions local, IReadOnlyList<AiModelOption> options)
    {
        DefaultModelType = defaultModelType;
        FallbackMode = fallbackMode;
        Cloud = cloud;
        Local = local;
        Options = options;
    }

    public AiModelType DefaultModelType { get; }
    public AiFallbackMode FallbackMode { get; }
    public AiOptions Cloud { get; }
    public AiOptions Local { get; }
    public IReadOnlyList<AiModelOption> Options { get; }

    public AiOptions For(AiModelType modelType) => modelType == AiModelType.Local ? Local : Cloud;

    /// <exception cref="InvalidOperationException">The configuration is invalid; the message lists every problem.</exception>
    public static ValidatedAiSettings Create(AiSettings settings)
    {
        var errors = new List<string>();
        if (!AiModelSettingsService.TryParse(settings.DefaultModelType, out var defaultType))
            errors.Add($"Ai:DefaultModelType '{settings.DefaultModelType}' is not supported. Use 'Cloud' or 'Local'.");
        if (!Enum.TryParse<AiFallbackMode>(settings.Fallback.Mode, ignoreCase: true, out var fallback) || int.TryParse(settings.Fallback.Mode, out _))
            errors.Add($"Ai:Fallback:Mode '{settings.Fallback.Mode}' is not supported. Use 'None' or 'Automatic'.");

        var cloud = Normalize(settings.Cloud, AiModelType.Cloud, errors);
        var local = Normalize(settings.Local, AiModelType.Local, errors);
        var options = new[] { ToOption(AiModelType.Cloud, cloud), ToOption(AiModelType.Local, local) };
        if (errors.Count == 0 && !options.Any(o => o.Enabled))
            errors.Add("Ai:Cloud:Provider and Ai:Local:Provider are both 'None'. Configure at least one model.");
        else if (errors.Count == 0 && !options.Single(o => o.ModelType == defaultType).Enabled)
            errors.Add($"Ai:DefaultModelType is '{defaultType}', but Ai:{defaultType}:Provider is 'None'.");

        if (errors.Count > 0)
            throw new InvalidOperationException("Invalid AI configuration: " + string.Join(" ", errors));
        return new ValidatedAiSettings(defaultType, fallback, cloud, local, options);
    }

    private static AiModelOption ToOption(AiModelType type, AiOptions options) =>
        options.Provider == AiProviderRegistry.ProviderNone
            ? new(type, false, options.Provider, options.ModelName, $"Set Ai:{type}:Provider to enable it.")
            : new(type, true, options.Provider, options.Provider == AiProviderRegistry.ProviderFake ? "fake" : options.ModelName, null);

    private static AiOptions Normalize(AiOptions source, AiModelType type, List<string> errors)
    {
        var prefix = $"Ai:{type}:";
        var provider = new[] { AiProviderRegistry.ProviderNone, AiProviderRegistry.ProviderFake, AiProviderRegistry.ProviderOpenAi, AiProviderRegistry.ProviderAnthropic }
            .FirstOrDefault(p => string.Equals(p, source.Provider?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            errors.Add($"{prefix}Provider '{source.Provider}' is not supported. Use 'None', 'Fake', 'OpenAI' or 'Anthropic'.");
            provider = AiProviderRegistry.ProviderNone;
        }

        var endpoint = string.IsNullOrWhiteSpace(source.Endpoint)
            ? provider == AiProviderRegistry.ProviderAnthropic ? AnthropicProvider.DefaultEndpoint
              : type == AiModelType.Local ? AiProviderRegistry.DefaultLocalEndpoint : AiProviderRegistry.DefaultOpenAiEndpoint
            : source.Endpoint.Trim();
        var normalized = new AiOptions
        {
            Provider = provider,
            Endpoint = endpoint,
            ApiKey = string.IsNullOrWhiteSpace(source.ApiKey) ? null : source.ApiKey,
            ApiKeyHeader = string.IsNullOrWhiteSpace(source.ApiKeyHeader) ? "Authorization" : source.ApiKeyHeader,
            ModelName = source.ModelName?.Trim() ?? string.Empty,
            MaxOutputTokens = source.MaxOutputTokens,
            AttemptTimeoutSeconds = source.AttemptTimeoutSeconds,
            TotalTimeoutSeconds = source.TotalTimeoutSeconds,
            MaxRetryAttempts = source.MaxRetryAttempts,
            InputCostPerMillionTokens = source.InputCostPerMillionTokens,
            OutputCostPerMillionTokens = source.OutputCostPerMillionTokens
        };

        if (provider is AiProviderRegistry.ProviderNone or AiProviderRegistry.ProviderFake)
            return normalized;

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            errors.Add($"{prefix}Endpoint '{endpoint}' must be an absolute http(s) URL.");
        if (string.IsNullOrWhiteSpace(normalized.ModelName))
            errors.Add($"{prefix}ModelName is required for provider '{provider}'.");
        // Local runtimes (Ollama, llama.cpp) usually run without a key; hosted APIs never do.
        if (normalized.ApiKey is null && (type == AiModelType.Cloud || provider == AiProviderRegistry.ProviderAnthropic))
            errors.Add($"{prefix}ApiKey is required for provider '{provider}'.");
        if (normalized.AttemptTimeoutSeconds <= 0 || normalized.TotalTimeoutSeconds < normalized.AttemptTimeoutSeconds)
            errors.Add($"{prefix}AttemptTimeoutSeconds must be positive and not exceed {prefix}TotalTimeoutSeconds.");
        if (normalized.MaxOutputTokens <= 0)
            errors.Add($"{prefix}MaxOutputTokens must be positive.");
        return normalized;
    }
}
