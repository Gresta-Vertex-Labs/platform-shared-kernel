using System.Data;
using Npgsql;
using SharedKernel.Persistence.Abstractions.Connections;

namespace SharedKernel.Persistence.PostgreSQL.Connections;

/// <summary>
/// PostgreSQL implementation of <see cref="IDbConnectionFactory"/> backed by a pooled
/// <see cref="NpgsqlDataSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CreateConnectionAsync"/> opens a new connection from the Npgsql connection pool and
/// returns it as an open <see cref="IDbConnection"/>. The caller is responsible for disposal —
/// disposing the connection returns it to the pool.
/// </para>
/// <para>
/// Connection pooling is managed by Npgsql's <see cref="NpgsqlDataSource"/>. Do not create
/// <c>NpgsqlConnection</c> instances directly in Dapper services — always use this factory.
/// </para>
/// </remarks>
public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    /// Initialises a new <see cref="NpgsqlConnectionFactory"/>.
    /// </summary>
    /// <param name="dataSource">The Npgsql data source that manages the connection pool.</param>
    public NpgsqlConnectionFactory(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns an open <see cref="NpgsqlConnection"/> from the connection pool.
    /// The caller must dispose the connection to return it to the pool.
    /// </remarks>
    public async Task<IDbConnection> CreateConnectionAsync(CancellationToken ct = default)
    {
        return await _dataSource.OpenConnectionAsync(ct);
    }
}
