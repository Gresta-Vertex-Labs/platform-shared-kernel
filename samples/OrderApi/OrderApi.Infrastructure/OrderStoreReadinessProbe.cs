using SharedKernel.Primitives.Health;

namespace OrderApi.Infrastructure;

/// <summary>
/// The readiness probe of the order store. Every adapter that owns a dependency registers one
/// <see cref="IReadinessProbe"/> for it; the host maps them all with <c>AddSharedKernelReadiness()</c> and never
/// needs to know which adapters exist.
/// </summary>
/// <remarks>
/// An in-memory store is always reachable, so this probe always reports healthy. A database-backed store
/// would issue a cheap round trip here; the constructor stays cheap either way.
/// </remarks>
public sealed class OrderStoreReadinessProbe : IReadinessProbe
{
    /// <summary>The probe name, which is also the health-check name on <c>/health/ready</c>.</summary>
    public const string ProbeName = "order-store";

    /// <inheritdoc />
    public string Name => ProbeName;

    /// <inheritdoc />
    public Task<ReadinessReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadinessReport.Healthy("In-memory order store."));
    }
}
