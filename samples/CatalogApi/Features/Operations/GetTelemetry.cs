using SharedKernel.Application;
using SharedKernel.Primitives.Results;

namespace CatalogApi.Features.Operations;

/// <summary>The latest spans and measurements the probe recorded.</summary>
public sealed record TelemetrySnapshot(
    int SpanCount,
    int MeasurementCount,
    IEnumerable<TelemetryProbe.SpanRecord> Spans,
    IEnumerable<TelemetryProbe.MeasurementRecord> Measurements);

/// <summary>
/// Proof that search telemetry is live. Both provider packages declared an ActivitySource and a Meter and never wrote
/// to either until the pre-publish pass, while WithSearchTelemetry() subscribed to both — a green dashboard with no data.
/// </summary>
public sealed record GetTelemetry : IQuery<TelemetrySnapshot>;

public sealed class GetTelemetryHandler(TelemetryProbe probe) : IQueryHandler<GetTelemetry, TelemetrySnapshot>
{
    public Task<Result<TelemetrySnapshot>> Handle(GetTelemetry query, CancellationToken cancellationToken) =>
        Task.FromResult(Result<TelemetrySnapshot>.Success(new TelemetrySnapshot(
            probe.Spans.Count,
            probe.Measurements.Count,
            probe.Spans.TakeLast(20),
            probe.Measurements.TakeLast(20))));
}
