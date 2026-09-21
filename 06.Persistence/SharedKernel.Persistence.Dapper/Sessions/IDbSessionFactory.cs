using System.Data;

namespace SharedKernel.Persistence.Dapper.Sessions;

/// <summary>
/// Opens <see cref="IDbSession"/>s. Registered scoped by <c>AddSharedKernelDapper</c>.
/// </summary>
/// <remarks>
/// <para>When a session opens:</para>
/// <list type="number">
/// <item><description>
/// <strong>Ambient unit of work.</strong> Inside <c>IUnitOfWork.ExecuteInTransactionAsync</c> the session uses
/// that transaction's connection, so Dapper and EF Core writes commit or roll back together.
/// </description></item>
/// <item><description>
/// <strong>Otherwise</strong> it opens a connection — from the read-only data source for a read-only session —
/// and begins its own transaction.
/// </description></item>
/// <item><description>
/// <strong>Row-level security</strong> (<c>RowLevelSecurity:Enabled</c> of the database's settings section): the
/// caller's tenant is bound to the transaction in one statement. Inside an active <c>ICrossTenantScope</c> the
/// session opens on the cross-tenant role's data source instead (never enlisted).
/// </description></item>
/// </list>
/// </remarks>
#pragma warning disable RS0026 // The options overload is selected by its required DbSessionOptions; the token stays optional and last.
public interface IDbSessionFactory
{
    /// <summary>Opens a read-write session.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The open session.</returns>
    Task<IDbSession> OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a read-only session (<c>SET TRANSACTION READ ONLY</c>), on the read replica when one is
    /// configured. Inside the ambient unit of work it enlists instead, so it reads the unit of work's own
    /// writes; the transaction is then not marked read-only.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The open session.</returns>
    Task<IDbSession> OpenReadOnlyAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens a session with explicit options.</summary>
    /// <param name="options">The session options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The open session.</returns>
    Task<IDbSession> OpenAsync(DbSessionOptions options, CancellationToken cancellationToken = default);
}
#pragma warning restore RS0026

/// <summary>Options of <see cref="IDbSessionFactory.OpenAsync(DbSessionOptions, CancellationToken)"/>.</summary>
public sealed class DbSessionOptions
{
    /// <summary>Whether the transaction is read-only. Defaults to <see langword="false"/>.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>The isolation level of an owned transaction. Defaults to <see cref="IsolationLevel.ReadCommitted"/>.</summary>
    public IsolationLevel IsolationLevel { get; init; } = IsolationLevel.ReadCommitted;

    /// <summary>
    /// Whether to join the ambient unit of work when one is active. Defaults to <see langword="true"/>;
    /// <see langword="false"/> always opens a separate connection and transaction.
    /// </summary>
    public bool EnlistInAmbientTransaction { get; init; } = true;
}
