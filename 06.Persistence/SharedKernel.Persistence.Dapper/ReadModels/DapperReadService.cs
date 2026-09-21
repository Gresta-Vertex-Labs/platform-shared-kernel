using System.Diagnostics;
using Dapper;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Dapper.Diagnostics;
using SharedKernel.Persistence.Dapper.Options;

namespace SharedKernel.Persistence.Dapper.ReadModels;

/// <summary>
/// Abstract base class for Dapper-based read-side query services.
/// </summary>
/// <remarks>
/// <para>
/// Subclasses receive an <see cref="IDbConnectionFactory"/> and use the query methods to execute
/// parameterized SQL against the database. Each method opens and disposes a connection per call —
/// connection pooling is managed by the underlying <see cref="Npgsql.NpgsqlDataSource"/> registered
/// by <c>AddSharedKernelNpgsql</c>.
/// </para>
/// <para>
/// <strong>Hard violation:</strong> String interpolation in SQL is forbidden — parameterized
/// queries only. Pass parameters via anonymous objects or <see cref="DynamicParameters"/>. Enforced
/// by <c>00.Governance</c>'s SK0042 analyzer, which flags a non-constant <c>sql</c> argument passed
/// to any method on this class.
/// </para>
/// <para>
/// <strong>Read-only:</strong> this class has no <c>ExecuteAsync</c> method — a
/// write belongs on <see cref="DapperCommandService"/>, which enlists in
/// <c>IUnitOfWork</c>'s ambient transaction when one is active. Keeping writes off the
/// read base makes "this type only ever reads" a property callers can rely on without inspecting
/// every method.
/// </para>
/// <para>
/// SQL is caller-supplied — no query builder abstraction is provided at this layer.
/// </para>
/// </remarks>
#pragma warning disable RS0026 // Symbol has multiple public overloads with optional parameters.
// QueryAsync<TResult>, QueryAsync<TFirst,TSecond,TReturn> and QueryAsync<TFirst,TSecond,TThird,TReturn>
// are Dapper's own single-mapping vs. two-way vs. three-way multi-mapping shapes — the generic arity
// a caller supplies (explicitly or via type inference from the `map` delegate's parameter count)
// already selects one specific overload; there is no shared call shape across the three for a
// trailing optional parameter to ever disambiguate incorrectly.
public abstract class DapperReadService
{
    /// <summary>
    /// The connection factory backing every query method on this base class.
    /// </summary>
    /// <remarks>
    /// Promoted from a private field to a protected property as the documented,
    /// supported extension seam for any Dapper capability this base class doesn't itself wrap (e.g.
    /// a stored-procedure call with output parameters, a bulk-copy operation). A subclass needing
    /// such a capability reuses the SAME open-per-call/dispose-per-call connection lifecycle instead
    /// of independently re-injecting a second <see cref="IDbConnectionFactory"/> — mirrors
    /// <c>TenantedDbContext.TenantProvider</c>'s existing protected-property pattern.
    /// </remarks>
    protected IDbConnectionFactory ConnectionFactory { get; }

    private readonly int? _defaultCommandTimeoutSeconds;

    /// <summary>
    /// Initialises a new <see cref="DapperReadService"/>.
    /// </summary>
    /// <param name="connectionFactory">
    /// Factory that creates open database connections. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="options">
    /// Optional default command timeout, applied to a call that does not supply its own
    /// <c>commandTimeout</c> argument. Resolved by DI when
    /// <c>AddSharedKernelDapper(IServiceCollection, IConfiguration)</c> was called; otherwise
    /// <see langword="null"/> (defers to Dapper's/the provider's own default).
    /// </param>
    protected DapperReadService(IDbConnectionFactory connectionFactory, DapperPersistenceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ConnectionFactory = connectionFactory;
        _defaultCommandTimeoutSeconds = options?.DefaultCommandTimeoutSeconds;
    }

