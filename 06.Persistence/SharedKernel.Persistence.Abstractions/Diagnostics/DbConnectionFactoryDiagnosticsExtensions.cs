using System.Diagnostics;
using SharedKernel.Persistence.Abstractions.Connections;

namespace SharedKernel.Persistence.Abstractions.Diagnostics;

/// <summary>
/// Database readiness probe extensions for <see cref="IDbConnectionFactory"/>.
/// </summary>
public static class DbConnectionFactoryDiagnosticsExtensions
{
    /// <summary>The default upper bound a probe call is allowed to run before it is abandoned.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Probes database connectivity by opening a connection and executing <c>SELECT 1</c>.
    /// </summary>
    /// <param name="factory">The connection factory to probe.</param>
    /// <param name="timeout">
    /// The maximum time the probe is allowed to run before it is abandoned and reported unhealthy.
    /// Defaults to <see cref="DefaultTimeout"/> when omitted. A hung driver/network call — not just a
    /// fast failure — must not block a readiness check indefinitely.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="DatabaseReadinessResult"/> describing the outcome. Never throws for a connectivity
    /// failure or a timeout — both are reported as <c>IsHealthy = false</c>. Throws
    /// <see cref="OperationCanceledException"/> only when <paramref name="cancellationToken"/> itself
    /// requested the cancellation (the caller's own shutdown/deadline) — that case must propagate
    /// rather than being reported as an unhealthy result.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Prefer <c>SharedKernelDbContext.CheckReadinessAsync</c> (EfCore package) when a
    /// <c>DbContext</c> is already in scope. This overload is intended for Dapper-only read
    /// services that have no <c>DbContext</c>. Only <see cref="System.Data.Common"/> and
    /// <see cref="System.Diagnostics"/> types are used — zero new dependencies.
    /// </para>
    /// <para>
    /// Genuinely async: <see cref="IDbConnectionFactory.CreateConnectionAsync"/> returns
    /// <see cref="System.Data.Common.DbConnection"/> directly, so this probe always
    /// awaits the real <see cref="System.Data.Common.DbCommand.ExecuteScalarAsync(CancellationToken)"/>
    /// with no runtime type check.
    /// </para>
    /// <para>
    /// <strong>No exception detail is ever surfaced:</strong> <see cref="DatabaseReadinessResult.ErrorMessage"/>
    /// carries only the failing exception's CLR type name (e.g. <c>"NpgsqlException"</c>) or
    /// <c>"Timeout"</c> — never <see cref="Exception.Message"/>. A driver-level connection failure
    /// message can embed the connection string, host, or credentials; a health-check endpoint is
    /// frequently exposed with weaker authentication than the application itself, so leaking that
    /// detail there is a real information-disclosure risk. Full exception detail belongs in
    /// server-side logs, not a readiness-probe response body.
    /// </para>
    /// </remarks>
    public static async Task<DatabaseReadinessResult> CheckReadinessAsync(
        this IDbConnectionFactory factory,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        System.Data.Common.DbConnection? connection = null;

        using var timeoutCts = new CancellationTokenSource(timeout ?? DefaultTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            connection = await factory.CreateConnectionAsync(linkedCts.Token);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";

            await command.ExecuteScalarAsync(linkedCts.Token);

            stopwatch.Stop();
            return new DatabaseReadinessResult(
                IsHealthy: true,
                Latency: stopwatch.Elapsed,
                Provider: connection.GetType().Name,
                ErrorMessage: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller's own token requested cancellation (shutdown/deadline) — propagate rather
            // than reporting a fabricated unhealthy result for a probe the caller no longer wants.
            throw;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new DatabaseReadinessResult(
                IsHealthy: false,
                Latency: stopwatch.Elapsed,
                Provider: connection?.GetType().Name ?? "unknown",
                ErrorMessage: "Timeout");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new DatabaseReadinessResult(
                IsHealthy: false,
                Latency: stopwatch.Elapsed,
                Provider: connection?.GetType().Name ?? "unknown",
                ErrorMessage: ex.GetType().Name);
        }
        finally
        {
            if (connection is not null)
                await connection.DisposeAsync();
        }
    }
}
