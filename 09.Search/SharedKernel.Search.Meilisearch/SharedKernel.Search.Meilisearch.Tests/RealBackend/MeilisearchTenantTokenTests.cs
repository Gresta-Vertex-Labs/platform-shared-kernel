using FluentAssertions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Tests.Containers;
using SharedKernel.Search.Meilisearch.Tests.Support;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Search.Meilisearch.Tests.RealBackend;

/// <summary>
/// T-17: real-backend tenant-token issuance against a real <see cref="MeilisearchContainerFixture"/> —
/// an engine-enforced per-tenant search token that the engine honours with no caller-supplied filter of
/// its own, the TTL-ceiling guard, and empirical proof the token scopes search only, never writes.
/// </summary>
[Collection(MeilisearchCollection.Name)]
public sealed class MeilisearchTenantTokenTests : IAsyncLifetime
{
    private const string IndexName = "products-tenant-token-tests";

    private readonly MeilisearchContainerFixture _fixture;
    private SearchIndexDefinition _definition = null!;
    private string _signingApiKey = null!;
    private string _apiKeyUid = null!;

    public MeilisearchTenantTokenTests(MeilisearchContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _definition = new SearchIndexDefinitionBuilder(IndexName).ConfigureSharedFields().Build().Value;
        var provisioner = MeilisearchProviderFactory.CreateProvisioner(_fixture);
        (await provisioner.EnsureIndexAsync(_definition)).IsSuccess.Should().BeTrue();

        var index = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        (await index.IndexManyAsync(TestProductCorpus.All, SearchWriteConsistency.Searchable)).IsSuccess.Should().BeTrue();

        // VERIFIED against the real v1.20.0 engine (2026-07-20): the fixture's own master key is
        // Meilisearch's bootstrap secret, not a persisted Key entity — GetKeyAsync(fixture.ApiKey)
        // 404s. MeilisearchTenantTokenIssuer.IssueAsync needs a (ApiKey, ApiKeyUid) pair naming the
        // SAME real, persisted key, so a dedicated, purpose-scoped key is minted here instead — the
        // same pattern a real deployment would use for issuing tenant tokens (never the master key
        // directly). CreateKeyAsync's response Key.KeyUid maps to JSON "key" (the actual secret value,
        // despite the confusing name) and Key.Uid maps to JSON "uid".
        var masterClient = MeilisearchProviderFactory.CreateClient(_fixture);
        var signingKey = await masterClient.CreateKeyAsync(new global::Meilisearch.Key
        {
            Description = "tenant-token-signing-key-for-T17",
            Actions = [global::Meilisearch.KeyAction.Search],
            Indexes = [IndexName],
            ExpiresAt = DateTime.UtcNow.AddHours(1),
        });
        _signingApiKey = signingKey.KeyUid;
        _apiKeyUid = signingKey.Uid;
    }

    public async Task DisposeAsync()
    {
        var provisioner = MeilisearchProviderFactory.CreateProvisioner(_fixture);
        await provisioner.DeleteIndexAsync(IndexName);
    }

    [Fact]
    public async Task IssueAsync_MintsTokenTheEngineEnforcesToTenantAOnly_WithNoCallerSuppliedFilter()
    {
        var issuer = MeilisearchProviderFactory.CreateTenantTokenIssuer(_fixture, _signingApiKey, _apiKeyUid);

        var issueResult = await issuer.IssueAsync(
            TenantScope.Of(TestProductCorpus.TenantA), TestProductFields.TenantId, [IndexName], TimeSpan.FromMinutes(5));

        issueResult.IsSuccess.Should().BeTrue();
        issueResult.Value.Value.Should().NotBeNullOrEmpty();
        issueResult.Value.ScopedIndexes.Should().Contain(IndexName);

        // Authenticate a SEPARATE client with the minted token and search with NO filter of its own —
        // the engine alone must restrict the result set to tenant-a.
        var tokenClient = MeilisearchProviderFactory.CreateClient(_fixture, issueResult.Value.Value);
        var searchResult = await tokenClient.Index(IndexName)
            .SearchAsync<TestProduct>(string.Empty, new global::Meilisearch.SearchQuery { Limit = 50 });

        searchResult.Hits.Should().HaveCount(TestProductCorpus.ForTenant(TestProductCorpus.TenantA).Count);
        searchResult.Hits.Should().OnlyContain(p => p.TenantId == TestProductCorpus.TenantA);
    }

