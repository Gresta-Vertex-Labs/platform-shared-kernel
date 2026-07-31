using System.Data;
using System.Data.Common;
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
    /// <para>
    /// Prefer <c>SharedKernelDbContext.CheckReadinessAsync</c> (EfCore package) when a
    /// <c>DbContext</c> is already in scope. This overload is intended for Dapper-only read
    /// services that have no <c>DbContext</c>. Only <see cref="System.Data"/> and
    /// <see cref="System.Diagnostics"/> types are used — zero new dependencies.
    /// </para>
    /// <para>
    /// <strong>GENUINE ASYNC (CORRECTED, WO-051/P-325):</strong> the command is safe-cast to
    /// <see cref="DbCommand"/> (every shipped <see cref="IDbConnectionFactory"/> implementation —
    /// <c>NpgsqlConnectionFactory</c> — returns a genuine <see cref="DbConnection"/>/<see cref="DbCommand"/>
    /// at runtime) and its true <see cref="DbCommand.ExecuteScalarAsync(CancellationToken)"/> is
    /// awaited; a synchronous <see cref="IDbCommand.ExecuteScalar"/> fallback is retained for
    /// correctness against any hypothetical non-<see cref="DbCommand"/> <see cref="IDbCommand"/>
    /// implementer. <see cref="IDbConnectionFactory"/>'s public interface signature is completely
    /// unchanged by this fix — it is a purely internal, non-breaking implementation correction that
    /// stops blocking a thread-pool thread for the DB round trip on every K8s readiness-probe firing.
    /// </para>
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

            if (command is DbCommand dbCommand)
                await dbCommand.ExecuteScalarAsync(ct);
            else
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
