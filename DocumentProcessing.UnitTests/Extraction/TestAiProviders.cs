using DocumentProcessing.Application.Services.Ai;
using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Domain.Enums;

namespace DocumentProcessing.UnitTests.Extraction;

/// <summary>An in-memory <see cref="IAiProviderRegistry"/> for extractor tests.</summary>
public sealed class TestAiProviders : IAiProviderRegistry
{
    private readonly Dictionary<AiModelType, IAiProvider> _providers = new();

    public static TestAiProviders For(IAiProvider cloud) => new TestAiProviders().With(AiModelType.Cloud, cloud);

    public TestAiProviders With(AiModelType modelType, IAiProvider provider)
    {
        _providers[modelType] = provider;
        return this;
    }

    public AiModelType DefaultModelType { get; set; } = AiModelType.Cloud;
    public AiFallbackMode FallbackMode { get; set; } = AiFallbackMode.None;

    public IReadOnlyList<AiModelOption> Options =>
        Enum.GetValues<AiModelType>().Select(t => _providers.ContainsKey(t)
            ? new AiModelOption(t, true, "Test", $"{t.ToString().ToLowerInvariant()}-model", null)
            : new AiModelOption(t, false, "None", string.Empty, $"Set Ai:{t}:Provider to enable it.")).ToList();

    public bool IsEnabled(AiModelType modelType) => _providers.ContainsKey(modelType);

    public IAiProvider Get(AiModelType modelType) => _providers.TryGetValue(modelType, out var provider)
        ? provider
        : throw new AiModelUnavailableException(modelType, $"The {modelType} model is not configured.", isConfigurationError: true);

    public Task<AiModelAvailability> CheckAvailabilityAsync(AiModelType modelType, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AiModelAvailability(modelType, IsEnabled(modelType), "test"));
}
