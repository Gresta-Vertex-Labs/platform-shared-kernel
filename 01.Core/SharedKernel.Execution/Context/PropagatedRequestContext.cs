using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Execution.Context;

/// <summary>
/// The caller identity a message, a workflow activity or another asynchronous hop carried with it — rebuilt on
/// the receiving side from the propagation headers (<see cref="RequestContextPropagation"/>) so the work there is
/// attributed to, and scoped to the tenant of, whoever caused it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this exists.</strong> A message consumer or a workflow activity runs with no HTTP request, so a
/// service's usual <see cref="IRequestContext"/> answers "nobody, no tenant". <c>06.Persistence</c> fails closed on
/// a null tenant, so every tenant-scoped write would be rejected and every outbound call would leave without a
/// tenant. Carrying the caller on the hop and rebuilding it here is what makes the receiver a first-class writer.
/// </para>
/// <para>
/// <strong>This is attribution, not authorization.</strong> <see cref="HasPermissionAsync"/> always answers
/// <see langword="false"/>. The values arrived as headers, which anyone able to reach the transport can set, so a
/// permission carried on one would be a permission granted by the wire. Code that must authorize re-resolves the
/// caller's permissions from the identity provider using <see cref="UserId"/>. For the same reason
/// <see cref="IsAuthenticated"/> reports what the sender claimed, not anything this process verified.
/// </para>
/// <para>
/// <strong>Trust boundary.</strong> The values are as trustworthy as the transport's own access control. On a
/// broker or workflow cluster every service can write to, a compromised service can forge any tenant; where that
/// matters, derive tenancy from signed payload content instead.
/// </para>
/// </remarks>
public sealed class PropagatedRequestContext : IRequestContext
{
    /// <summary>Initialises a context from the values a hop carried.</summary>
    /// <param name="tenantId">
    /// The tenant the work was sent for, or <see langword="null"/> when none was carried — tenant-scoped
    /// persistence then keeps failing closed.
    /// </param>
    /// <param name="userId">The sending caller's subject id, or <see langword="null"/>.</param>
    /// <param name="actorKind">
    /// The kind of actor that sent the work. Defaults to <see cref="Context.ActorKind.Anonymous"/>, so an absent or
    /// unparsable value degrades to the least-privileged answer rather than to <see cref="Context.ActorKind.User"/>.
    /// </param>
    /// <param name="clientId">The OAuth2 client id the sender authenticated through, if known.</param>
    /// <param name="correlationId">The correlation id of the operation, or <see langword="null"/>.</param>
    public PropagatedRequestContext(
        TenantId? tenantId,
        string? userId = null,
        ActorKind actorKind = Context.ActorKind.Anonymous,
        string? clientId = null,
        string? correlationId = null)
    {
        TenantId = tenantId;
        UserId = userId;
        ActorKind = actorKind;
        ClientId = clientId;
        CorrelationId = correlationId;
    }

    /// <summary>
    /// Gets a value indicating whether the sending caller claimed to be authenticated — <see langword="true"/>
    /// when a subject id was carried.
    /// </summary>
    public bool IsAuthenticated => UserId is not null;

    /// <summary>Gets the sending caller's subject id, or <see langword="null"/>.</summary>
    /// <remarks>
    /// <c>06.Persistence</c> writes this to <c>CreatedBy</c>/<c>ModifiedBy</c>, so a row written on the receiving
    /// side is attributed to the human or service that caused it rather than to the receiving service's own name.
    /// </remarks>
    public string? UserId { get; }

    /// <summary>Gets the tenant the work was sent for, or <see langword="null"/>.</summary>
    public TenantId? TenantId { get; }

    /// <summary>Gets the kind of actor that sent the work.</summary>
    public ActorKind ActorKind { get; }

    /// <summary>Gets the OAuth2 client id the sending caller authenticated through, if known.</summary>
    public string? ClientId { get; }

    /// <summary>Gets the correlation id of the operation, or <see langword="null"/>.</summary>
    public string? CorrelationId { get; }

    /// <summary>Always returns <see langword="false"/>: permissions never travel with a hop.</summary>
    /// <param name="permission">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>A completed task whose result is <see langword="false"/>.</returns>
    /// <remarks>
    /// Deliberately not "throw": a pipeline that happens to ask should be denied, not crashed.
    /// </remarks>
    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken)
        => ValueTask.FromResult(false);
}