    [Fact]
    public async Task IssueAsync_WithTtlAboveConfiguredMaximum_ReturnsTenantTokenTtlOutOfRange()
    {
        var issuer = MeilisearchProviderFactory.CreateTenantTokenIssuer(_fixture, _signingApiKey, _apiKeyUid);

        // Default MeilisearchOptions.TenantTokenMaxTtlMinutes is 15; 60 exceeds it.
        var issueResult = await issuer.IssueAsync(
            TenantScope.Of(TestProductCorpus.TenantA), TestProductFields.TenantId, [IndexName], TimeSpan.FromMinutes(60));

        issueResult.IsFailure.Should().BeTrue();
        issueResult.Error.Code.Should().Be("search.meilisearch.tenant_token_ttl_out_of_range");
        issueResult.Error.Type.Should().Be(ErrorType.Validation);
    }

    /// <summary>
    /// Documents and empirically proves that a Meilisearch tenant search token scopes SEARCH ONLY.
    /// Tenant tokens carry only <c>searchRules</c>, derived from a signing key that itself has no
    /// write-capable action, so a write attempt through the token-authenticated raw index handle must
    /// be rejected by the engine. Write-path tenant isolation remains the caller's
    /// <see cref="TenantScope"/> responsibility on <c>ISearchIndex&lt;TDocument&gt;</c>.
    /// </summary>
    /// <remarks>
    /// VERIFIED against the real v1.20.0 engine (2026-07-20), via a RAW (non-SDK) HTTP POST carrying the
    /// same tenant token as the <c>Authorization: Bearer</c> header: the engine rejects the write
    /// SYNCHRONOUSLY at the HTTP layer with a genuine <c>403 Forbidden</c>
    /// (<c>{"code":"invalid_api_key",...}</c>) — write-path rejection is real and immediate, not an async
    /// task-processing outcome. However, the MeiliSearch 0.20.0 C# SDK's <c>Index.AddDocumentsAsync</c>
    /// does NOT surface that 403 as an exception: it silently returns a DEFAULT-VALUED <c>TaskInfo</c>
    /// (<c>TaskUid = 0</c>, <c>Status = Enqueued</c>) instead of throwing — the SAME SDK-inconsistency
    /// class already found and worked around on <c>MeilisearchIndexProvisioner.IndexExistsAsync</c>/
    /// <c>ProbeAsync</c> and <c>MeilisearchIndex.GetAsync</c> (see those methods' own notes), where
    /// different SDK call paths inconsistently throw <c>MeilisearchApiError</c>, throw a raw
    /// <c>HttpRequestException</c>, or — this case, the worst of the three — swallow the failure into a
    /// garbage return value. Waiting on that bogus <c>TaskInfo.TaskUid</c> (0) resolves to whatever
    /// UNRELATED task genuinely holds that uid on this Meilisearch instance (typically an
    /// already-succeeded earlier task), producing a false "succeeded" reading — so this test does not
    /// trust the SDK's write-call return value or exception behavior at all. The only reliable proof is
    /// checking, via the unrestricted MASTER client, that the document was never actually written.
    /// </remarks>
    [Fact]
    public async Task IssueAsync_TokenScope_IsSearchOnly_SoAWriteAttemptIsRejectedByTheEngine()
    {
        var issuer = MeilisearchProviderFactory.CreateTenantTokenIssuer(_fixture, _signingApiKey, _apiKeyUid);
        var issueResult = await issuer.IssueAsync(
            TenantScope.Of(TestProductCorpus.TenantA), TestProductFields.TenantId, [IndexName], TimeSpan.FromMinutes(5));
        issueResult.IsSuccess.Should().BeTrue();
        var tokenClient = MeilisearchProviderFactory.CreateClient(_fixture, issueResult.Value.Value);
        var rejectedDocId = $"token-write-attempt-{Guid.NewGuid():N}";
        var rejectedDoc = TestProductCorpus.All[0] with { DocumentId = rejectedDocId };

        try
        {
            await tokenClient.Index(IndexName).AddDocumentsAsync([rejectedDoc], TestProductFields.DocumentId);
        }
        catch (global::Meilisearch.MeilisearchApiError)
        {
            // A future SDK version surfacing the 403 as a proper exception is also an acceptable
            // outcome — the assertion below still passes either way, since the document was never
            // written regardless of what this call returned or threw.
        }
        catch (global::System.Net.Http.HttpRequestException)
        {
        }

        // Decisive proof, independent of whatever the SDK's write call returned or threw: query the
        // document by id through the unrestricted MASTER client and confirm it was never indexed.
        var masterIndex = MeilisearchProviderFactory.CreateIndex<TestProduct>(_fixture, _definition);
        var getResult = await masterIndex.GetAsync(rejectedDocId, TenantScope.Of(TestProductCorpus.TenantA));
        getResult.IsFailure.Should().BeTrue(
            "a search-scoped tenant token carries no write-capable action, so the engine must reject the write " +
            "— proven here by the document never actually landing, since the SDK's own AddDocumentsAsync " +
            "return value/exception behavior for this failure is unreliable (see remarks)");
    }
}
