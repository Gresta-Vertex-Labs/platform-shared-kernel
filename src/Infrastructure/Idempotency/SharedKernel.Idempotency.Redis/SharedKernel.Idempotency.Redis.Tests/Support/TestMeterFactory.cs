using System.Diagnostics.Metrics;
using SharedKernel.Idempotency.Redis.Diagnostics;

namespace SharedKernel.Idempotency.Redis.Tests.Support;

/// <summary>A minimal <see cref="IMeterFactory"/> for tests that construct the store without a host.</summary>
internal sealed class TestMeterFactory : IMeterFactory
{
    private static readonly TestMeterFactory Shared = new();

    private readonly List<Meter> _meters = [];

    /// <summary>The instruments for a store built directly in a test.</summary>
    public static RedisIdempotencyMetrics NewMetrics() => new(Shared);

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
}
