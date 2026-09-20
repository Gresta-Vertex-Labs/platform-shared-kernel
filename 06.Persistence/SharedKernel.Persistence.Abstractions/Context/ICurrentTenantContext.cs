namespace SharedKernel.Persistence.Abstractions.Context;

/// <summary>
/// Resolves the current tenant identity for persistence-layer isolation (the tenant global query
/// filter and the tenant write guard).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Fail-closed by construction:</strong> <see cref="TenantId"/> is
/// <see cref="Nullable{T}"/> — <see langword="null"/> is the explicit, unambiguous "no tenant could
/// be resolved" state. This replaces the former <c>IAuditActorContext.TenantId</c> (a non-nullable
/// <see cref="Guid"/> that overloaded <see cref="Guid.Empty"/> as its own sentinel for "no tenant"),
/// which could not be distinguished from a genuine, deliberately-assigned all-zero tenant id and
/// relied on every caller remembering the convention. A tenant filter built from
/// <see langword="null"/> still returns zero rows — the safety property is unchanged, only made
/// explicit in the type.
/// </para>
/// <para>
/// Consumed by <c>TenantedDbContext</c>'s global tenant query filter and the tenant write guard
/// interceptor. Only registered/required when a service opts in to multi-tenancy
/// (<c>EfCorePersistenceBuilder.WithMultiTenancy()</c>) — a single-tenant service extending
/// <c>SharedKernelDbContext</c> directly never resolves this seam.
/// </para>
/// </remarks>
public interface ICurrentTenantContext
{
    /// <summary>
    /// Gets the current tenant identifier, or <see langword="null"/> when no tenant could be
    /// resolved for the current logical call. <see langword="null"/> causes the tenant global query
    /// filter to match zero rows and the tenant write guard to reject every tenant-scoped write.
    /// </summary>
    Guid? TenantId { get; }
}
