using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DocumentProcessing.Application.Services.Invoice;

/// <summary>
/// Traces and metrics for the invoice extraction pipeline stages (text extraction, LLM extraction, validation).
/// </summary>
public static class ExtractionTelemetry
{
    public const string SourceName = "DocumentProcessing.Extraction";

    public static readonly ActivitySource ActivitySource = new(SourceName);
    private static readonly Meter Meter = new(SourceName);

    public static readonly Histogram<double> StageDuration = Meter.CreateHistogram<double>(
        "documentprocessing.extraction.stage.duration", unit: "s", description: "Duration of one extraction stage.");

    public static readonly Counter<long> Extractions = Meter.CreateCounter<long>(
        "documentprocessing.extraction.results", description: "Finished invoice extractions, by outcome.");

    public static readonly Counter<long> Fallbacks = Meter.CreateCounter<long>(
        "documentprocessing.extraction.model_fallbacks", description: "Extractions that switched to the other AI model type after a failure.");

    public const string TextExtraction = "text_extraction";
    public const string LlmExtraction = "llm_extraction";
    public const string Validation = "validation";

    /// <summary>Starts a child span for <paramref name="stage"/> and records its duration when disposed.</summary>
    public static StageScope Stage(string stage) => new(stage);

    public readonly struct StageScope : IDisposable
    {
        private readonly string _stage;
        private readonly long _started;

        internal StageScope(string stage)
        {
            _stage = stage;
            _started = Stopwatch.GetTimestamp();
            Activity = ActivitySource.StartActivity($"invoice.{stage}");
        }

        public Activity? Activity { get; }

        public void Dispose()
        {
            StageDuration.Record(Stopwatch.GetElapsedTime(_started).TotalSeconds, new KeyValuePair<string, object?>("stage", _stage));
            Activity?.Dispose();
        }
    }
}
