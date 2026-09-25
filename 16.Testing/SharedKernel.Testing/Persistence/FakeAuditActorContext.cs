using SharedKernel.Execution.Context;
using SharedKernel.Persistence.Testing;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// The persistence-test flavor of <see cref="TestRequestContext"/> (<c>SharedKernel.Persistence.Testing</c>): an
/// authenticated actor with a fixed id and a fixed, non-empty tenant — so audit columns, tenant filters and the audit
/// trail all have something to attribute to with zero configuration.
/// </summary>
public sealed class FakeAuditActorContext : TestRequestContext
{
    private static readonly Guid DefaultTenantId = new("22222222-2222-2222-2222-222222222222");

    /// <summary>Initialises a new <see cref="FakeAuditActorContext"/>.</summary>
    /// <param name="actorId">The actor identifier to use. Defaults to <c>"test-actor"</c> when omitted.</param>
    /// <param name="tenantId">
    /// The tenant id to use. Defaults to a fixed, non-empty test <see cref="Guid"/> when omitted. Set
    /// <see cref="TestRequestContext.TenantId"/> to <see langword="null"/> after construction to simulate "no tenant resolved".
    /// </param>
    /// <param name="actorKind">The actor kind to use. Defaults to <see cref="ActorKind.User"/>.</param>
    public FakeAuditActorContext(string? actorId = null, Guid? tenantId = null, ActorKind actorKind = ActorKind.User)
    {
        UserId = actorId ?? "test-actor";
        TenantId = tenantId ?? DefaultTenantId;
        ActorKind = actorKind;
    }

    /// <summary>Gets or sets the current actor identifier, reported as <see cref="TestRequestContext.UserId"/>.</summary>
    public string ActorId
    {
        get => UserId ?? string.Empty;
        set => UserId = value;
    }
}
