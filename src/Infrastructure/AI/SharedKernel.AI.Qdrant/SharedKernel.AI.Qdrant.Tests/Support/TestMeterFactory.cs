using System.Diagnostics.Metrics;
using SharedKernel.AI.Qdrant.Diagnostics;

namespace SharedKernel.AI.Qdrant.Tests.Support;

/// <summary>A minimal <see cref="IMeterFactory"/> for tests that only need an instrument home (no sharing semantics).</summary>
internal sealed class TestMeterFactory : IMeterFactory
{
    private readonly List<Meter> _meters = [];

    public Meter Create(MeterOptions options)
    {
        var meter = new Meter(options.Name, options.Version, options.Tags, scope: this);
        lock (_meters)
        {
            _meters.Add(meter);
        }

        return meter;
    }

    public void Dispose()
    {
        lock (_meters)
        {
            foreach (var meter in _meters)
            {
                meter.Dispose();
            }

            _meters.Clear();
        }
    }

    /// <summary>Creates a <see cref="QdrantTelemetry"/> over a fresh factory.</summary>
    public static QdrantTelemetry NewTelemetry() => new(new TestMeterFactory());
}
