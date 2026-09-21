using System.Diagnostics;
using SharedKernel.Persistence.Abstractions.Diagnostics;
using SharedKernel.Persistence.EfCore.Context;

namespace SharedKernel.Persistence.EfCore.Diagnostics;

/// <summary>
/// Database readiness probe extensions for <see cref="SharedKernelDbContext"/>.
/// </summary>
public static class DbContextDiagnosticsExtensions
{
    private static readonly TimeSpan DefaultReadinessTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Probes database connectivity via <see cref="Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade.CanConnectAsync"/>.
    /// </summary>
    /// <param name="context">The DB context to probe.</param>
    /// <param name="timeout">
    /// The maximum time the probe is allowed to run before it is abandoned and reported unhealthy.
    /// Defaults to 5 seconds when omitted.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="DatabaseReadinessResult"/> describing the outcome. Never throws for a
    /// connectivity failure or timeout — both are reported as <c>IsHealthy = false</c>. Throws
    /// <see cref="OperationCanceledException"/> only when <paramref name="cancellationToken"/> itself
    /// requested the cancellation.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Prefer this overload when a <see cref="SharedKernelDbContext"/> is already in scope. Use
    /// <c>IDbConnectionFactory.CheckReadinessAsync</c> (Abstractions package) for Dapper-only read
    /// services that have no <c>DbContext</c>. This domain does not implement <c>IHealthCheck</c>;
    /// consumers in <c>13.ServiceDefaults</c> wrap this extension inside an <c>IHealthCheck</c> adapter.
    /// </para>
    /// <para>
    /// <strong>No exception detail is ever surfaced:</strong> mirrors
    /// <see cref="DbConnectionFactoryDiagnosticsExtensions.CheckReadinessAsync"/> — a driver-level
    /// connection failure message can embed the connection string, host, or credentials, so
    /// <see cref="DatabaseReadinessResult.ErrorMessage"/> carries only the failing exception's CLR
    /// type name or <c>"Timeout"</c>, never <see cref="Exception.Message"/>.
    /// </para>
    /// </remarks>
    public static async Task<DatabaseReadinessResult> CheckReadinessAsync(
        this SharedKernelDbContext context,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var provider = context.Database.ProviderName ?? "unknown";

        using var timeoutCts = new CancellationTokenSource(timeout ?? DefaultReadinessTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var canConnect = await context.Database.CanConnectAsync(linkedCts.Token);
            stopwatch.Stop();

            return canConnect
                ? new DatabaseReadinessResult(
                    IsHealthy: true,
                    Latency: stopwatch.Elapsed,
                    Provider: provider,
                    ErrorMessage: null)
                        : new DatabaseReadinessResult(
                    IsHealthy: false,
                    Latency: stopwatch.Elapsed,
                    Provider: provider,
                    ErrorMessage: "CanConnectFalse");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new DatabaseReadinessResult(
                IsHealthy: false,
                Latency: stopwatch.Elapsed,
                Provider: provider,
                ErrorMessage: "Timeout");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new DatabaseReadinessResult(
                IsHealthy: false,
                Latency: stopwatch.Elapsed,
                Provider: provider,
                ErrorMessage: ex.GetType().Name);
        }
    }
}
