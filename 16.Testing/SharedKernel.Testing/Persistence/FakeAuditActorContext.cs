using SharedKernel.Persistence.Abstractions.Auditing;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// In-memory fake implementation of <see cref="IAuditActorContext"/> for use in unit tests.
/// </summary>
/// <remarks>
/// Defaults to a fixed, non-empty test <see cref="Guid"/> for <see cref="TenantId"/> — the identical
/// literal <c>Security/FakeTenantProvider</c> defaults to, so a test composing both fakes gets
/// matching tenant identity for free — and a fixed non-blank <see cref="ActorId"/>, mirroring
/// <c>Security/FakeUserContext</c>'s "authenticated/non-empty by default" convention: most test
/// setups need zero configuration.
/// </remarks>
public sealed class FakeAuditActorContext : IAuditActorContext
{
    private static readonly Guid DefaultTenantId = new("22222222-2222-2222-2222-222222222222");

    /// <summary>
    /// Initialises a new <see cref="FakeAuditActorContext"/>.
    /// </summary>
    /// <param name="actorId">The actor identifier to use. Defaults to <c>"test-actor"</c> when omitted.</param>
    /// <param name="tenantId">
    /// The tenant id to use. Defaults to a fixed, non-empty test <see cref="Guid"/> when omitted —
    /// never <see cref="Guid.Empty"/>.
    /// </param>
    public FakeAuditActorContext(string? actorId = null, Guid? tenantId = null)
    {
        ActorId = actorId ?? "test-actor";
        TenantId = tenantId ?? DefaultTenantId;
    }

    /// <summary>Gets or sets the current actor identifier.</summary>
    public string ActorId { get; set; }

    /// <summary>Gets or sets the current tenant identifier.</summary>
    public Guid TenantId { get; set; }
}
