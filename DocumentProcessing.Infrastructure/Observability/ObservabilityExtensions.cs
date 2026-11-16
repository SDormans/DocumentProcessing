using DocumentProcessing.Application.Services.Invoice;
using DocumentProcessing.Infrastructure.Ai;
using DocumentProcessing.Infrastructure.Queue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace DocumentProcessing.Infrastructure.Observability;

public static class ObservabilityExtensions
{
    /// <summary>Standard OpenTelemetry variable; when unset, telemetry is collected but not exported.</summary>
    public const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    public const string ConsoleFormatterKey = "Logging:Console:FormatterName";

    /// <summary>Activity sources and meters emitted by this application.</summary>
    public static readonly string[] Sources =
    [
        PipelineTelemetry.SourceName,
        ExtractionTelemetry.SourceName,
        AiTelemetry.SourceName
    ];

    /// <summary>
    /// Structured console logging (JSON outside Development unless <c>Logging:Console:FormatterName</c> says
    /// otherwise) with trace/span ids, plus OpenTelemetry traces, metrics and logs exported over OTLP.
    /// </summary>
    public static IHostApplicationBuilder AddObservability(
        this IHostApplicationBuilder builder,
        string serviceName,
        Action<TracerProviderBuilder>? configureTracing = null,
        Action<MeterProviderBuilder>? configureMetrics = null)
    {
        builder.Logging.Configure(options =>
            options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);

        var formatter = builder.Configuration[ConsoleFormatterKey];
        if (string.IsNullOrWhiteSpace(formatter))
            formatter = builder.Environment.IsDevelopment() ? ConsoleFormatterNames.Simple : ConsoleFormatterNames.Json;
        if (string.Equals(formatter, ConsoleFormatterNames.Json, StringComparison.OrdinalIgnoreCase))
        {
            builder.Logging.AddJsonConsole(options =>
            {
                options.IncludeScopes = true;
                options.UseUtcTimestamp = true;
                options.TimestampFormat = "O";
            });
        }
        else
        {
            builder.Logging.AddSimpleConsole(options => options.IncludeScopes = true);
        }

        builder.Logging.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
        });

        var openTelemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName,
                serviceVersion: typeof(ObservabilityExtensions).Assembly.GetName().Version?.ToString()))
            .WithTracing(tracing =>
            {
                tracing.AddSource(Sources)
                    // Npgsql's built-in ActivitySource (one span per database command).
                    .AddSource("Npgsql")
                    .AddHttpClientInstrumentation();
                configureTracing?.Invoke(tracing);
            })
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(Sources)
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
                configureMetrics?.Invoke(metrics);
            });

        if (!string.IsNullOrWhiteSpace(builder.Configuration[OtlpEndpointKey]))
            openTelemetry.UseOtlpExporter();

        return builder;
    }
}
