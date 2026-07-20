using System.Net.Http.Headers;
using SharedKernel.Testing.Containers;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Containers;

/// <summary>
/// Proves <see cref="MeilisearchContainerFixture"/> per the existing <c>MinioContainerFixtureTests</c>
/// pattern in this same project — Docker-gated: these tests require a local Docker daemon to pull
/// and start the pinned <c>getmeili/meilisearch:v1.20.0</c> image, mirroring how the four
/// pre-existing sibling fixtures are proven here with no explicit skip/availability check.
/// Adoption by <c>SharedKernel.Search.Meilisearch.Tests</c> as the canonical shared fixture is an
/// explicit cross-domain follow-up (T-48) for a future <c>09.Search</c> implementer pass — this
/// domain never touches a <c>.Tests</c> project outside its own.
/// </summary>
public sealed class MeilisearchContainerFixtureTests
{
    [Fact]
    public void Url_ReadBeforeInitialize_Throws()
    {
        var fixture = new MeilisearchContainerFixture();
        Assert.Throws<InvalidOperationException>(() => fixture.Url);
    }

    [Fact]
    public void ApiKey_ReadBeforeInitialize_Throws()
    {
        var fixture = new MeilisearchContainerFixture();
        Assert.Throws<InvalidOperationException>(() => fixture.ApiKey);
    }

    [Fact]
    public async Task FullLifecycle_StartsPinnedImage_HealthEndpointSucceeds_AuthenticatedCallSucceeds_AndStopsCleanly()
    {
        var fixture = new MeilisearchContainerFixture();

        await fixture.InitializeAsync();
        try
        {
            Assert.False(string.IsNullOrWhiteSpace(fixture.Url));
            Assert.StartsWith("http://", fixture.Url, StringComparison.Ordinal);
            Assert.True(fixture.ApiKey.Length >= 16);

            using var client = new HttpClient { BaseAddress = new Uri(fixture.Url) };

            // GET /health is the one route Meilisearch leaves unprotected by the master key
            // regardless of key configuration -- this is what the fixture's own wait strategy
            // polls, and re-checking it here proves InitializeAsync's readiness gate was honest.
            using var healthResponse = await client.GetAsync("/health");
            Assert.True(healthResponse.IsSuccessStatusCode);

            // An authenticated call against a protected route proves ApiKey is genuinely wired as
            // this instance's MEILI_MASTER_KEY, not merely a well-formed string.
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.ApiKey);
            using var indexesResponse = await client.GetAsync("/indexes");
            Assert.True(indexesResponse.IsSuccessStatusCode);
        }
        finally
        {
            // Proves DisposeAsync stops the container without throwing.
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task Url_AndApiKey_AreStableAcrossMultipleReads_AfterInitialize()
    {
        var fixture = new MeilisearchContainerFixture();

        await fixture.InitializeAsync();
        try
        {
            var url1 = fixture.Url;
            var url2 = fixture.Url;
            var apiKey1 = fixture.ApiKey;
            var apiKey2 = fixture.ApiKey;

            Assert.Equal(url1, url2);
            Assert.Equal(apiKey1, apiKey2);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }
}
