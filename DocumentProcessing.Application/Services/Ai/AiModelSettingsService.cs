using DocumentProcessing.Domain.Enums;

namespace DocumentProcessing.Application.Services.Ai;

public class AiModelSettingsDto
{
    public string SelectedModelType { get; set; } = string.Empty;
    public string DefaultModelType { get; set; } = string.Empty;
    public string FallbackMode { get; set; } = string.Empty;
    public IReadOnlyList<AiModelOptionDto> Options { get; set; } = [];
}

public class AiModelOptionDto
{
    public string ModelType { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string? DisabledReason { get; set; }

    /// <summary>Only set when availability was checked.</summary>
    public bool? Available { get; set; }
    public string? AvailabilityMessage { get; set; }
}

public class UpdateAiModelSettingsRequest
{
    public string? ModelType { get; set; }
}

public sealed record AiModelSelectionResult(bool Success, string? Error, AiModelSettingsDto? Settings);

/// <summary>Reads and stores the current user's model choice and resolves the model for new uploads.</summary>
public class AiModelSettingsService
{
    private readonly IAiProviderRegistry _registry;
    private readonly IAiModelPreferenceStore _preferences;

    public AiModelSettingsService(IAiProviderRegistry registry, IAiModelPreferenceStore preferences)
    {
        _registry = registry;
        _preferences = preferences;
    }

    public bool IsEnabled(AiModelType modelType) => _registry.IsEnabled(modelType);

    /// <summary>The user's saved choice when it is still enabled, otherwise the configured default.</summary>
    public async Task<AiModelType> ResolveAsync(string? userId, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(userId) && await _preferences.GetAsync(userId, cancellationToken) is { } saved && _registry.IsEnabled(saved))
            return saved;
        return _registry.DefaultModelType;
    }

    public async Task<AiModelSettingsDto> GetAsync(string? userId, bool checkAvailability, CancellationToken cancellationToken = default)
    {
        var options = new List<AiModelOptionDto>();
        foreach (var option in _registry.Options)
        {
            var dto = new AiModelOptionDto
            {
                ModelType = option.ModelType.ToString(),
                Enabled = option.Enabled,
                Provider = option.Provider,
                ModelName = option.ModelName,
                DisabledReason = option.DisabledReason
            };
            if (checkAvailability && option.Enabled)
            {
                var availability = await _registry.CheckAvailabilityAsync(option.ModelType, cancellationToken);
                dto.Available = availability.Available;
                dto.AvailabilityMessage = availability.Message;
            }
            options.Add(dto);
        }

        return new AiModelSettingsDto
        {
            SelectedModelType = (await ResolveAsync(userId, cancellationToken)).ToString(),
            DefaultModelType = _registry.DefaultModelType.ToString(),
            FallbackMode = _registry.FallbackMode.ToString(),
            Options = options
        };
    }

    public async Task<AiModelSelectionResult> SelectAsync(string? userId, string? modelType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
            return new(false, "You must be signed in to choose a model.", null);
        if (!TryParse(modelType, out var parsed))
            return new(false, $"Unknown model type '{modelType}'. Use 'Cloud' or 'Local'.", null);
        if (!_registry.IsEnabled(parsed))
        {
            var reason = _registry.Options.FirstOrDefault(o => o.ModelType == parsed)?.DisabledReason;
            return new(false, $"The {parsed} model is not available. {reason}".Trim(), null);
        }

        await _preferences.SetAsync(userId, parsed, cancellationToken);
        return new(true, null, await GetAsync(userId, checkAvailability: false, cancellationToken));
    }

    public static bool TryParse(string? value, out AiModelType modelType) =>
        Enum.TryParse(value, ignoreCase: true, out modelType) && Enum.IsDefined(modelType) && !int.TryParse(value, out _);
}
