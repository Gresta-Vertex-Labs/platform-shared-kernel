using System.Data;
using System.Diagnostics;
using SharedKernel.Persistence.Abstractions.Connections;

namespace SharedKernel.Persistence.Abstractions.Diagnostics;

/// <summary>
/// Database readiness probe extensions for <see cref="IDbConnectionFactory"/>.
/// </summary>
public static class DbConnectionFactoryDiagnosticsExtensions
{
    /// <summary>
    /// Probes database connectivity by opening a connection and executing <c>SELECT 1</c>.
    /// </summary>
    /// <param name="factory">The connection factory to probe.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="DatabaseReadinessResult"/> describing the outcome. Never throws — any exception
    /// is caught and reported as <c>IsHealthy = false</c> with <c>ErrorMessage</c> populated.
    /// </returns>
    /// <remarks>
    /// Prefer <c>SharedKernelDbContext.CheckReadinessAsync</c> (EfCore package) when a
    /// <c>DbContext</c> is already in scope. This overload is intended for Dapper-only read
    /// services that have no <c>DbContext</c>. Only <see cref="System.Data"/> and
    /// <see cref="System.Diagnostics"/> types are used — zero new dependencies.
    /// </remarks>
    public static async Task<DatabaseReadinessResult> CheckReadinessAsync(
        this IDbConnectionFactory factory,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        IDbConnection? connection = null;

        try
        {
            connection = await factory.CreateConnectionAsync(ct);

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.ExecuteScalar();

            stopwatch.Stop();
            return new DatabaseReadinessResult(
                IsHealthy: true,
                Latency: stopwatch.Elapsed,
                Provider: connection.GetType().Name,
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new DatabaseReadinessResult(
                IsHealthy: false,
                Latency: stopwatch.Elapsed,
                Provider: connection?.GetType().Name ?? "unknown",
                ErrorMessage: ex.Message);
        }
        finally
        {
            connection?.Dispose();
        }
    }
}
