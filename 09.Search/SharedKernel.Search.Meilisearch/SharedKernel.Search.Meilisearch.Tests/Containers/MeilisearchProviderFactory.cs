using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Index;
using SharedKernel.Search.Meilisearch.Options;
using SharedKernel.Search.Meilisearch.Provisioning;
using SharedKernel.Search.Meilisearch.Tenancy;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Search.Meilisearch.Tests.Containers;

/// <summary>
/// Builds real, non-mocked Meilisearch provider objects wired directly against a running
/// <see cref="MeilisearchContainerFixture"/> — the construction shape
/// <c>AddSharedKernelMeilisearchSearch</c> itself uses, minus the DI container, so behavioral/round-trip
/// tests exercise the exact same <see cref="global::Meilisearch.MeilisearchClient"/> configuration a
/// consuming host would. Mirrors <c>08.Storage</c>'s <c>MinioProviderFactory</c>.
/// </summary>
internal static class MeilisearchProviderFactory
{
    /// <summary>Creates a <see cref="MeilisearchOptions"/> instance pointed at <paramref name="fixture"/>.</summary>
    public static MeilisearchOptions CreateOptions(MeilisearchContainerFixture fixture) => new()
    {
        Url = fixture.Url,
        ApiKey = fixture.ApiKey,
    };

    /// <summary>Creates a real <see cref="global::Meilisearch.MeilisearchClient"/> against <paramref name="fixture"/>'s running container.</summary>
    public static global::Meilisearch.MeilisearchClient CreateClient(MeilisearchContainerFixture fixture) =>
        new(new HttpClient { BaseAddress = new Uri(fixture.Url) }, fixture.ApiKey);

    /// <summary>Creates a real <see cref="global::Meilisearch.MeilisearchClient"/> authenticated with a specific API key.</summary>
    public static global::Meilisearch.MeilisearchClient CreateClient(MeilisearchContainerFixture fixture, string apiKey) =>
        new(new HttpClient { BaseAddress = new Uri(fixture.Url) }, apiKey);

    /// <summary>Creates a <see cref="MeilisearchIndex{TDocument}"/> backed by a fresh client against <paramref name="fixture"/>.</summary>
    public static MeilisearchIndex<TDocument> CreateIndex<TDocument>(
        MeilisearchContainerFixture fixture, SearchIndexDefinition definition, ILogger<MeilisearchIndex<TDocument>>? logger = null)
        where TDocument : class, ISearchDocument =>
        new(CreateClient(fixture), definition, CreateOptions(fixture), new FakeClock(), logger ?? NullLogger<MeilisearchIndex<TDocument>>.Instance);

    /// <summary>Creates a <see cref="MeilisearchIndex{TDocument}"/> from an already-built client/options pair.</summary>
    public static MeilisearchIndex<TDocument> CreateIndex<TDocument>(
        global::Meilisearch.MeilisearchClient client,
        SearchIndexDefinition definition,
        MeilisearchOptions options,
        ILogger<MeilisearchIndex<TDocument>>? logger = null)
        where TDocument : class, ISearchDocument =>
        new(client, definition, options, new FakeClock(), logger ?? NullLogger<MeilisearchIndex<TDocument>>.Instance);

    /// <summary>Creates a <see cref="MeilisearchIndexProvisioner"/> backed by a fresh client against <paramref name="fixture"/>.</summary>
    public static MeilisearchIndexProvisioner CreateProvisioner(
        MeilisearchContainerFixture fixture,
        ILogger<MeilisearchIndexProvisioner>? logger = null,
        IReadOnlyDictionary<string, SearchIndexDefinition>? registeredDefinitions = null) =>
        new(
            CreateClient(fixture),
            CreateOptions(fixture),
            registeredDefinitions ?? EmptyDefinitions,
            EmptyRankingRules,
            logger ?? NullLogger<MeilisearchIndexProvisioner>.Instance);

    /// <summary>Creates a <see cref="MeilisearchIndexProvisioner"/> authenticated with a specific (e.g. mis-scoped) API key.</summary>
    public static MeilisearchIndexProvisioner CreateProvisioner(
        MeilisearchContainerFixture fixture, string apiKey, ILogger<MeilisearchIndexProvisioner>? logger = null)
    {
        var options = CreateOptions(fixture);
        options.ApiKey = apiKey;
        return new MeilisearchIndexProvisioner(
            CreateClient(fixture, apiKey),
            options,
            EmptyDefinitions,
            EmptyRankingRules,
            logger ?? NullLogger<MeilisearchIndexProvisioner>.Instance);
    }

