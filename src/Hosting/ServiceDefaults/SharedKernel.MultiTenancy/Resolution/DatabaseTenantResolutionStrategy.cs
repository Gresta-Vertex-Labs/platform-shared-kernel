using SharedKernel.Execution.Tenancy;
using System.Data.Common;
using Microsoft.AspNetCore.Http;
using SharedKernel.Persistence.Abstractions.Connections;

namespace SharedKernel.MultiTenancy.Resolution;

/// <summary>
/// Resolves the tenant identifier from a tenant-directory lookup keyed by the request host (or
/// subdomain), via <see cref="IDbConnectionFactory"/>.
/// </summary>
/// <remarks>
/// <para>
/// For DB-per-tenant / schema-per-tenant isolation models, where the tenant is not visible in a
/// header or claim. Distinct from <c>06.Persistence</c>'s <c>TenantedDbContext</c>: that applies a
/// row-level filter once the tenant is already known; this strategy answers the prior question of
/// "which tenant is this request for".
/// </para>
/// <para>
/// Uses a parameterized query exclusively — the request-derived host value is never
/// string-interpolated or concatenated into SQL text.
/// </para>
/// </remarks>
public sealed class DatabaseTenantResolutionStrategy(IDbConnectionFactory connectionFactory)
    : ITenantResolutionStrategy
{
    private const string LookupCommandText =
        "SELECT tenant_id FROM tenant_directory WHERE host = @host";

    /// <inheritdoc/>
    public string StrategyName => TenantResolutionStrategyNames.Database;

    /// <inheritdoc/>
    public async Task<TenantId?> TryResolveAsync(HttpContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var host = context.Request.Host.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var connection = await connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = LookupCommandText;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@host";
            parameter.Value = host;
            command.Parameters.Add(parameter);

            // Use the genuine async ADO.NET path (DbCommand.ExecuteScalarAsync) with the supplied
            // CancellationToken threaded through, rather than blocking a thread-pool thread via the
            // synchronous IDbCommand.ExecuteScalar(). Every IDbConnectionFactory implementation in
            // this platform (NpgsqlConnectionFactory et al.) returns a DbCommand-derived command.
            var result = command is DbCommand dbCommand
                ? await dbCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
                : command.ExecuteScalar();

            return result is null or DBNull
                ? null
                : TenantId.TryParse(result.ToString(), out var tenantId) ? tenantId : null;
        }
        finally
        {
            connection.Dispose();
        }
    }
}
