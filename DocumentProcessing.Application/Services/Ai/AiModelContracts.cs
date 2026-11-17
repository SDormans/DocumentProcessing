using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Domain.Enums;

namespace DocumentProcessing.Application.Services.Ai;

/// <summary>What happens when the chosen model fails.</summary>
public enum AiFallbackMode
{
    /// <summary>Retry the chosen model; when retries run out the document fails and the user can retry it with the other model.</summary>
    None,

    /// <summary>Immediately try the other enabled model and record a warning on the result.</summary>
    Automatic
}

/// <param name="Enabled">False when the model type is not configured (<c>Provider</c> is <c>None</c>).</param>
public sealed record AiModelOption(AiModelType ModelType, bool Enabled, string Provider, string ModelName, string? DisabledReason);

public sealed record AiModelAvailability(AiModelType ModelType, bool Available, string Message);

/// <summary>
/// The configured cloud and local models. Core logic asks this for a model type and never depends on a concrete provider.
/// </summary>
public interface IAiProviderRegistry
{
    AiModelType DefaultModelType { get; }
    AiFallbackMode FallbackMode { get; }
    IReadOnlyList<AiModelOption> Options { get; }
    bool IsEnabled(AiModelType modelType);

    /// <exception cref="AiModelUnavailableException">The model type is not configured.</exception>
    IAiProvider Get(AiModelType modelType);

    /// <summary>Checks that the endpoint is reachable, the credentials work and, for local runtimes, the model is installed.</summary>
    Task<AiModelAvailability> CheckAvailabilityAsync(AiModelType modelType, CancellationToken cancellationToken = default);
}

/// <summary>Per-user model choice, persisted so it survives sign-out.</summary>
public interface IAiModelPreferenceStore
{
    Task<AiModelType?> GetAsync(string userId, CancellationToken cancellationToken = default);
    Task SetAsync(string userId, AiModelType modelType, CancellationToken cancellationToken = default);
}

/// <summary>The chosen model could not be used. <see cref="Message"/> is safe to show to the user.</summary>
public class AiModelUnavailableException : Exception
{
    public AiModelUnavailableException(AiModelType modelType, string message, bool isConfigurationError = false, Exception? innerException = null)
        : base(message, innerException)
    {
        ModelType = modelType;
        IsConfigurationError = isConfigurationError;
    }

    public AiModelType ModelType { get; }

    /// <summary>True when retrying cannot help, e.g. the model type is not configured.</summary>
    public bool IsConfigurationError { get; }
}
