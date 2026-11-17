using DocumentProcessing.Application.Services.Ai;
using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DocumentProcessing.Infrastructure.Ai;

public static class AiServiceCollectionExtensions
{
    /// <summary>
    /// Validates <c>Ai:*</c> and registers one keyed <see cref="IAiProvider"/> per enabled model type, the
    /// <see cref="IAiProviderRegistry"/>, and an unkeyed <see cref="IAiProvider"/> for the default model type.
    /// </summary>
    public static IServiceCollection AddAiProvider(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = ValidatedAiSettings.Create(AiSettings.Bind(configuration));
        services.AddSingleton(settings);

        foreach (var modelType in new[] { AiModelType.Cloud, AiModelType.Local })
        {
            var options = settings.For(modelType);
            switch (options.Provider)
            {
                case AiProviderRegistry.ProviderFake:
                    services.AddKeyedScoped<IAiProvider>(modelType, (_, _) => new FakeAiProvider());
                    break;
                case AiProviderRegistry.ProviderOpenAi:
                    AddHttpClients(services, modelType, options, OpenAiProvider.ConfigureClient);
                    services.AddKeyedScoped<IAiProvider>(modelType, (sp, _) => new OpenAiProvider(
                        sp.GetRequiredService<IHttpClientFactory>().CreateClient(AiProviderRegistry.ClientName(modelType)),
                        options, sp.GetRequiredService<ILogger<OpenAiProvider>>()));
                    break;
                case AiProviderRegistry.ProviderAnthropic:
                    AddHttpClients(services, modelType, options, AnthropicProvider.ConfigureClient);
                    services.AddKeyedScoped<IAiProvider>(modelType, (sp, _) => new AnthropicProvider(
                        sp.GetRequiredService<IHttpClientFactory>().CreateClient(AiProviderRegistry.ClientName(modelType)),
                        options, sp.GetRequiredService<ILogger<AnthropicProvider>>()));
                    break;
            }
        }

        services.AddScoped<IAiProviderRegistry>(sp => new AiProviderRegistry(
            sp.GetRequiredService<ValidatedAiSettings>(), sp, sp.GetService<IHttpClientFactory>()));
        services.AddScoped<IAiProvider>(sp => sp.GetRequiredService<IAiProviderRegistry>().Get(settings.DefaultModelType));
        return services;
    }

    private static void AddHttpClients(IServiceCollection services, AiModelType modelType, AiOptions options, Action<HttpClient, AiOptions> configure)
    {
        services.AddHttpClient(AiProviderRegistry.ClientName(modelType), client => configure(client, options))
            .AddStandardResilienceHandler(resilience =>
            {
                resilience.Retry.MaxRetryAttempts = Math.Max(1, options.MaxRetryAttempts);
                resilience.Retry.UseJitter = true;
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(options.AttemptTimeoutSeconds);
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(options.TotalTimeoutSeconds);
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(Math.Max(options.AttemptTimeoutSeconds * 2, 30));
            });
        // Availability checks must answer quickly, so they skip the retry pipeline.
        services.AddHttpClient(AiProviderRegistry.ProbeClientName(modelType), client => configure(client, options));
    }
}
