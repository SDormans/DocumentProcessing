using System.Diagnostics.Metrics;
using DocumentProcessing.Infrastructure.Queue;
using FluentAssertions;
using Xunit;

namespace DocumentProcessing.UnitTests.Observability;

public class QueueDepthGaugeTests
{
    [Fact]
    public void Gauge_reports_the_last_recorded_depth()
    {
        var observed = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "documentprocessing.queue.depth")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => observed.Add(value));
        listener.Start();

        PipelineTelemetry.RecordQueueDepth(7);
        listener.RecordObservableInstruments();

        observed.Should().ContainSingle().Which.Should().Be(7);
    }
}
