using SharedKernel.Persistence.Abstractions.Context;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// In-memory fake implementing both <see cref="ICurrentActorContext"/> and
/// <see cref="ICurrentTenantContext"/> for use in unit tests.
/// </summary>
/// <remarks>
/// P-557/W2: one fake serves both seams (the platform's former combined <c>IAuditActorContext</c>
/// split into two orthogonal interfaces — see their own remarks) so a test composing this type once
/// still gets matching actor and tenant identity for free, exactly as before. Defaults to a fixed,
/// non-empty test <see cref="Guid"/> for <see cref="TenantId"/> — the identical literal
/// <c>Security/FakeTenantProvider</c> defaults to — and a fixed non-blank <see cref="ActorId"/>,
/// mirroring <c>Security/FakeUserContext</c>'s "authenticated/non-empty by default" convention: most
/// test setups need zero configuration.
/// </remarks>
public sealed class FakeAuditActorContext : ICurrentActorContext, ICurrentTenantContext
{
    private static readonly Guid DefaultTenantId = new("22222222-2222-2222-2222-222222222222");

    /// <summary>
    /// Initialises a new <see cref="FakeAuditActorContext"/>.
    /// </summary>
    /// <param name="actorId">The actor identifier to use. Defaults to <c>"test-actor"</c> when omitted.</param>
    /// <param name="tenantId">
    /// The tenant id to use. Defaults to a fixed, non-empty test <see cref="Guid"/> when omitted.
    /// Pass <see langword="null"/> explicitly (after construction, via the setter) to simulate "no
    /// tenant resolved".
    /// </param>
    /// <param name="actorKind">The actor kind to use. Defaults to <see cref="ActorKind.User"/>.</param>
    public FakeAuditActorContext(string? actorId = null, Guid? tenantId = null, ActorKind actorKind = ActorKind.User)
    {
        ActorId = actorId ?? "test-actor";
        TenantId = tenantId ?? DefaultTenantId;
        ActorKind = actorKind;
    }

    /// <summary>Gets or sets the current actor identifier.</summary>
    public string ActorId { get; set; }

    /// <summary>Gets or sets the current actor kind.</summary>
    public ActorKind ActorKind { get; set; }

    /// <summary>Gets or sets the current tenant identifier, or <see langword="null"/> for "no tenant resolved".</summary>
    public Guid? TenantId { get; set; }
}
