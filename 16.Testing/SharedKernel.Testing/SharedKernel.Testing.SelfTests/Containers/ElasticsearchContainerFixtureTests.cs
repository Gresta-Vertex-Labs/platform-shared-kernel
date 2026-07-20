using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SharedKernel.Testing.Containers;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Containers;

/// <summary>
/// Proves <see cref="ElasticsearchContainerFixture"/> per the existing
/// <c>MinioContainerFixtureTests</c> pattern in this same project — Docker-gated: these tests
/// require a local Docker daemon to pull and start the pinned
/// <c>docker.elastic.co/elasticsearch/elasticsearch:9.4.2</c> image, and additionally confirm that
/// image is genuinely a 9.x server (never the <c>Testcontainers.Elasticsearch</c> module's own
/// <c>elasticsearch:8.6.1</c> default) and that the fixture's explicit post-start cluster-health
/// poll (the <c>testcontainers-dotnet#955</c> readiness-race workaround) resolves before
/// <see cref="ElasticsearchContainerFixture.InitializeAsync"/> returns. Adoption by
/// <c>SharedKernel.Search.ElasticSearch.Tests</c> as the canonical shared fixture is an explicit
/// cross-domain follow-up (T-49) for a future <c>09.Search</c> implementer pass — this domain
/// never touches a <c>.Tests</c> project outside its own.
/// </summary>
public sealed class ElasticsearchContainerFixtureTests
{
    [Fact]
    public void Nodes_ReadBeforeInitialize_Throws()
    {
        var fixture = new ElasticsearchContainerFixture();
        Assert.Throws<InvalidOperationException>(() => fixture.Nodes);
    }

    [Fact]
    public void Username_Password_AndAllowInvalidCertificates_AreReadableBeforeInitialize()
    {
        // Unlike Nodes (container-derived), these three are fixed facts about the pinned module
        // configuration and never depend on the started container.
        var fixture = new ElasticsearchContainerFixture();

        Assert.Equal("elastic", fixture.Username);
        Assert.False(string.IsNullOrWhiteSpace(fixture.Password));
        Assert.True(fixture.AllowInvalidCertificates);
    }

    [Fact]
    public async Task FullLifecycle_StartsPinned9xImage_NeverModule8xDefault_ClusterHealthResolves_AndStopsCleanly()
    {
        var fixture = new ElasticsearchContainerFixture();

        // InitializeAsync's explicit post-start cluster-health poll (beyond the module's own
        // built-in wait strategy) must resolve before this call returns -- if it never resolves,
        // this call throws InvalidOperationException once its poll budget is exhausted.
        await fixture.InitializeAsync();
        try
        {
            Assert.Single(fixture.Nodes);
            Assert.StartsWith("https://", fixture.Nodes[0], StringComparison.Ordinal);

            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(fixture.Nodes[0]) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{fixture.Username}:{fixture.Password}")));

            using var rootResponse = await client.GetAsync("/");
            Assert.True(rootResponse.IsSuccessStatusCode);

            var body = await rootResponse.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            var versionNumber = document.RootElement.GetProperty("version").GetProperty("number").GetString();

            // Proves the explicit 9.4.2 override actually started -- never the module's own
            // elasticsearch:8.6.1 default, which is an unsupported pairing with 09.Search's
            // pinned 9.4.2 client.
            Assert.StartsWith("9.", versionNumber, StringComparison.Ordinal);

            using var healthResponse = await client.GetAsync("/_cluster/health?wait_for_status=yellow&timeout=1s");
            Assert.True(healthResponse.IsSuccessStatusCode);
        }
        finally
        {
            // Proves DisposeAsync stops the container without throwing.
            await fixture.DisposeAsync();
        }
    }
}
