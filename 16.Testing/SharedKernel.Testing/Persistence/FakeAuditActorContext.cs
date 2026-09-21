using SharedKernel.Application.Context;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// An <see cref="IRequestContext"/> fake with persistence-friendly defaults: an authenticated actor
/// with a fixed id and a fixed, non-empty tenant — so audit columns, tenant filters and the audit
/// trail all have something to attribute to with zero configuration.
/// </summary>
/// <remarks>
/// P-558: the persistence layer reads its caller from the shared <see cref="IRequestContext"/>
/// (<c>SharedKernel.Application.Abstractions</c>); the former <c>ICurrentActorContext</c>/
/// <c>ICurrentTenantContext</c> pair is gone. <see cref="ActorId"/> is reported as
/// <see cref="IRequestContext.UserId"/>. Use <see cref="Application.FakeRequestContext"/> instead for
/// pipeline tests that need a permission set.
/// </remarks>
public sealed class FakeAuditActorContext : IRequestContext
{
    private static readonly Guid DefaultTenantId = new("22222222-2222-2222-2222-222222222222");

    /// <summary>Initialises a new <see cref="FakeAuditActorContext"/>.</summary>
    /// <param name="actorId">The actor identifier to use. Defaults to <c>"test-actor"</c> when omitted.</param>
    /// <param name="tenantId">
    /// The tenant id to use. Defaults to a fixed, non-empty test <see cref="Guid"/> when omitted. Set
    /// <see cref="TenantId"/> to <see langword="null"/> after construction to simulate "no tenant resolved".
    /// </param>
    /// <param name="actorKind">The actor kind to use. Defaults to <see cref="ActorKind.User"/>.</param>
    public FakeAuditActorContext(string? actorId = null, Guid? tenantId = null, ActorKind actorKind = ActorKind.User)
    {
        ActorId = actorId ?? "test-actor";
        TenantId = tenantId ?? DefaultTenantId;
        ActorKind = actorKind;
    }

    /// <summary>Gets or sets the current actor identifier, reported as <see cref="UserId"/>.</summary>
    public string ActorId { get; set; }

    /// <inheritdoc />
    public ActorKind ActorKind { get; set; }

    /// <inheritdoc />
    public Guid? TenantId { get; set; }

    /// <inheritdoc />
    public string? ClientId { get; set; }

    /// <inheritdoc />
    public string? SessionId { get; set; }

    /// <inheritdoc />
    public string? ImpersonatorId { get; set; }

    /// <inheritdoc />
    public bool IsAuthenticated => true;

    /// <inheritdoc />
    public string? UserId => ActorId;

    /// <inheritdoc />
    /// <remarks>Always <see langword="false"/> — this fake grants no permissions.</remarks>
    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken) =>
        ValueTask.FromResult(false);
}