    /// <summary>Creates a <see cref="MeilisearchIndexProvisioner"/> from an already-built client/options pair.</summary>
    public static MeilisearchIndexProvisioner CreateProvisioner(
        global::Meilisearch.MeilisearchClient client,
        MeilisearchOptions options,
        ILogger<MeilisearchIndexProvisioner>? logger = null,
        IReadOnlyDictionary<string, SearchIndexDefinition>? registeredDefinitions = null) =>
        new(
            client,
            options,
            registeredDefinitions ?? EmptyDefinitions,
            EmptyRankingRules,
            logger ?? NullLogger<MeilisearchIndexProvisioner>.Instance);

    /// <summary>
    /// Creates a provisioner that will apply <paramref name="rankingRules"/> to
    /// <paramref name="definition"/>, mirroring what <c>MeilisearchSearchBuilder.WithRankingRules</c>
    /// wires at a real composition root.
    /// </summary>
    public static MeilisearchIndexProvisioner CreateProvisionerWithRankingRules(
        MeilisearchContainerFixture fixture,
        SearchIndexDefinition definition,
        IReadOnlyList<string> rankingRules) =>
        new(
            CreateClient(fixture),
            CreateOptions(fixture),
            new Dictionary<string, SearchIndexDefinition>(StringComparer.Ordinal) { [definition.Name] = definition },
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [definition.Name] = rankingRules },
            NullLogger<MeilisearchIndexProvisioner>.Instance);

    private static readonly IReadOnlyDictionary<string, SearchIndexDefinition> EmptyDefinitions =
        new Dictionary<string, SearchIndexDefinition>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyRankingRules =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

    /// <summary>Creates a <see cref="MeilisearchTenantTokenIssuer"/> backed by a fresh client against <paramref name="fixture"/>.</summary>
    /// <remarks>
    /// Uses <see cref="SystemClock"/>, never <see cref="FakeClock"/> — VERIFIED (2026-07-20):
    /// <c>IssueAsync</c> computes the token's expiry as <c>clock.UtcNow.Add(ttl)</c> and passes it to
    /// the real SDK's <c>GenerateTenantToken</c>, which validates the resulting <c>DateTime</c> against
    /// REAL wall-clock time ("Provide a valid UTC DateTime in the future"). <see cref="FakeClock"/>'s
    /// default fixed baseline (2024-01-01) is in the past relative to real time, so every issuance
    /// failed with <c>TenantTokenIssuanceFailed</c> until this was corrected — unlike
    /// <see cref="CreateIndex{TDocument}"/>'s clock, which only feeds a purely-local, unvalidated
    /// <c>SearchWriteReceipt.AcceptedAt</c> timestamp and is unaffected by this class of bug.
    /// </remarks>
    public static MeilisearchTenantTokenIssuer CreateTenantTokenIssuer(
        MeilisearchContainerFixture fixture, string apiKeyUid, ILogger<MeilisearchTenantTokenIssuer>? logger = null)
    {
        var options = CreateOptions(fixture);
        options.ApiKeyUid = apiKeyUid;
        return new MeilisearchTenantTokenIssuer(
            CreateClient(fixture), options, new SystemClock(), logger ?? NullLogger<MeilisearchTenantTokenIssuer>.Instance);
    }

    /// <summary>
    /// Creates a <see cref="MeilisearchTenantTokenIssuer"/> signing with a SPECIFIC (<paramref name="apiKey"/>,
    /// <paramref name="apiKeyUid"/>) pair — for a dedicated, purpose-scoped signing key rather than the
    /// fixture's own master key. <c>GenerateTenantToken</c> is a purely local HMAC-signing operation, but
    /// the server-side verification of the resulting token requires the UID and the secret to name the
    /// SAME real, persisted key — the fixture's own master key is Meilisearch's bootstrap secret, not a
    /// persisted <c>Key</c> entity, and is not resolvable via the Keys API (<c>GET /keys/{key}</c> 404s
    /// for it — VERIFIED against the real v1.20.0 engine).
    /// </summary>
    public static MeilisearchTenantTokenIssuer CreateTenantTokenIssuer(
        MeilisearchContainerFixture fixture, string apiKey, string apiKeyUid, ILogger<MeilisearchTenantTokenIssuer>? logger = null)
    {
        var options = CreateOptions(fixture);
        options.ApiKey = apiKey;
        options.ApiKeyUid = apiKeyUid;
        return new MeilisearchTenantTokenIssuer(
            CreateClient(fixture, apiKey), options, new SystemClock(), logger ?? NullLogger<MeilisearchTenantTokenIssuer>.Instance);
    }
}
