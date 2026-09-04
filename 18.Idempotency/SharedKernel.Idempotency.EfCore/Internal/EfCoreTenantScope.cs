namespace SharedKernel.Idempotency.EfCore.Internal;

/// <summary>
/// Resolves the mandatory <c>TenantId</c> column value for a store row from the ambient
/// <see cref="SharedKernel.Messaging.Abstractions.TenantContext.ITenantContextAccessor"/> (D-02).
/// </summary>
/// <remarks>
/// Not shared with <c>SharedKernel.Idempotency.Redis</c> — this domain deliberately has no shared
/// <c>.Core</c> package (18.Idempotency/CLAUDE.md, "Code shared by both providers: Nowhere —
/// duplicate it").
/// </remarks>
internal static class EfCoreTenantScope
{
    /// <summary>
    /// The fixed, non-caller-suppliable <c>TenantId</c> value substituted for a
    /// <see langword="null"/> ambient tenant identity (D-02). Never omitted from the mandatory
    /// <c>TenantId</c> column — a null-tenant row always carries this exact value, so it can never
    /// collide with a real tenant's <see cref="Guid"/>. Deliberately not <see cref="Guid.Empty"/>,
    /// which a misbehaving accessor implementation could plausibly return by accident.
    /// </summary>
    internal static readonly Guid NonTenantSentinel = new("00000000-0000-0000-0000-000000000001");

    /// <summary>Resolves the <c>TenantId</c> column value for the ambient <paramref name="tenantId"/>.</summary>
    public static Guid Resolve(Guid? tenantId) => tenantId ?? NonTenantSentinel;
}
