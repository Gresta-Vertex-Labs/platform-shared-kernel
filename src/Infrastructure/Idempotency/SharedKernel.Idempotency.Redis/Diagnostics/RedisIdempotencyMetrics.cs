using System.Diagnostics;
using System.Diagnostics.Metrics;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Idempotency.Redis.Diagnostics;

/// <summary>
/// Holds the instruments of the Redis idempotency store: the operation duration histogram and the fail-open counter.
/// </summary>
/// <remarks>
/// <para>
/// A singleton created from <see cref="IMeterFactory"/> and registered by <c>AddRedisIdempotency</c> together with
/// <c>AddMetrics()</c>, so the host's container owns the meter. It is public only because the public
/// <see cref="Store.RedisIdempotencyStore"/> constructor takes it; it has no members of its own to call.
/// </para>
/// <para>
/// The instrument definitions are identical to the EF Core provider's, so a host that registers both receives one
/// stream per instrument, separated by the <see cref="IdempotencyTagKeys.Provider"/> tag.
/// </para>
/// </remarks>
public sealed class RedisIdempotencyMetrics
{
    /// <summary>The name of the meter this package records on ("SharedKernel.Idempotency"), shared by every idempotency provider.</summary>
    public const string MeterName = IdempotencyTelemetry.MeterName;

    private readonly Histogram<double> _operationDuration;
    private readonly Counter<long> _failOpen;

    /// <summary>Initializes the instruments on a meter created by <paramref name="meterFactory"/>.</summary>
    /// <param name="meterFactory">The factory that owns the meter.</param>
    /// <exception cref="ArgumentNullException"><paramref name="meterFactory"/> is <see langword="null"/>.</exception>
    public RedisIdempotencyMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(new MeterOptions(MeterName)
        {
            Version = TelemetryVersion.Of(typeof(RedisIdempotencyMetrics).Assembly),
        });

        _operationDuration = meter.CreateHistogram<double>(
            IdempotencyTelemetry.OperationDurationInstrument,
            unit: "s",
            description: "Duration, in seconds, of one idempotency store operation.");
        _failOpen = meter.CreateCounter<long>(
            IdempotencyTelemetry.FailOpenInstrument,
            unit: "{operation}",
            description: "Operations let through because the store was unavailable and fail-open is enabled.");
    }

    /// <summary>Records the duration of one operation.</summary>
    internal void RecordDuration(string operation, string purpose, string outcome, string? result, long startTimestamp)
    {
        if (!_operationDuration.Enabled)
        {
            return;
        }

        var tags = new TagList
        {
            { IdempotencyTagKeys.Operation, operation },
            { IdempotencyTagKeys.Purpose, purpose },
            { IdempotencyTagKeys.Provider, RedisIdempotencyTelemetry.ProviderName },
            { WellKnownTagKeys.Outcome, outcome },
        };
        if (result is not null)
        {
            tags.Add(IdempotencyTagKeys.Result, result);
        }

        _operationDuration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds, tags);
    }

    /// <summary>Counts one operation let through by fail-open.</summary>
    internal void RecordFailOpen(string operation, string purpose) =>
        _failOpen.Add(
            1,
            new TagList
            {
                { IdempotencyTagKeys.Operation, operation },
                { IdempotencyTagKeys.Purpose, purpose },
                { IdempotencyTagKeys.Provider, RedisIdempotencyTelemetry.ProviderName },
            });
}
