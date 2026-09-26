namespace SharedKernel.Primitives.Propagation;

/// <summary>
/// The HTTP and gRPC-metadata header names carrying cross-service propagation identifiers.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule:</b> any package reading or writing one of these headers references the constant.
/// Never retype the literal, even once — these are wire-format identifiers shared by packages that
/// cannot reference each other, so a typo in one of them is not a compile error, it is a
/// correlation id that silently stops correlating.
/// </para>
/// <para>
/// That is not hypothetical. Before this registry, <c>"X-Tenant-Id"</c> was independently declared
/// in <c>11.Communication.Rest</c>, <c>11.Communication.Grpc</c>, and
/// <c>13.ServiceDefaults.MultiTenancy</c> — three sources of truth for one wire contract, with
/// nothing to catch drift between them. <c>00.Governance</c>'s <c>SK0022</c> analyzer now flags a
/// raw literal at a header call site, and accepts a reference to any named constant, so a
/// domain-local constants class stays valid where the value is genuinely domain-local.
/// </para>
/// <para>
/// <b>Changing a value here is a breaking cross-domain change,</b> not a routine edit: every
/// service already deployed is reading the old name. It needs a work order in each consuming
/// domain and a rollout where both names are accepted.
/// </para>
/// <para>
/// <b>Pick the right registry for the call-site shape.</b> Header names live here; OTel baggage
/// keys in <see cref="WellKnownBaggageKeys"/>; span attribute keys in
/// <see cref="WellKnownTagKeys"/>. They are not interchangeable, and two of them holding the same
/// literal for the same concept does not make them so.
/// </para>
/// <para>
/// <b>Why <c>01.Core</c> and not <c>04.Contracts</c>.</b> <c>04.Contracts</c> looks like the
/// natural home for a wire contract, but <c>SharedKernel.Communication.Grpc</c> carries a
/// mechanically-enforced rule (<c>GrpcNeverReferencesContracts</c>) forbidding a
/// <c>04.Contracts</c> reference — protobuf messages are its wire contract. Routing these
/// constants through <c>04.Contracts</c> would reintroduce exactly the coupling that rule removes.
/// <c>01.Core</c> is the only layer every consumer already references unconditionally.
/// </para>
/// </remarks>
public static class WellKnownHeaders
{
    /// <summary>
    /// Header carrying the correlation id that ties one logical operation together across service
    /// boundaries (<c>"X-Correlation-Id"</c>).
    /// </summary>
    public const string CorrelationId = "X-Correlation-Id";

    /// <summary>
    /// Header carrying the tenant id used to scope a request in a multi-tenant deployment
    /// (<c>"X-Tenant-Id"</c>).
    /// </summary>
    /// <remarks>
    /// <b>An inbound value of this header is caller-supplied and unsigned.</b> It must never
    /// outrank a tenant identity taken from a signature-verified JWT claim. Shipping the opposite
    /// precedence was a real cross-tenant impersonation vector on this platform;
    /// <c>13.ServiceDefaults</c>' tenant resolution now orders its strategies
    /// <c>[Claim, Header, Database]</c> for that reason.
    /// </remarks>
    public const string TenantId = "X-Tenant-Id";

    /// <summary>
    /// Header carrying the client-generated key that makes a retried request safe to repeat
    /// (<c>"Idempotency-Key"</c>, the name the IETF <c>httpapi-idempotency-key-header</c> draft defines).
    /// </summary>
    /// <remarks>
    /// Outbound clients send it under this name and inbound endpoints read it under this name. The two
    /// sides once disagreed (<c>x-idempotency-key</c> out, <c>Idempotency-Key</c> in), so a retried call
    /// arrived with no key at all and was processed twice.
    /// </remarks>
    public const string IdempotencyKey = "Idempotency-Key";

    /// <summary>
    /// Header carrying the subject id of the caller on whose behalf a call or message is made
    /// (<c>"x-sk-actor-id"</c>). Omitted when the caller is unauthenticated.
    /// </summary>
    /// <remarks>
    /// <b>Attribution, never authentication.</b> A receiver records it (audit columns, logs) but never grants
    /// anything on the strength of it: any caller able to reach the receiver can set it. The <c>x-sk-</c> prefix
    /// is what messaging consumers lift into their log scope, so the value keeps the name messages already
    /// carry.
    /// </remarks>
    public const string ActorId = "x-sk-actor-id";

    /// <summary>
    /// Header carrying the caller's <c>ActorKind</c> as the enum member name — <c>User</c>, <c>Service</c>,
    /// <c>System</c> or <c>Anonymous</c> (<c>"x-sk-actor-kind"</c>).
    /// </summary>
    /// <remarks>
    /// The name, not the number, so a value in flight is readable without the enum at hand and inserting a
    /// member can never reinterpret it. Attribution only, like <see cref="ActorId"/>.
    /// </remarks>
    public const string ActorKind = "x-sk-actor-kind";

    /// <summary>
    /// Header carrying the OAuth2 client id the caller authenticated through (<c>"x-sk-client-id"</c>),
    /// omitted when unknown. Attribution only, like <see cref="ActorId"/>.
    /// </summary>
    public const string ClientId = "x-sk-client-id";
}
