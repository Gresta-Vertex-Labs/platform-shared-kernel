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
    /// <summary>
    /// The connection factory backing every query method on this base class.
    /// </summary>
    /// <remarks>
    /// WO-051/P-321 — promoted from a private field to a protected property as the documented,
    /// supported extension seam for any Dapper capability this base class doesn't itself wrap (e.g.
    /// a stored-procedure call with output parameters, a bulk-copy operation). A subclass needing
    /// such a capability reuses the SAME open-per-call/dispose-per-call connection lifecycle instead
    /// of independently re-injecting a second <see cref="IDbConnectionFactory"/> — mirrors
    /// <c>TenantedDbContext.TenantProvider</c>'s existing protected-property pattern.
    /// </remarks>
    protected IDbConnectionFactory ConnectionFactory { get; }

    /// <summary>
    /// Initialises a new <see cref="DapperReadService"/>.
    /// </summary>
    /// <param name="connectionFactory">
    /// Factory that creates open database connections. Must not be <see langword="null"/>.
    /// </param>
    protected DapperReadService(IDbConnectionFactory connectionFactory)
    {
        ConnectionFactory = connectionFactory;
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
        using var connection = await ConnectionFactory.CreateConnectionAsync(ct);
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
        using var connection = await ConnectionFactory.CreateConnectionAsync(ct);
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
        using var connection = await ConnectionFactory.CreateConnectionAsync(ct);
        var command = new CommandDefinition(sql, parameters, cancellationToken: ct);
        return await connection.ExecuteAsync(command);
    }

    /// <summary>
    /// Executes a parameterized two-table join query, mapping each row pair to
    /// <typeparamref name="TReturn"/> via Dapper's <c>splitOn</c>-based multi-mapping.
    /// </summary>
    /// <typeparam name="TFirst">The first (leftmost) mapped type.</typeparam>
    /// <typeparam name="TSecond">The second mapped type.</typeparam>
    /// <typeparam name="TReturn">The composed return type.</typeparam>
    /// <param name="sql">The parameterized SQL query. Must not use string interpolation.</param>
    /// <param name="map">Composes one <typeparamref name="TFirst"/>/<typeparamref name="TSecond"/> pair into a <typeparamref name="TReturn"/>.</param>
    /// <param name="parameters">Query parameters, or <see langword="null"/> for queries with no parameters.</param>
    /// <param name="splitOn">The column name Dapper splits the result set on. Defaults to <c>"Id"</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An enumerable of composed <typeparamref name="TReturn"/> instances.</returns>
    /// <remarks>WO-051/P-321 — a thin wrapper over Dapper's own splitOn-based multi-mapping, for join-projection queries.</remarks>
    protected async Task<IEnumerable<TReturn>> QueryAsync<TFirst, TSecond, TReturn>(
        string sql,
        Func<TFirst, TSecond, TReturn> map,
        object? parameters = null,
        string splitOn = "Id",
        CancellationToken ct = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(ct);
        var command = new CommandDefinition(sql, parameters, cancellationToken: ct);
        return await connection.QueryAsync(command, map, splitOn);
    }

    /// <summary>
    /// Executes a parameterized three-table join query, mapping each row triple to
    /// <typeparamref name="TReturn"/> via Dapper's <c>splitOn</c>-based multi-mapping.
    /// </summary>
    /// <typeparam name="TFirst">The first (leftmost) mapped type.</typeparam>
    /// <typeparam name="TSecond">The second mapped type.</typeparam>
    /// <typeparam name="TThird">The third mapped type.</typeparam>
    /// <typeparam name="TReturn">The composed return type.</typeparam>
    /// <param name="sql">The parameterized SQL query. Must not use string interpolation.</param>
    /// <param name="map">Composes one <typeparamref name="TFirst"/>/<typeparamref name="TSecond"/>/<typeparamref name="TThird"/> triple into a <typeparamref name="TReturn"/>.</param>
    /// <param name="parameters">Query parameters, or <see langword="null"/> for queries with no parameters.</param>
    /// <param name="splitOn">The column name(s) Dapper splits the result set on. Defaults to <c>"Id"</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An enumerable of composed <typeparamref name="TReturn"/> instances.</returns>
    /// <remarks>WO-051/P-321 — a thin wrapper over Dapper's own splitOn-based multi-mapping, for join-projection queries.</remarks>
    protected async Task<IEnumerable<TReturn>> QueryAsync<TFirst, TSecond, TThird, TReturn>(
        string sql,
        Func<TFirst, TSecond, TThird, TReturn> map,
        object? parameters = null,
        string splitOn = "Id",
        CancellationToken ct = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(ct);
        var command = new CommandDefinition(sql, parameters, cancellationToken: ct);
        return await connection.QueryAsync(command, map, splitOn);
    }

    /// <summary>
    /// Executes a parameterized, multi-statement SQL batch and reads each result set sequentially
    /// off a single <see cref="SqlMapper.GridReader"/>, all within one database round trip.
    /// </summary>
    /// <typeparam name="TResult">The type produced by <paramref name="readFunc"/> from the grid reader.</typeparam>
    /// <param name="sql">The parameterized, multi-statement SQL batch. Must not use string interpolation.</param>
    /// <param name="readFunc">
    /// Reads each result set off the supplied <see cref="SqlMapper.GridReader"/> in order (e.g. via
    /// <c>grid.ReadAsync&lt;T&gt;()</c>/<c>grid.ReadSingleAsync&lt;T&gt;()</c>) and composes
    /// <typeparamref name="TResult"/>.
    /// </param>
    /// <param name="parameters">Statement parameters, or <see langword="null"/> for no parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <typeparamref name="TResult"/> produced by <paramref name="readFunc"/>.</returns>
    /// <remarks>
    /// WO-051/P-321 — a <see cref="SqlMapper.GridReader"/> streams sequential result sets over ONE
    /// open connection and cannot be read after the connection closes; this method keeps the
    /// connection open for the FULL duration of <paramref name="readFunc"/> and disposes it only
    /// after <paramref name="readFunc"/> completes.
    /// </remarks>
    protected async Task<TResult> QueryMultipleAsync<TResult>(
        string sql,
        Func<SqlMapper.GridReader, Task<TResult>> readFunc,
        object? parameters = null,
        CancellationToken ct = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(ct);
        var command = new CommandDefinition(sql, parameters, cancellationToken: ct);
        using var grid = await connection.QueryMultipleAsync(command);
        return await readFunc(grid);
    }
}
