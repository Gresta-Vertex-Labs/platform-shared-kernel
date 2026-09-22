using System.Collections.Concurrent;
using Npgsql;
using Pgvector.Npgsql;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// One cached <see cref="NpgsqlDataSource"/> per connection string for tests that configure EF Core directly
/// (<c>options.UsePostgres(TestNpgsqlDataSources.Get(connectionString))</c>) instead of through
/// <c>AddSharedKernelNpgsql(configuration)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>UsePostgres</c> takes a data source, not a connection string, because a data source owns a connection
/// pool: creating one per <c>DbContext</c> would open a pool per context. Tests build many contexts against the
/// same few databases, so this caches one data source per (connection string, pgvector) pair for the lifetime
/// of the test process. The data sources are never disposed; the process exit closes their connections.
/// </para>
/// <para>
/// Creating a data source does not connect, so model-only tests may pass an unreachable connection string.
/// </para>
/// </remarks>
public static class TestNpgsqlDataSources
{
    private static readonly ConcurrentDictionary<(string ConnectionString, bool UseVector), NpgsqlDataSource> Cache = new();

    /// <summary>Returns the process-wide data source for <paramref name="connectionString"/>.</summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="useVector">Whether to enable pgvector's ADO-level type mapping on the data source.</param>
    /// <returns>A cached, never-disposed <see cref="NpgsqlDataSource"/>.</returns>
    public static NpgsqlDataSource Get(string connectionString, bool useVector = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return Cache.GetOrAdd((connectionString, useVector), static key =>
        {
            var builder = new NpgsqlDataSourceBuilder(key.ConnectionString);
            if (key.UseVector)
                builder.UseVector();

            return builder.Build();
        });
    }
}
