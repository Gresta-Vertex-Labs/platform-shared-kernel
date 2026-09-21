using System.Data.Common;

namespace SharedKernel.Persistence.Abstractions.Context;

/// <summary>
/// Binds the current tenant to an open database transaction, so a server-side row-level security
/// (RLS) policy can enforce tenant isolation independently of application-level query filters.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Transaction-local only.</strong> The binding lives exactly as long as the transaction passed
/// to <see cref="BindAsync"/>: PostgreSQL discards it at commit or rollback. There is deliberately no
/// session-scoped variant — a session-scoped setting survives on the physical connection and, behind a
/// transaction-mode pooler such as PgBouncer, leaks into another client's transaction.
/// </para>
/// <para>
/// <strong>Defense in depth.</strong> A binding without a matching RLS policy on the table enforces
/// nothing, and RLS itself guards against application bugs (a forgotten tenant filter), not against SQL
/// injection by a caller able to run arbitrary SQL as the application role.
/// </para>
/// </remarks>
public interface ITenantSessionBinder
{
    /// <summary>
    /// Binds <paramref name="tenantId"/> to <paramref name="transaction"/> in one statement.
    /// </summary>
    /// <param name="connection">The open connection <paramref name="transaction"/> belongs to.</param>
    /// <param name="transaction">The transaction the binding is scoped to.</param>
    /// <param name="tenantId">
    /// The tenant to bind, or <see langword="null"/> to bind "no tenant", which a tenant policy treats as
    /// matching no rows.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task BindAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid? tenantId,
        CancellationToken cancellationToken = default);
}
