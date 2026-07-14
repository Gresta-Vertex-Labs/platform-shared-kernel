namespace SharedKernel.Primitives.Propagation;

/// <summary>
/// Compile-time constant registry of HTTP/gRPC-metadata header names carrying cross-service
/// propagation identifiers — the single authoritative source for these literals platform-wide.
/// </summary>
/// <remarks>
/// <para>
/// Every domain that needs to read or write the correlation-id or tenant-id header must reference
/// these constants instead of redeclaring the literal locally. Before this registry existed, the
/// tenant-header literal (<c>"X-Tenant-Id"</c>) was independently redeclared in
/// <c>11.Communication.Rest.TenantIdDelegatingHandler</c>, <c>11.Communication.Grpc.TenantIdInterceptor</c>,
/// and <c>13.ServiceDefaults.MultiTenancy.HeaderTenantResolutionStrategy</c> — three parallel sources of
/// truth for the same wire-format identifier, with no mechanism to catch drift between them.
/// </para>
/// <para>
/// <c>04.Contracts</c> was considered and rejected as the home for this registry:
/// <c>SharedKernel.Communication.Grpc</c> carries a hard governance rule (P-163,
/// <c>GrpcNeverReferencesContracts</c>) forbidding a <c>04.Contracts</c> reference, so routing these
/// constants through <c>04.Contracts</c> would reintroduce exactly the coupling that rule removed.
/// <c>01.Core</c> is the only architecturally legal home — every consumer already references it
/// unconditionally.
/// </para>
/// <para>
/// This registry is a pure promotion of today's de facto platform standard — changing a value here is
/// a breaking, cross-domain change requiring explicit work orders in every consuming domain, never a
/// routine edit.
/// </para>
/// </remarks>
public static class WellKnownHeaders
{
    /// <summary>
    /// The HTTP/gRPC-metadata header name carrying the correlation id used to tie a single logical
    /// operation together across service boundaries (<c>"X-Correlation-Id"</c>).
    /// </summary>
    public const string CorrelationId = "X-Correlation-Id";

    /// <summary>
    /// The HTTP/gRPC-metadata header name carrying the tenant id used for multi-tenant request
    /// scoping across service boundaries (<c>"X-Tenant-Id"</c>).
    /// </summary>
    /// <remarks>
    /// Replaces the independently-redeclared literal in
    /// <c>11.Communication.Rest.TenantIdDelegatingHandler</c>,
    /// <c>11.Communication.Grpc.TenantIdInterceptor</c>, and
    /// <c>13.ServiceDefaults.MultiTenancy.HeaderTenantResolutionStrategy</c>.
    /// </remarks>
    public const string TenantId = "X-Tenant-Id";
}
