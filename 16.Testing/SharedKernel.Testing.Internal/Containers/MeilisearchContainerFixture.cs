using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace SharedKernel.Testing.Containers;

/// <summary>
/// Testcontainers fixture wrapping a pinned Meilisearch container for integration tests.
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="IAsyncLifetime"/> exclusively — never a synchronous constructor that
/// blocks on the container startup task. Intended for use via xUnit's
/// <c>[CollectionDefinition]</c> + <c>ICollectionFixture&lt;T&gt;</c> — one instance per test
/// collection, never started per test method.
/// </para>
/// <para>
/// Hand-rolled on the generic <see cref="ContainerBuilder"/>/<see cref="IContainer"/> API rather
/// than a dedicated <c>Testcontainers.Meilisearch</c> module — no such module exists on
/// nuget.org (confirmed 404). Constructed directly via the <c>ctor(string image)</c> overload
/// (never the obsolete parameterless <c>ContainerBuilder()</c> + <c>.WithImage(...)</c> pattern,
/// which emits CS0618 as of the pinned <c>4.13.0</c> <c>Testcontainers</c> package version).
/// </para>
/// <para>
/// <see cref="Url"/>/<see cref="ApiKey"/> are named to match <c>09.Search</c>'s
/// <c>MeilisearchOptions.Url</c>/<c>.ApiKey</c> 1:1, mirroring
/// <see cref="MinioContainerFixture"/>'s established "match the real Options type's property
/// names exactly so the consuming <c>.Tests</c> project binds with zero renaming" convention.
/// This fixture deliberately takes NO <c>ProjectReference</c> to
/// <c>SharedKernel.Search.Abstractions</c> or <c>.Meilisearch</c> — flat scalar connection
/// properties only, exactly like its siblings expose a raw <c>ConnectionString</c>, so it has
/// zero build-time dependency on <c>09.Search</c>'s own code.
/// </para>
/// </remarks>
public sealed class MeilisearchContainerFixture : IAsyncLifetime
{
    private const string ImageName = "getmeili/meilisearch:v1.20.0";
    private const int MeilisearchPort = 7700;

    // At least 16 bytes, per Meilisearch's own minimum master-key length requirement.
    private const string FixedApiKey = "sharedkernel-testing-meilisearch-master-key";

    private readonly IContainer _container = new ContainerBuilder(ImageName)
        .WithPortBinding(MeilisearchPort, assignRandomHostPort: true)
        .WithEnvironment("MEILI_MASTER_KEY", FixedApiKey)
        .WithEnvironment("MEILI_NO_ANALYTICS", "true")
        .WithWaitStrategy(
            Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(request => request.ForPort(MeilisearchPort).ForPath("/health")))
        .Build();

    private bool _started;

    /// <summary>Gets the Meilisearch instance URL. Matches <c>MeilisearchOptions.Url</c> 1:1.</summary>
    /// <exception cref="InvalidOperationException">Read before <see cref="InitializeAsync"/> completes.</exception>
    public string Url
    {
        get
        {
            EnsureStarted();
            return $"http://{_container.Hostname}:{_container.GetMappedPublicPort(MeilisearchPort)}";
        }
    }

    /// <summary>
    /// Gets the fixture's own fixed master key — at least 16 bytes, per Meilisearch's own minimum
    /// key-length requirement. Matches <c>MeilisearchOptions.ApiKey</c> 1:1.
    /// </summary>
    /// <exception cref="InvalidOperationException">Read before <see cref="InitializeAsync"/> completes.</exception>
    public string ApiKey
    {
        get
        {
            EnsureStarted();
            return FixedApiKey;
        }
    }

    /// <summary>
    /// Starts the underlying Meilisearch container. The wait strategy polls the unauthenticated
    /// <c>GET /health</c> route — the one route Meilisearch leaves unprotected by the master key
    /// regardless of key configuration, making it the correct universal readiness probe.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        _started = true;
    }

    /// <inheritdoc />
    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);

    private void EnsureStarted()
    {
        if (!_started)
        {
            throw new InvalidOperationException(
                "This property cannot be read before InitializeAsync has completed.");
        }
    }
}
