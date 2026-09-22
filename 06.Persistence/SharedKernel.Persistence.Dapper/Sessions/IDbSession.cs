using System.Data.Common;
using Dapper;

namespace SharedKernel.Persistence.Dapper.Sessions;

/// <summary>
/// An open connection and transaction to run Dapper commands on, prepared for the current caller:
/// enlisted in the ambient unit of work when there is one, and with the caller's tenant bound when row-level
/// security is on.
/// </summary>
/// <remarks>
/// <para>Use Dapper's own extension methods on <see cref="Connection"/>, passing <see cref="Transaction"/>
/// (or build the command with <see cref="Command"/>):</para>
/// <code>
/// await using var session = await sessions.OpenAsync(ct);
/// var order = await session.Connection.QuerySingleOrDefaultAsync&lt;OrderRow&gt;(
///     session.Command("SELECT id, total FROM orders WHERE id = @id", new { id }, ct));
/// await session.Connection.ExecuteAsync(session.Command("UPDATE orders SET total = @total WHERE id = @id", new { id, total }, ct));
/// await session.CommitAsync(ct);
/// </code>
/// <para>
/// Disposing a session that owns its transaction without <see cref="CommitAsync"/> rolls it back. SQL is
/// always parameterized — never interpolate values into it.
/// </para>
/// </remarks>
public interface IDbSession : IAsyncDisposable
{
    /// <summary>The open connection.</summary>
    DbConnection Connection { get; }

    /// <summary>The transaction every command of the session must use.</summary>
    DbTransaction Transaction { get; }

    /// <summary>
    /// Whether the session runs inside the ambient unit of work (<c>IUnitOfWork.ExecuteInTransactionAsync</c>).
    /// Its work then commits or rolls back with that unit of work, and <see cref="CommitAsync"/> does nothing.
    /// </summary>
    bool IsEnlisted { get; }

    /// <summary>Whether the transaction is read-only.</summary>
    bool IsReadOnly { get; }

    /// <summary>The caller's tenant, or <see langword="null"/> when none is resolved.</summary>
    Guid? TenantId { get; }

    /// <summary>
    /// The caller's tenant, for SQL that filters on it explicitly.
    /// </summary>
    /// <returns>The tenant id.</returns>
    /// <exception cref="InvalidOperationException">No tenant is resolved.</exception>
    Guid RequireTenantId();

    /// <summary>
    /// A Dapper command on this session's transaction, with the configured default command timeout.
    /// </summary>
    /// <param name="sql">The SQL, parameterized.</param>
    /// <param name="parameters">The parameters, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The command definition.</returns>
    CommandDefinition Command(string sql, object? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>Commits the session's own transaction; does nothing for an enlisted session.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CommitAsync(CancellationToken cancellationToken = default);
}
