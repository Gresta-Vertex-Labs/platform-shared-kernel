namespace SharedKernel.Persistence.Abstractions.Context;

/// <summary>
/// Resolves the current actor identity for persistence-layer attribution (audit columns, the audit
/// trail, and any future write-time actor stamping).
/// </summary>
/// <remarks>
/// <para>
/// Replaces the former combined <c>IAuditActorContext</c>, which conflated
/// actor identity and tenant identity into a single fail-open seam (a caller with no tenant resolved
/// silently reported <see cref="Guid.Empty"/>, indistinguishable from a genuine tenant whose id
/// happened to be all zeroes). Actor identity and tenant identity are orthogonal — a system job has
/// an actor but no tenant, a cross-tenant admin report has a tenant per row but one fixed actor — so
/// they are now two separate, independently-registerable seams. See
/// <see cref="ICurrentTenantContext"/> for the tenant half.
/// </para>
/// <para>
/// Consumed by <c>AuditInterceptor</c>, <c>SoftDeleteInterceptor</c>, and the audit-trail writer.
/// <c>SharedKernel.Persistence.EfCore</c> registers a default, system-attributed implementation
/// (<c>AnonymousActorContext</c>) so the platform-three interceptors always have something to
/// resolve. A consuming service bridges this seam to its real identity source (typically
/// <c>SharedKernel.Security.Abstractions</c>'s <c>IUserContext</c>) at its own composition root —
/// <c>13.ServiceDefaults/SharedKernel.ServiceDefaults.Persistence</c> ships that bridge for services
/// that already use <c>12.Security</c>.
/// </para>
/// </remarks>
public interface ICurrentActorContext
{
    /// <summary>
    /// Gets the current actor identifier, in the platform's existing audit-string-format convention
    /// (see <see cref="Auditing.AuditRecord.ActorId"/>'s remarks).
    /// </summary>
    string ActorId { get; }

    /// <summary>Gets the kind of actor <see cref="ActorId"/> identifies.</summary>
    ActorKind ActorKind { get; }
}
