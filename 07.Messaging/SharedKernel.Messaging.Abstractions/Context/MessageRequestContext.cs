using SharedKernel.Execution.Context;

namespace SharedKernel.Messaging.Abstractions.Context;

/// <summary>
/// The publishing caller's identity, reconstructed on the consumer from the transport headers the
/// message arrived with, so a consumer can write tenant-scoped data and attribute it to whoever
/// caused the message.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this exists.</strong> A consumer runs on a background thread with no HTTP request,
/// so the service's usual <c>IRequestContext</c> resolves to <c>AnonymousRequestContext</c>: no
/// tenant. <c>06.Persistence</c> fails closed on a null tenant, so every tenant-scoped write from
/// a consumer is rejected and every tenant-scoped read returns nothing. Carrying the caller's
/// identity on the message and rebuilding it here is what makes a consumer a first-class writer.
/// </para>
/// <para>
/// <strong>This is attribution, not authorization.</strong> <see cref="HasPermissionAsync"/> always
/// answers <see langword="false"/>, whatever the message said. Headers are attacker-controllable
/// by anyone who can reach the broker, so a permission carried on one would be a permission
/// granted by the wire. A consumer that must make an authorization decision re-resolves the
/// caller's permissions from the identity provider using <see cref="UserId"/>; it never trusts the
/// message. The same reasoning is why <see cref="IsAuthenticated"/> reports what the publisher
/// claimed rather than anything this process verified.
/// </para>
/// <para>
/// <strong>Trust boundary.</strong> Treat these values the way you would treat any inbound header:
/// they are as trustworthy as the broker's own access control. On a broker every service in the
/// deployment can publish to, a compromised service can forge any tenant. Where that matters, sign
/// or encrypt the payload (<c>MessagingBusBuilder.WithPayloadTransform()</c>) and derive tenancy
/// from the signed content instead.
/// </para>
/// </remarks>
public sealed class MessageRequestContext : IRequestContext
{
    /// <summary>
    /// Initialises a context from the values a message carried.
    /// </summary>
    /// <param name="tenantId">
    /// The tenant the message was published for, or <see langword="null"/> when it carried none —
    /// in which case tenant-scoped persistence continues to fail closed, exactly as before.
    /// </param>
    /// <param name="userId">The publishing caller's subject id, or <see langword="null"/>.</param>
    /// <param name="actorKind">
    /// The kind of actor that published. Defaults to <see cref="Execution.Context.ActorKind.Anonymous"/> so an
    /// absent or unparsable header degrades to the least-privileged answer rather than to
    /// <see cref="Execution.Context.ActorKind.User"/>.
    /// </param>
    /// <param name="clientId">The OAuth2 client id the publisher authenticated through, if known.</param>
    public MessageRequestContext(
        Guid? tenantId,
        string? userId = null,
        ActorKind actorKind = Execution.Context.ActorKind.Anonymous,
        string? clientId = null)
    {
        TenantId = tenantId;
        UserId = userId;
        ActorKind = actorKind;
        ClientId = clientId;
    }

    /// <summary>
    /// Gets a value indicating whether the publishing caller claimed to be authenticated —
    /// <see langword="true"/> when the message carried a subject id.
    /// </summary>
    /// <remarks>
    /// Reports what the publisher asserted. Nothing in this process verified it; see the type-level
    /// remarks on the trust boundary.
    /// </remarks>
    public bool IsAuthenticated => UserId is not null;

    /// <summary>Gets the publishing caller's subject id, or <see langword="null"/>.</summary>
    /// <remarks>
    /// <c>06.Persistence</c> writes this to <c>CreatedBy</c>/<c>ModifiedBy</c>, so a row written by
    /// a consumer is attributed to the human or service that caused it rather than to the consuming
    /// service's own name.
    /// </remarks>
    public string? UserId { get; }

    /// <summary>Gets the tenant the message was published for, or <see langword="null"/>.</summary>
    public Guid? TenantId { get; }

    /// <summary>Gets the kind of actor that published the message.</summary>
    public ActorKind ActorKind { get; }

    /// <summary>Gets the OAuth2 client id the publishing caller authenticated through, if known.</summary>
    public string? ClientId { get; }

    /// <summary>
    /// Always returns <see langword="false"/>: permissions never travel on a message.
    /// </summary>
    /// <param name="permission">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>A completed task whose result is <see langword="false"/>.</returns>
    /// <remarks>
    /// Deliberately not "throw": a consumer pipeline that happens to ask should be denied, not
    /// crashed. See the type-level remarks for why granting from a header is not an option.
    /// </remarks>
    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken)
        => ValueTask.FromResult(false);
}
