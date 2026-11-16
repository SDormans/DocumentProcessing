using DocumentProcessing.Infrastructure.Observability;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace DocumentProcessing.UnitTests.Observability;

public class ObservabilityExtensionsTests
{
    private static IHost Build(string environment, params (string Key, string Value)[] settings)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environment,
            DisableDefaults = true
        });
        foreach (var (key, value) in settings)
            builder.Configuration[key] = value;
        builder.AddObservability("test-service");
        return builder.Build();
    }

    private static string? FormatterName(IHost host) =>
        host.Services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue.FormatterName;

    [Fact]
    public void Uses_json_console_logs_outside_development()
    {
        using var host = Build(Environments.Production);
        FormatterName(host).Should().Be(ConsoleFormatterNames.Json);
    }

    [Fact]
    public void Uses_plain_console_logs_in_development()
    {
        using var host = Build(Environments.Development);
        FormatterName(host).Should().Be(ConsoleFormatterNames.Simple);
    }

    [Fact]
    public void Formatter_can_be_overridden_by_configuration()
    {
        using var host = Build(Environments.Development, (ObservabilityExtensions.ConsoleFormatterKey, "json"));
        FormatterName(host).Should().Be(ConsoleFormatterNames.Json);
    }

    [Fact]
    public void Registers_tracer_and_meter_providers_without_an_otlp_endpoint()
    {
        using var host = Build(Environments.Production);
        host.Services.GetService<TracerProvider>().Should().NotBeNull();
        host.Services.GetService<MeterProvider>().Should().NotBeNull();
    }
}
