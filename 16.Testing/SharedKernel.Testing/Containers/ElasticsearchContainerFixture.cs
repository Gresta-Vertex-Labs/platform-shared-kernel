using System.Net.Http.Headers;
using System.Text;
using Testcontainers.Elasticsearch;
using Xunit;

namespace SharedKernel.Testing.Containers;

/// <summary>
/// Testcontainers fixture wrapping a pinned Elasticsearch container for integration tests.
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="IAsyncLifetime"/> exclusively — never a synchronous constructor that
/// blocks on the container startup task. Intended for use via xUnit's
/// <c>[CollectionDefinition]</c> + <c>ICollectionFixture&lt;T&gt;</c> — one instance per test
/// collection, never started per test method.
/// </para>
/// <para>
/// Built on the OFFICIAL <c>Testcontainers.Elasticsearch</c> module's
/// <see cref="ElasticsearchBuilder"/> (unlike <see cref="MeilisearchContainerFixture"/>, a real
/// module exists here — only its default image needs overriding). Constructed directly via the
/// <c>ctor(string image)</c> overload (never the obsolete parameterless
/// <c>ElasticsearchBuilder()</c> + <c>.WithImage(...)</c> pattern, which emits CS0618 as of the
/// pinned <c>4.13.0</c> <c>Testcontainers.Elasticsearch</c> package version) to a pinned 9.x
/// server tag — the module's own default (<c>elasticsearch:8.6.1</c>) is an unsupported pairing
/// with <c>09.Search</c>'s pinned <c>Elastic.Clients.Elasticsearch</c> <c>9.4.2</c> client per
/// Elastic's published compatibility matrix, so the server tag here is pinned to the identical
/// <c>9.4.2</c> version to keep client and server in lockstep.
/// </para>
/// <para>
/// The module runs Elasticsearch 9.x secure-by-default over HTTPS with a self-signed
/// certificate (it does not set <c>xpack.security.enabled=false</c>) — handled via
/// <see cref="AllowInvalidCertificates"/> (always <see langword="true"/>), never a trusted-CA
/// workaround. <see cref="Username"/> is fixed to the module's own
/// <see cref="ElasticsearchBuilder.DefaultUsername"/> ("elastic") — the module exposes no
/// <c>WithUsername</c> fluent method, so the default is the only reachable value.
/// <see cref="Password"/> is likewise the module's own
/// <see cref="ElasticsearchBuilder.DefaultPassword"/> — the module never overrides it via
/// <c>WithPassword</c>, and <see cref="ElasticsearchContainer"/> exposes no
/// <c>GetPassword()</c> accessor to read back a generated value, so the default is both the
/// configured and the only observable value.
/// </para>
/// <para>
/// <see cref="Nodes"/> is a single-element array holding the container's mapped HTTPS base URL —
/// matches <c>ElasticSearchOptions.Nodes</c> 1:1. This fixture deliberately takes NO
/// <c>ProjectReference</c> to <c>SharedKernel.Search.Abstractions</c> or <c>.ElasticSearch</c> —
/// flat scalar connection properties only, so it has zero build-time dependency on
/// <c>09.Search</c>'s own code.
/// </para>
/// </remarks>
public sealed class ElasticsearchContainerFixture : IAsyncLifetime
{
    private const string ImageName = "docker.elastic.co/elasticsearch/elasticsearch:9.4.2";
    private const string ClusterHealthPath = "/_cluster/health?wait_for_status=yellow&timeout=1s";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private const int MaxReadinessPollAttempts = 60;

    private readonly ElasticsearchContainer _container = new ElasticsearchBuilder(ImageName).Build();

    private bool _started;

    /// <summary>
    /// Gets a single-element array containing the container's mapped HTTPS base URL. Matches
    /// <c>ElasticSearchOptions.Nodes</c> 1:1.
    /// </summary>
    /// <exception cref="InvalidOperationException">Read before <see cref="InitializeAsync"/> completes.</exception>
    public string[] Nodes
    {
        get
        {
            EnsureStarted();
            return [_container.GetConnectionString()];
        }
    }

    /// <summary>
    /// Gets the Elasticsearch username — fixed to the module's own
    /// <see cref="ElasticsearchBuilder.DefaultUsername"/> ("elastic"). Matches
    /// <c>ElasticSearchOptions.Username</c> 1:1.
    /// </summary>
    public string Username => ElasticsearchBuilder.DefaultUsername;

    /// <summary>
    /// Gets the Elasticsearch superuser password — the module's own
    /// <see cref="ElasticsearchBuilder.DefaultPassword"/>, since this fixture never overrides it
    /// via <c>WithPassword</c> and the module exposes no way to read a generated value back.
    /// Matches <c>ElasticSearchOptions.Password</c> 1:1.
    /// </summary>
    public string Password => ElasticsearchBuilder.DefaultPassword;

    /// <summary>
    /// Gets a value indicating whether TLS certificate validation must be disabled to reach this
    /// container. Always <see langword="true"/> — the module runs secure-by-default over HTTPS
    /// with a self-signed certificate, and this fixture exposes no trusted-CA path. Matches
    /// <c>ElasticSearchOptions.AllowInvalidCertificates</c> 1:1, mirroring
    /// <see cref="MinioContainerFixture.ForcePathStyle"/>'s identical always-true precedent.
    /// </summary>
    public bool AllowInvalidCertificates => true;

    /// <summary>
    /// Starts the underlying Elasticsearch container, then polls its cluster-health endpoint
    /// until it reports at least <c>yellow</c> status.
    /// </summary>
    /// <remarks>
    /// The module's own built-in wait strategy only proves the HTTP listener is accepting
    /// connections — it does not prove the single node has finished cluster bootstrap and is
    /// actually ready to serve index operations, a documented readiness race
    /// (<c>testcontainers-dotnet#955</c>). This method closes that race with an explicit
    /// post-start poll loop against <c>GET /_cluster/health</c>, using
    /// <see cref="AllowInvalidCertificates"/>'s self-signed-certificate bypass and
    /// <see cref="Username"/>/<see cref="Password"/> basic auth. Each iteration awaits a real
    /// HTTP round trip (or, on transport failure, a short <see cref="Task.Delay(TimeSpan)"/>
    /// retry interval) — this is never a blind, unconditional delay standing in for a readiness
    /// check.
    /// </remarks>
    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        _started = true;

        await WaitForClusterHealthAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);

    private async Task WaitForClusterHealthAsync()
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(_container.GetConnectionString()) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}")));

        for (var attempt = 0; attempt < MaxReadinessPollAttempts; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(ClusterHealthPath).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Node not ready to accept requests yet — retry.
            }

            await Task.Delay(PollInterval).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            "Elasticsearch cluster did not report at least yellow status within the allotted startup window.");
    }

    private void EnsureStarted()
    {
        if (!_started)
        {
            throw new InvalidOperationException(
                "This property cannot be read before InitializeAsync has completed.");
        }
    }
}
