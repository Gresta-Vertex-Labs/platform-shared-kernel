using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Meilisearch.Tenancy;

/// <summary>
/// The Meilisearch-exclusive tenant search token issuer — declared here, not in
/// <c>SharedKernel.Search.Abstractions</c>, and registered only when the composition root calls
/// <c>.WithTenantTokens()</c>.
/// </summary>
/// <remarks>
/// <para>
/// Meilisearch tenant tokens are HS256 JWTs carrying <c>searchRules</c> that the engine enforces below
/// the application — the strongest tenant-isolation primitive either engine offers. ElasticSearch's
/// equivalent, document-level security, is a commercial-tier feature; the OSS substitute is a filtered
/// alias plus role privileges denying direct access to the concrete index name, which is deployment
/// configuration, not a runtime API. A neutral "issue a scoped token" contract whose ElasticSearch
/// adapter returned a locally-signed token that nothing enforces would be actively dangerous —
/// identical at the type level, opposite in effect.
/// </para>
/// <para>
/// The rule dictionary is closed on purpose: the implementation constructs the underlying
/// <c>searchRules</c> itself from <see cref="TenantScope"/> and the tenant field name — callers can
/// never hand-write it, because the SDK's rule constructor takes an untyped dictionary where a mistake
/// is a silent cross-tenant leak.
/// </para>
/// </remarks>
public interface ITenantSearchTokenIssuer
{
    /// <summary>
    /// Issues a tenant search token scoped to <paramref name="tenantScope"/> across
    /// <paramref name="indexNames"/>, valid for <paramref name="ttl"/>.
    /// </summary>
    Task<Result<TenantSearchToken>> IssueAsync(
        TenantScope tenantScope,
        string tenantField,
        IReadOnlyCollection<string> indexNames,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);
}
