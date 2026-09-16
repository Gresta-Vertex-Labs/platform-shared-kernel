namespace SharedKernel.Persistence.Abstractions.Auditing;

/// <summary>
/// Resolves the current actor and tenant identity for <see cref="IAuditTrailWriter"/>.
/// </summary>
/// <remarks>
/// <para>
/// WO-071/P-456/D-116. A package-local seam, mirroring <c>05.Application</c>'s
/// <c>IRequestContext</c> bridge pattern in SHAPE — even though the two packages differ in WHY
/// they need one: <c>05.Application</c> has zero <c>SharedKernel.Security.Abstractions</c> access of
/// any kind, whereas <c>SharedKernel.Persistence.EfCore</c> already holds an APPROVED, narrowly-scoped
/// exception referencing <c>SharedKernel.Security.Abstractions</c> directly (P-078/WO-014) — but that
/// grant is scoped to <c>SharedKernel.Persistence.EfCore</c> specifically, NOT to
/// <c>SharedKernel.Persistence.Abstractions</c>, where this interface lives. Extending the EfCore-only
/// exception to Abstractions would be an unjustified, unrecorded widening, hence this fresh local seam.
/// </para>
/// <para>
/// The default EF Core implementation (<c>EfCoreAuditActorContext</c>,
/// <c>SharedKernel.Persistence.EfCore</c>) bridges the already-approved <c>IUserContext</c>/
/// <c>ITenantProvider</c>. A consuming service may override this default with its own
/// <see cref="IAuditActorContext"/> registration (last-registration-wins).
/// </para>
/// </remarks>
public interface IAuditActorContext
{
    /// <summary>
    /// Gets the current actor identifier, in the platform's existing audit-string-format convention
    /// (see <see cref="AuditRecord.ActorId"/>'s remarks).
    /// </summary>
    string ActorId { get; }

    /// <summary>
    /// Gets the current tenant identifier. <see cref="Guid.Empty"/> mirrors <c>ITenantProvider</c>'s
    /// existing single-tenant/no-tenant sentinel convention.
    /// </summary>
    Guid TenantId { get; }
}
