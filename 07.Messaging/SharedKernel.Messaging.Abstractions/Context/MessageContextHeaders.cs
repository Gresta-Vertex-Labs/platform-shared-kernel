namespace SharedKernel.Messaging.Abstractions.Context;

/// <summary>
/// The transport header names that carry the publishing caller's identity to the consumer.
/// </summary>
/// <remarks>
/// <para>
/// These names are a wire contract between a publisher and a consumer that may be different
/// services, on different deploy schedules, written by different teams. A retyped literal on
/// either side silently drops the identity rather than failing, so both sides always name the
/// constant (the root brain's magic-string rule, enforced by <c>SK0022</c>).
/// </para>
/// <para>
/// <strong>Tenant is deliberately absent from this class.</strong> It already has a cross-domain
/// name — <c>01.Core</c>'s <see cref="SharedKernel.Primitives.Propagation.WellKnownHeaders.TenantId"/>
/// — that <c>11.Communication</c>, <c>13.ServiceDefaults</c> and <c>14.Presentation</c> also
/// propagate under. Declaring a second messaging-local tenant header would fork that contract,
/// which is exactly the defect the registry exists to prevent.
/// </para>
/// <para>
/// The actor names are domain-local because no other domain propagates an actor over the wire
/// today. If a second one ever does, they move to <c>WellKnownHeaders</c> rather than being
/// copied.
/// </para>
/// <para>
/// The <c>x-sk-</c> prefix is not decorative: <c>ConsumerBase&lt;TMessage&gt;</c> lifts every
/// header under it into the structured log scope, so the actor travels into a consumer's logs
/// with no per-consumer code.
/// </para>
/// </remarks>
public static class MessageContextHeaders
{
    /// <summary>
    /// The subject identifier of the caller that published the message
    /// (<c>IRequestContext.UserId</c>), omitted entirely when the publisher was unauthenticated.
    /// </summary>
    public const string ActorId = "x-sk-actor-id";

    /// <summary>
    /// The publishing caller's <c>ActorKind</c>, written as the enum member name
    /// (<c>User</c>, <c>Service</c>, <c>System</c>, <c>Anonymous</c>).
    /// </summary>
    /// <remarks>
    /// The <em>name</em> rather than the numeric value, so a message in a dead-letter queue is
    /// readable without the enum definition at hand, and so inserting a member into
    /// <c>ActorKind</c> can never silently reinterpret messages already in flight.
    /// </remarks>
    public const string ActorKind = "x-sk-actor-kind";

    /// <summary>
    /// The OAuth2 client id the publishing caller authenticated through
    /// (<c>IRequestContext.ClientId</c>), omitted when unknown.
    /// </summary>
    public const string ClientId = "x-sk-client-id";
}
