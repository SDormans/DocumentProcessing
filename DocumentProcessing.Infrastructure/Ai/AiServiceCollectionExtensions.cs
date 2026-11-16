using DocumentProcessing.Application.Services.Invoice;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DocumentProcessing.Infrastructure.Ai;

public static class AiServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="IAiProvider"/> selected by <c>Ai:Provider</c>.
    /// </summary>
    public static IServiceCollection AddAiProvider(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AiOptions.SectionName);
        services.Configure<AiOptions>(section);
        var options = section.Get<AiOptions>() ?? new AiOptions();

        if (string.Equals(options.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(options.ApiKey))
                throw new InvalidOperationException("Ai:Provider is 'OpenAI' but Ai:ApiKey is not configured.");

            services.AddHttpClient<IAiProvider, OpenAiProvider>((sp, client) =>
                    OpenAiProvider.ConfigureClient(client, sp.GetRequiredService<IOptions<AiOptions>>().Value))
                .AddStandardResilienceHandler(resilience =>
                {
                    resilience.Retry.MaxRetryAttempts = Math.Max(1, options.MaxRetryAttempts);
                    resilience.Retry.UseJitter = true;
                    resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(options.AttemptTimeoutSeconds);
                    resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(options.TotalTimeoutSeconds);
                    resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(Math.Max(options.AttemptTimeoutSeconds * 2, 30));
                });
        }
        else if (string.Equals(options.Provider, "Fake", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IAiProvider>(_ => new FakeAiProvider());
        }
        else
        {
            throw new InvalidOperationException($"Unsupported Ai:Provider '{options.Provider}'. Use 'Fake' or 'OpenAI'.");
        }

        return services;
    }
}
