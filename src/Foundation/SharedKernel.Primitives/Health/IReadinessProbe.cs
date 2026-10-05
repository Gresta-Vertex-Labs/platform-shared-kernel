namespace SharedKernel.Primitives.Health;

/// <summary>
/// Reports whether one external dependency of this process — a broker connection, a Redis connection, a
/// storage bucket, a search index, a key vault — is ready to serve requests.
/// </summary>
/// <remarks>
/// <para>
/// <b>One contract for every provider.</b> Each provider package implements this interface over the client it
/// already registered (never a second connection) and registers the probe itself, as a singleton, when the
/// provider is registered. A provider with several targets — named storage stores, search indexes, vector
/// collections — registers one probe per target, each with its own <see cref="Name"/>.
/// </para>
/// <para>
/// <b>The host decides what to do with probes.</b> Provider packages never reference a health-checks library.
/// <c>SharedKernel.ServiceDefaults</c>' <c>AddSharedKernelReadiness()</c> maps every registered probe to a
/// health check on the readiness endpoint; a host without ASP.NET Core can resolve
/// <c>IEnumerable&lt;IReadinessProbe&gt;</c> and call the probes itself.
/// </para>
/// <para>
/// <b>Construction must be cheap and must not resolve the client.</b> A host constructs every registered probe to
/// read its <see cref="Name"/> while it builds its health checks — possibly while hosted services are still being
/// created. A probe therefore takes <see cref="IServiceProvider"/> (or other cheap dependencies) and resolves the
/// connection, client or hosted service inside <see cref="ProbeAsync"/>; resolving a hosted service in the
/// constructor can become a circular dependency, and building a client there opens connections early.
/// </para>
/// <para>
/// <b>Failures are reported, not thrown.</b> An unreachable dependency is a
/// <see cref="ReadinessStatus.Unhealthy"/> report. Only cancellation of <c>cancellationToken</c> throws.
/// Descriptions and data may be shown on a health endpoint, so they never contain connection strings,
/// credentials or exception messages.
/// </para>
/// </remarks>
public interface IReadinessProbe
{
    /// <summary>
    /// The probe's name, unique within the process — for example <c>"redis"</c> or <c>"storage-invoices"</c>.
    /// A host uses it as the health-check registration name.
    /// </summary>
    string Name { get; }

    /// <summary>Checks the dependency once.</summary>
    /// <param name="cancellationToken">A token to cancel the check.</param>
    /// <returns>The outcome of the check.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<ReadinessReport> ProbeAsync(CancellationToken cancellationToken = default);
}
