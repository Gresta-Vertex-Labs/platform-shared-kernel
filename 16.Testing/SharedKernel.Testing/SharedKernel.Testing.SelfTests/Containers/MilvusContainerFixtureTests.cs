using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using SharedKernel.Testing.Containers;
using Testcontainers.Milvus;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Containers;

/// <summary>
/// Proves <see cref="MilvusContainerFixture"/> per the existing <c>QdrantContainerFixtureTests</c>
/// pattern in this same project — Docker-gated: these tests require a local Docker daemon to pull and
/// start the pinned <c>milvusdb/milvus:v2.3.10</c> image, mirroring how the seven pre-existing sibling
/// fixtures are proven here with no explicit skip/availability check. Adoption by
/// <c>SharedKernel.AI.Milvus.Tests</c> (<c>10.Intelligence</c>'s own T-05, not existing on disk yet) as
/// the canonical shared fixture is an explicit cross-domain follow-up (T-53) for a future
/// <c>10.Intelligence</c> implementer pass — this domain never touches a <c>.Tests</c> project outside
/// its own.
/// </summary>
public sealed class MilvusContainerFixtureTests
{
    [Fact]
    public void Endpoint_ReadBeforeInitialize_Throws()
    {
        var fixture = new MilvusContainerFixture();
        Assert.Throws<InvalidOperationException>(() => fixture.Endpoint);
    }

    [Fact]
    public async Task FullLifecycle_StartsPinnedImage_HealthzOverManagementPortSucceeds_GrpcChannelOpens_NoExternalEtcdOrMinioContainer_AndStopsCleanly()
    {
        // Snapshot taken as late as possible before starting the container, and re-checked as early
        // as possible after InitializeAsync returns (below), to keep the window for a concurrently
        // running sibling fixture test (e.g. MinioContainerFixtureTests, which this same self-test
        // project also runs, possibly in parallel per xUnit's default cross-class parallelization) as
        // narrow as practical. This is a documented, accepted residual race, not a guarantee of
        // perfect isolation from the shared local Docker daemon — see the assertion below.
        var beforeImages = await GetRunningContainerImagesAsync();

        var fixture = new MilvusContainerFixture();

        // InitializeAsync relies exclusively on the module's own built-in
        // Wait.ForUnixContainer().UntilContainerIsHealthy() docker-healthcheck wait strategy (no
        // explicit post-start poll override, per the fixture's own design note) — a call that
        // returns at all is already partial proof that strategy resolved.
        await fixture.InitializeAsync();
        try
        {
            var endpoint = fixture.Endpoint;
            Assert.Equal("http", endpoint.Scheme);
            Assert.False(string.IsNullOrWhiteSpace(endpoint.Host));

            // Bare gRPC-channel-open probe against the gRPC endpoint: a gRPC channel is fundamentally
            // a TCP connection to an HTTP/2 endpoint, so opening one against Endpoint proves it is
            // reachable without pulling in a full Milvus.Client/protobuf dependency — the fixture
            // itself deliberately takes no ProjectReference to SharedKernel.AI.Abstractions/.Milvus,
            // and this test respects that same isolation.
            using (var tcpClient = new TcpClient())
            {
                await tcpClient.ConnectAsync(endpoint.Host, endpoint.Port);
                Assert.True(tcpClient.Connected);
            }

            // GET /healthz over the management port (container port 9091) is the exact
            // docker-healthcheck route the fixture's own wait strategy polls. The fixture
            // deliberately exposes no management-port property of its own (flat scalar Endpoint
            // only, per its own design note), so the mapped host port is read via the underlying
            // Testcontainers MilvusContainer's own public GetMappedPublicPort(int), reached through
            // the fixture's private backing field — the only way to reach it without changing the
            // fixture's already-shipped (Core-phase, out of scope here) public surface.
            var container = GetUnderlyingContainer(fixture);
            var managementPort = container.GetMappedPublicPort(9091);

            using var httpClient = new HttpClient { BaseAddress = new Uri($"http://{endpoint.Host}:{managementPort}") };
            using var healthzResponse = await httpClient.GetAsync("/healthz");
            Assert.True(healthzResponse.IsSuccessStatusCode);

            // Explicit proof of this fixture's own hard acceptance criterion (P-283/WO-045): the
            // module's own embedded-etcd standalone mode (DEPLOY_MODE=STANDALONE / ETCD_USE_EMBED=true
            // / COMMON_STORAGETYPE=local, the fixture's documented default-as-is configuration) must
            // never spin up a separate etcd or MinIO sidecar container. Diffed against the snapshot
            // taken immediately before InitializeAsync so pre-existing, unrelated containers already
            // running in the local Docker context (this repo's own dev Postgres/RabbitMQ/Aspire
            // Dashboard containers, or a MinioContainerFixture test that had already started before
            // this test began) never produce a false failure — only NEWLY created containers count.
            var afterImages = await GetRunningContainerImagesAsync();
            var newImages = afterImages.Except(beforeImages).ToList();

            Assert.DoesNotContain(
                newImages,
                image => image.Contains("etcd", StringComparison.OrdinalIgnoreCase)
                    || image.Contains("minio", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            // Proves DisposeAsync stops the container without throwing.
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task Endpoint_IsStableAcrossMultipleReads_AfterInitialize()
    {
        var fixture = new MilvusContainerFixture();

        await fixture.InitializeAsync();
        try
        {
            var endpoint1 = fixture.Endpoint;
            var endpoint2 = fixture.Endpoint;

            Assert.Equal(endpoint1, endpoint2);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    private static MilvusContainer GetUnderlyingContainer(MilvusContainerFixture fixture)
    {
        var field = typeof(MilvusContainerFixture).GetField("_container", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "MilvusContainerFixture no longer exposes a private '_container' field — this test's " +
                "reflection-based access to GetMappedPublicPort(9091) needs updating.");

        return (MilvusContainer)field.GetValue(fixture)!;
    }

    /// <summary>
    /// Shells out to the Docker CLI for the image name of every currently running container. Reads
    /// stdout and stderr CONCURRENTLY via <see cref="Task.WhenAll(Task[])"/> rather than sequentially
    /// — a sequential read-then-read pattern risks a classic pipe-buffer deadlock if the child process
    /// writes enough to the stream not yet being drained; `docker ps` output is small, but the
    /// concurrent-read pattern is used defensively regardless.
    /// </summary>
    private static async Task<IReadOnlyList<string>> GetRunningContainerImagesAsync()
    {
        var startInfo = new ProcessStartInfo("docker", "ps --format \"{{.Image}}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the docker CLI process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync());

        var stdout = await stdoutTask;
        return stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
