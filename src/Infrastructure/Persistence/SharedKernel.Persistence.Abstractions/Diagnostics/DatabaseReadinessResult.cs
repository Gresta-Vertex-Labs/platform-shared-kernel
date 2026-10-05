namespace SharedKernel.Persistence.Abstractions.Diagnostics;

/// <summary>
/// The result of a database readiness probe.
/// </summary>
/// <param name="IsHealthy">
/// <see langword="true"/> when the probe completed without error.
/// </param>
/// <param name="Latency">The round-trip time of the probe.</param>
/// <param name="Provider">A label identifying the database provider that was probed.</param>
/// <param name="ErrorMessage">
/// The error message when <paramref name="IsHealthy"/> is <see langword="false"/>; otherwise
/// <see langword="null"/>.
/// </param>
/// <remarks>
/// BCL-only — no ORM types. This domain does not implement <c>IHealthCheck</c>; consumers in
/// <c>13.ServiceDefaults</c> wrap this record inside an <c>IHealthCheck</c> adapter.
/// </remarks>
public sealed record DatabaseReadinessResult(
    bool IsHealthy,
    TimeSpan Latency,
    string Provider,
    string? ErrorMessage);
