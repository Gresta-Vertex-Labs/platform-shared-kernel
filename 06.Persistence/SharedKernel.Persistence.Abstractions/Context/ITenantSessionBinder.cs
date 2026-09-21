using System.Data.Common;

namespace SharedKernel.Persistence.Abstractions.Context;

/// <summary>
/// Binds the current tenant identity — and, when active, the cross-tenant bypass escape clause — to
/// a database session, so server-side row-level security (RLS) policies can enforce tenant isolation
/// independently of application-layer query filters.
/// </summary>
/// <remarks>
/// <para>
/// Two binding shapes exist because the two consumers of this seam attach to a database session
/// differently:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <see cref="BindAsync"/> — transaction-scoped, self-resetting. Used by a caller (e.g. a Dapper
/// read service) that already opens its own explicit transaction for the duration of one logical
/// operation. PostgreSQL discards a transaction-local setting automatically at commit or rollback, so
/// a pooled connection can never carry a stale binding into its next lease through this path alone.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="BindConnectionAsync"/>/<see cref="ResetConnectionAsync"/> — connection/session-scoped.
/// Used by a caller (the EF Core row-level-security connection interceptor) that cannot guarantee an
/// explicit transaction wraps every command — a plain, non-transactional read is a single statement
/// with no <c>BEGIN</c> at all. Binding at connection-open time makes the setting visible to every
/// command issued on that connection lease, regardless of whether it runs inside an explicit
/// transaction; the caller MUST pair this with <see cref="ResetConnectionAsync"/> immediately before
/// the connection returns to the pool, because a session-scoped setting otherwise persists on the
/// pooled physical connection and could leak into a later, differently-tenanted lease — including one
/// taken out by an entirely different consumer sharing the same <c>NpgsqlDataSource</c> (e.g. a plain
/// <c>IDbConnectionFactory</c>-based Dapper call that does not itself call this seam).
/// </description>
/// </item>
/// </list>
/// <para>
/// <strong>Defense in depth, not the sole enforcement mechanism.</strong> The actual tenant isolation
/// guarantee comes from a database-level row-level security policy (see
/// <c>SharedKernel.Persistence.EfCore</c>'s RLS migration helper) that reads the same session
/// settings this interface writes. Calling any member here without a matching RLS policy on the
/// target table binds a session variable that nothing enforces — always pair the two.
/// </para>
/// <para>
/// <strong>Cross-tenant escape clause:</strong> every binding member also carries
/// <c>crossTenantActive</c> — whether an <see cref="ICrossTenantScope"/> is currently entered. A
/// matching RLS policy OR's this flag into its <c>USING</c>/<c>WITH CHECK</c> expression
/// (<c>current_setting('app.cross_tenant', true) = 'on'</c>), so a caller inside an active scope sees
/// and can write every tenant's rows regardless of which — if any — tenant is separately bound; a
/// caller outside the scope is bound exactly as before. The flag is written unconditionally (never
/// "only when active") so a session that previously ran inside an active scope has the escape clause
/// explicitly turned back <c>'off'</c> rather than left stale.
/// </para>
/// </remarks>
public interface ITenantSessionBinder
{
    /// <summary>
    /// Binds <paramref name="tenantId"/> and <paramref name="crossTenantActive"/> to the current
    /// database session for the duration of <paramref name="transaction"/>.
    /// </summary>
    /// <param name="connection">The open connection the binding statements are issued on.</param>
    /// <param name="transaction">
    /// The active transaction the binding is scoped to. The setting is expected to reset
    /// automatically when this transaction ends.
    /// </param>
    /// <param name="tenantId">
    /// The tenant identity to bind, or <see langword="null"/> when no tenant is known for this call
    /// (e.g. an admin call running purely under an active cross-tenant scope).
    /// </param>
    /// <param name="crossTenantActive">
    /// Whether an <see cref="ICrossTenantScope"/> is currently entered for the calling code path.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task BindAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid? tenantId,
        bool crossTenantActive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Binds <paramref name="tenantId"/> and <paramref name="crossTenantActive"/> to the current
    /// database session for the lifetime of this connection lease — i.e. until
    /// <see cref="ResetConnectionAsync"/> is called or the physical connection is discarded (never
    /// returned to the pool while still bound).
    /// </summary>
    /// <param name="connection">The open connection to bind.</param>
    /// <param name="tenantId">
    /// The tenant identity to bind, or <see langword="null"/> when no tenant is known for this
    /// connection lease.
    /// </param>
    /// <param name="crossTenantActive">
    /// Whether an <see cref="ICrossTenantScope"/> is currently entered for the calling code path.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The caller MUST call <see cref="ResetConnectionAsync"/> on the same connection before it is
    /// closed/returned to the pool — see the interface remarks.
    /// </remarks>
    Task BindConnectionAsync(
        DbConnection connection,
        Guid? tenantId,
        bool crossTenantActive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears whatever <see cref="BindConnectionAsync"/> bound on this connection, so a pooled
    /// physical connection never carries a stale tenant/cross-tenant binding into its next lease.
    /// </summary>
    /// <param name="connection">The connection to clear.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ResetConnectionAsync(
        DbConnection connection,
        CancellationToken cancellationToken = default);
}
