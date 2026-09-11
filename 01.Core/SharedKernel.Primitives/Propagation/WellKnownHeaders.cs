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
}
