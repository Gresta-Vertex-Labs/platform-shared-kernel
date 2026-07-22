using System.Net.Sockets;
using SharedKernel.Testing.Containers;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Containers;

/// <summary>
/// Proves <see cref="QdrantContainerFixture"/> per the existing <c>MeilisearchContainerFixtureTests</c>/
/// <c>ElasticsearchContainerFixtureTests</c> pattern in this same project — Docker-gated: these tests
/// require a local Docker daemon to pull and start the pinned <c>qdrant/qdrant:v1.13.4</c> image,
/// mirroring how the six pre-existing sibling fixtures are proven here with no explicit
/// skip/availability check. Adoption by <c>SharedKernel.AI.Qdrant.Tests</c> (<c>10.Intelligence</c>'s
/// own T-03, not existing on disk yet) as the canonical shared fixture is an explicit cross-domain
/// follow-up (T-52) for a future <c>10.Intelligence</c> implementer pass — this domain never touches a
/// <c>.Tests</c> project outside its own.
/// </summary>
public sealed class QdrantContainerFixtureTests
{
    [Fact]
    public void GrpcEndpoint_ReadBeforeInitialize_Throws()
    {
        var fixture = new QdrantContainerFixture();
        Assert.Throws<InvalidOperationException>(() => fixture.GrpcEndpoint);
    }

    [Fact]
    public void HttpEndpoint_ReadBeforeInitialize_Throws()
    {
        var fixture = new QdrantContainerFixture();
        Assert.Throws<InvalidOperationException>(() => fixture.HttpEndpoint);
    }

    [Fact]
    public async Task FullLifecycle_StartsPinnedImage_ReadyzAndCollectionsRoundtripSucceed_GrpcChannelOpens_AndStopsCleanly()
    {
        var fixture = new QdrantContainerFixture();

        // InitializeAsync relies exclusively on the module's own built-in HTTP /readyz wait strategy
        // (no explicit post-start poll override, per the fixture's own design note) — a call that
        // returns at all is already partial proof that strategy resolved.
        await fixture.InitializeAsync();
        try
        {
            Assert.False(string.IsNullOrWhiteSpace(fixture.GrpcEndpoint));
            Assert.False(string.IsNullOrWhiteSpace(fixture.HttpEndpoint));

            using var httpClient = new HttpClient { BaseAddress = new Uri(fixture.HttpEndpoint) };

            // GET /readyz is Qdrant's own unauthenticated readiness probe — the exact route the
            // fixture's own wait strategy polls; re-checking it here proves InitializeAsync's
            // readiness gate was honest, not merely that the TCP port accepted a connection.
            using var readyzResponse = await httpClient.GetAsync("/readyz");
            Assert.True(readyzResponse.IsSuccessStatusCode);

            // GET /collections is an unauthenticated read-only roundtrip proving the HTTP API is
            // genuinely serving well-formed responses, not merely accepting the readiness probe.
            using var collectionsResponse = await httpClient.GetAsync("/collections");
            Assert.True(collectionsResponse.IsSuccessStatusCode);
            var body = await collectionsResponse.Content.ReadAsStringAsync();
            Assert.Contains("\"collections\"", body, StringComparison.Ordinal);

            // Bare gRPC-channel-open probe against the PRIMARY endpoint: a gRPC channel is
            // fundamentally a TCP connection to an HTTP/2 endpoint, so opening one against
            // GrpcEndpoint proves it is reachable without pulling in a full Qdrant.Client/protobuf
            // dependency — the fixture itself deliberately takes no ProjectReference to
            // SharedKernel.AI.Abstractions/.Qdrant, and this test respects that same isolation.
            var grpcUri = new Uri(fixture.GrpcEndpoint);
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(grpcUri.Host, grpcUri.Port);
            Assert.True(tcpClient.Connected);
        }
        finally
        {
            // Proves DisposeAsync stops the container without throwing.
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task GrpcEndpoint_AndHttpEndpoint_AreStableAcrossMultipleReads_AfterInitialize()
    {
        var fixture = new QdrantContainerFixture();

        await fixture.InitializeAsync();
        try
        {
            var grpc1 = fixture.GrpcEndpoint;
            var grpc2 = fixture.GrpcEndpoint;
            var http1 = fixture.HttpEndpoint;
            var http2 = fixture.HttpEndpoint;

            Assert.Equal(grpc1, grpc2);
            Assert.Equal(http1, http2);
            Assert.NotEqual(grpc1, http1);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }
}
