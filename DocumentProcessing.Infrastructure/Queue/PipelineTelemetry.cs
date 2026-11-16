using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DocumentProcessing.Infrastructure.Queue;

public static class PipelineTelemetry
{
    public const string SourceName = "DocumentProcessing.Pipeline";

    public static readonly ActivitySource ActivitySource = new(SourceName);

    private static readonly Meter Meter = new(SourceName);

    public static readonly Counter<long> CommandsProcessed = Meter.CreateCounter<long>(
        "documentprocessing.pipeline.commands", description: "Queue commands handled, by outcome.");

    public static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "documentprocessing.pipeline.duration", unit: "s", description: "Time to process one queue command.");
}
