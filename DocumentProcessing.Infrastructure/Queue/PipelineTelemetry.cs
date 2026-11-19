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

    private static long _queueDepth = -1;

    /// <summary>Last measured queue depth; not reported until the worker has measured it once.</summary>
    public static readonly ObservableGauge<long> QueueDepth = Meter.CreateObservableGauge(
        "documentprocessing.queue.depth",
        () => Interlocked.Read(ref _queueDepth) is var depth and >= 0 ? [new Measurement<long>(depth)] : Array.Empty<Measurement<long>>(),
        unit: "{command}",
        description: "Queue commands pending or in progress.");

    public static void RecordQueueDepth(long depth) => Interlocked.Exchange(ref _queueDepth, depth);
}