    /// <summary>
    /// Executes a parameterized SELECT query and returns every matching row.
    /// </summary>
    /// <typeparam name="TResult">The type to map each row to.</typeparam>
    /// <param name="sql">The parameterized SQL query. Must not use string interpolation.</param>
    /// <param name="parameters">
    /// Query parameters (anonymous object or <see cref="DynamicParameters"/>), or
    /// <see langword="null"/> for queries with no parameters.
    /// </param>
    /// <param name="commandTimeout">
    /// Per-call command timeout (seconds), overriding the injected default. <see langword="null"/>
    /// defers to the injected default, then to Dapper's/the provider's own default.
    /// </param>
    /// <param name="flags">Dapper execution flags. Defaults to <see cref="CommandFlags.Buffered"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Every matching row, mapped to <typeparamref name="TResult"/>. Empty, never
    /// <see langword="null"/>, when no rows match.
    /// </returns>
    protected async Task<IReadOnlyList<TResult>> QueryAsync<TResult>(
        string sql,
        object? parameters,
        int? commandTimeout = null,
        CommandFlags flags = CommandFlags.Buffered,
        CancellationToken cancellationToken = default)
    {
        using var activity = DapperActivitySource.Source.StartActivity("Dapper.Query", ActivityKind.Client);
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        var command = new CommandDefinition(
            sql, parameters, commandTimeout: EffectiveTimeout(commandTimeout), flags: flags, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<TResult>(command);
        return rows.AsList();
    }

    /// <summary>
    /// Executes a parameterized SELECT query and streams each row as it is read from the server,
    /// without buffering the full result set in memory.
    /// </summary>
    /// <typeparam name="TResult">The type to map each row to.</typeparam>
    /// <param name="sql">The parameterized SQL query. Must not use string interpolation.</param>
    /// <param name="parameters">Query parameters, or <see langword="null"/> for none.</param>
    /// <param name="commandTimeout">See <see cref="QueryAsync{TResult}"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="IAsyncEnumerable{T}"/> streaming each mapped row.</returns>
    /// <remarks>
    /// The underlying connection stays open for the full duration of enumeration — the caller must
    /// fully enumerate (or dispose the enumerator) promptly; do not hold a partially-enumerated
    /// result across an unrelated long-running operation.
    /// </remarks>
    protected async IAsyncEnumerable<TResult> QueryUnbufferedAsync<TResult>(
        string sql,
        object? parameters,
        int? commandTimeout = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = DapperActivitySource.Source.StartActivity("Dapper.QueryUnbuffered", ActivityKind.Client);
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);

        // Dapper's QueryUnbufferedAsync has no CommandDefinition/CancellationToken-accepting overload
        // — cancellation for this streaming path is caller-side only (stop enumerating).
        await foreach (var row in connection.QueryUnbufferedAsync<TResult>(
            sql, parameters, commandTimeout: EffectiveTimeout(commandTimeout)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return row;
        }
    }

    /// <summary>
    /// Executes a parameterized SELECT query and returns the first row, or <see langword="null"/>
    /// when no row matches. Never throws for more than one matching row — the extra rows are
    /// discarded.
    /// </summary>
    /// <typeparam name="TResult">The type to map the row to.</typeparam>
    /// <param name="sql">The parameterized SQL query. Must not use string interpolation.</param>
    /// <param name="parameters">Query parameters, or <see langword="null"/> for none.</param>
    /// <param name="commandTimeout">See <see cref="QueryAsync{TResult}"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected async Task<TResult?> QueryFirstOrDefaultAsync<TResult>(
        string sql,
        object? parameters,
        int? commandTimeout = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = DapperActivitySource.Source.StartActivity("Dapper.QueryFirstOrDefault", ActivityKind.Client);
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        var command = new CommandDefinition(sql, parameters, commandTimeout: EffectiveTimeout(commandTimeout), cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<TResult>(command);
    }

    /// <summary>
    /// Executes a parameterized SELECT query and returns a single result row, or
    /// <see langword="null"/> when no row matches.
    /// </summary>
    /// <typeparam name="TResult">The type to map the row to.</typeparam>
    /// <param name="sql">The parameterized SQL query. Must not use string interpolation.</param>
    /// <param name="parameters">Query parameters, or <see langword="null"/> for none.</param>
    /// <param name="commandTimeout">See <see cref="QueryAsync{TResult}"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The mapped <typeparamref name="TResult"/> when exactly one row matches;
    /// <see langword="null"/> when no rows match; throws when more than one row matches.
    /// </returns>
    protected async Task<TResult?> QuerySingleOrDefaultAsync<TResult>(
        string sql,
        object? parameters,
        int? commandTimeout = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = DapperActivitySource.Source.StartActivity("Dapper.QuerySingleOrDefault", ActivityKind.Client);
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        var command = new CommandDefinition(sql, parameters, commandTimeout: EffectiveTimeout(commandTimeout), cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<TResult>(command);
    }

    /// <summary>
    /// Executes a parameterized query that returns a single scalar value (e.g. <c>COUNT(*)</c>,
    /// <c>EXISTS(...)</c>).
    /// </summary>
    /// <typeparam name="TResult">The scalar result type.</typeparam>
    /// <param name="sql">The parameterized SQL query. Must not use string interpolation.</param>
    /// <param name="parameters">Query parameters, or <see langword="null"/> for none.</param>
    /// <param name="commandTimeout">See <see cref="QueryAsync{TResult}"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected async Task<TResult?> ExecuteScalarAsync<TResult>(
        string sql,
        object? parameters,
        int? commandTimeout = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = DapperActivitySource.Source.StartActivity("Dapper.ExecuteScalar", ActivityKind.Client);
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        var command = new CommandDefinition(sql, parameters, commandTimeout: EffectiveTimeout(commandTimeout), cancellationToken: cancellationToken);
        return await connection.ExecuteScalarAsync<TResult>(command);
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
    /// <param name="commandTimeout">See <see cref="QueryAsync{TResult}"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every composed <typeparamref name="TReturn"/> row.</returns>
    /// <remarks>A thin wrapper over Dapper's own splitOn-based multi-mapping, for join-projection queries.</remarks>
    protected async Task<IReadOnlyList<TReturn>> QueryAsync<TFirst, TSecond, TReturn>(
        string sql,
        Func<TFirst, TSecond, TReturn> map,
        object? parameters = null,
        string splitOn = "Id",
        int? commandTimeout = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        var command = new CommandDefinition(sql, parameters, commandTimeout: EffectiveTimeout(commandTimeout), cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync(command, map, splitOn);
        return rows.AsList();
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
    /// <param name="commandTimeout">See <see cref="QueryAsync{TResult}"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every composed <typeparamref name="TReturn"/> row.</returns>
    /// <remarks>A thin wrapper over Dapper's own splitOn-based multi-mapping, for join-projection queries.</remarks>
    protected async Task<IReadOnlyList<TReturn>> QueryAsync<TFirst, TSecond, TThird, TReturn>(
        string sql,
        Func<TFirst, TSecond, TThird, TReturn> map,
        object? parameters = null,
        string splitOn = "Id",
        int? commandTimeout = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        var command = new CommandDefinition(sql, parameters, commandTimeout: EffectiveTimeout(commandTimeout), cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync(command, map, splitOn);
        return rows.AsList();
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
    /// <param name="commandTimeout">See <see cref="QueryAsync{TResult}"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The <typeparamref name="TResult"/> produced by <paramref name="readFunc"/>.</returns>
    /// <remarks>
    /// A <see cref="SqlMapper.GridReader"/> streams sequential result sets over ONE
    /// open connection and cannot be read after the connection closes; this method keeps the
    /// connection open for the FULL duration of <paramref name="readFunc"/> and disposes it only
    /// after <paramref name="readFunc"/> completes.
    /// </remarks>
    protected async Task<TResult> QueryMultipleAsync<TResult>(
        string sql,
        Func<SqlMapper.GridReader, Task<TResult>> readFunc,
        object? parameters = null,
        int? commandTimeout = null,
        CancellationToken cancellationToken = default)
    {
        using var connection = await ConnectionFactory.CreateConnectionAsync(cancellationToken);
        var command = new CommandDefinition(sql, parameters, commandTimeout: EffectiveTimeout(commandTimeout), cancellationToken: cancellationToken);
        using var grid = await connection.QueryMultipleAsync(command);
        return await readFunc(grid);
    }

    private int? EffectiveTimeout(int? commandTimeout) => commandTimeout ?? _defaultCommandTimeoutSeconds;
}
#pragma warning restore RS0026
