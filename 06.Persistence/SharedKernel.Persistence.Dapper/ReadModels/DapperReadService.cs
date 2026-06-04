using Dapper;
using SharedKernel.Persistence.Abstractions.Connections;

namespace SharedKernel.Persistence.Dapper.ReadModels;

/// <summary>
/// Abstract base class for Dapper-based read-side query services.
/// </summary>
/// <remarks>
/// <para>
/// Subclasses receive an <see cref="IDbConnectionFactory"/> and use the three protected query
/// methods to execute parameterized SQL against the database. Each method opens and disposes a
/// connection per call — connection pooling is managed by the underlying
/// <see cref="Npgsql.NpgsqlDataSource"/> registered by <c>AddSharedKernelPostgreSQL</c>.
/// </para>
/// <para>
/// <strong>Hard violation:</strong> String interpolation in SQL is forbidden — parameterized
/// queries only. Pass parameters via anonymous objects or <see cref="DynamicParameters"/>.
/// </para>
/// <para>
/// SQL is caller-supplied — no query builder abstraction is provided at this layer.
/// </para>
/// </remarks>
public abstract class DapperReadService
{
    private readonly IDbConnectionFactory _connectionFactory;

    /// <summary>
    /// Initialises a new <see cref="DapperReadService"/>.
    /// </summary>
    /// <param name="connectionFactory">
    /// Factory that creates open database connections. Must not be <see langword="null"/>.
    /// </param>
    protected DapperReadService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Executes a parameterized SELECT query and returns multiple result rows.
    /// </summary>
    /// <typeparam name="TResult">The type to map each row to.</typeparam>
    /// <param name="sql">The parameterized SQL query. Must not use string interpolation.</param>
    /// <param name="parameters">
    /// Query parameters (anonymous object or <see cref="DynamicParameters"/>), or
    /// <see langword="null"/> for queries with no parameters.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An enumerable of <typeparamref name="TResult"/> instances, one per result row.
    /// Returns an empty enumerable when no rows match.
    /// </returns>
    protected async Task<IEnumerable<TResult>> QueryAsync<TResult>(
        string sql,
        object? parameters,
        CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        var command = new CommandDefinition(sql, parameters, cancellationToken: ct);
        return await connection.QueryAsync<TResult>(command);
    }

    /// <summary>
    /// Executes a parameterized SELECT query and returns a single result row, or
    /// <see langword="null"/> when no row matches.
    /// </summary>
    /// <typeparam name="TResult">The type to map the row to.</typeparam>
    /// <param name="sql">The parameterized SQL query. Must not use string interpolation.</param>
    /// <param name="parameters">
    /// Query parameters (anonymous object or <see cref="DynamicParameters"/>), or
    /// <see langword="null"/> for queries with no parameters.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The mapped <typeparamref name="TResult"/> when exactly one row matches;
    /// <see langword="null"/> when no rows match; throws when more than one row matches.
    /// </returns>
    protected async Task<TResult?> QuerySingleOrDefaultAsync<TResult>(
        string sql,
        object? parameters,
        CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        var command = new CommandDefinition(sql, parameters, cancellationToken: ct);
        return await connection.QuerySingleOrDefaultAsync<TResult>(command);
    }

    /// <summary>
    /// Executes a parameterized DML statement (INSERT, UPDATE, DELETE) and returns
    /// the number of affected rows.
    /// </summary>
    /// <param name="sql">The parameterized SQL statement. Must not use string interpolation.</param>
    /// <param name="parameters">
    /// Statement parameters (anonymous object or <see cref="DynamicParameters"/>), or
    /// <see langword="null"/> for statements with no parameters.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of rows affected by the statement.</returns>
    protected async Task<int> ExecuteAsync(
        string sql,
        object? parameters,
        CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(ct);
        var command = new CommandDefinition(sql, parameters, cancellationToken: ct);
        return await connection.ExecuteAsync(command);
    }
}
