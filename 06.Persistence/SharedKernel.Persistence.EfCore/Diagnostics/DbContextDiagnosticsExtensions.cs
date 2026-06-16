using System.Diagnostics;
using SharedKernel.Persistence.Abstractions.Diagnostics;
using SharedKernel.Persistence.EfCore.Context;

namespace SharedKernel.Persistence.EfCore.Diagnostics;

/// <summary>
/// Database readiness probe extensions for <see cref="SharedKernelDbContext"/>.
/// </summary>
public static class DbContextDiagnosticsExtensions
{
    /// <summary>
    /// Probes database connectivity via <see cref="Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade.CanConnectAsync"/>.
    /// </summary>
    /// <param name="context">The DB context to probe.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="DatabaseReadinessResult"/> describing the outcome. Never throws — any exception
    /// is caught and reported as <c>IsHealthy = false</c> with <c>ErrorMessage</c> populated.
    /// </returns>
    /// <remarks>
    /// Prefer this overload when a <see cref="SharedKernelDbContext"/> is already in scope. Use
    /// <c>IDbConnectionFactory.CheckReadinessAsync</c> (Abstractions package) for Dapper-only read
    /// services that have no <c>DbContext</c>. This domain does not implement <c>IHealthCheck</c>;
    /// consumers in <c>13.ServiceDefaults</c> wrap this extension inside an <c>IHealthCheck</c> adapter.
    /// </remarks>
    public static async Task<DatabaseReadinessResult> CheckReadinessAsync(
        this SharedKernelDbContext context,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var provider = context.Database.ProviderName ?? "unknown";

        try
        {
            var canConnect = await context.Database.CanConnectAsync(ct);
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
                    ErrorMessage: "Database.CanConnectAsync returned false.");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new DatabaseReadinessResult(
                IsHealthy: false,
                Latency: stopwatch.Elapsed,
                Provider: provider,
                ErrorMessage: ex.Message);
        }
    }
}
