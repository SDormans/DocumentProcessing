using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DocumentProcessing.Infrastructure.Ai;

/// <summary>
/// Metrics and traces for LLM calls. Exported by any OpenTelemetry/metrics listener subscribed to
/// <see cref="SourceName"/>.
/// </summary>
public static class AiTelemetry
{
    public const string SourceName = "DocumentProcessing.Ai";

    public static readonly ActivitySource ActivitySource = new(SourceName);
    private static readonly Meter Meter = new(SourceName);

    public static readonly Counter<long> Requests = Meter.CreateCounter<long>(
        "ai.requests", description: "LLM requests by outcome.");
    public static readonly Counter<long> InputTokens = Meter.CreateCounter<long>(
        "ai.tokens.input", unit: "{token}", description: "Prompt tokens consumed.");
    public static readonly Counter<long> OutputTokens = Meter.CreateCounter<long>(
        "ai.tokens.output", unit: "{token}", description: "Completion tokens produced.");
    public static readonly Counter<double> Cost = Meter.CreateCounter<double>(
        "ai.cost", unit: "{currency}", description: "Estimated LLM cost from configured token prices.");
    public static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "ai.request.duration", unit: "s", description: "LLM request duration including retries.");
}
