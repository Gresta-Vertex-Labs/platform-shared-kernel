using Testcontainers.Milvus;
using Xunit;

namespace SharedKernel.Testing.Containers;

/// <summary>
/// Testcontainers fixture wrapping a pinned Milvus container, running in genuine single-container
/// standalone mode, for integration tests.
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="IAsyncLifetime"/> exclusively — never a synchronous constructor that
/// blocks on the container startup task. Intended for use via xUnit's
/// <c>[CollectionDefinition]</c> + <c>ICollectionFixture&lt;T&gt;</c> — one instance per test
/// collection, never started per test method.
/// </para>
/// <para>
/// Built on the OFFICIAL <c>Testcontainers.Milvus</c> module's <see cref="MilvusBuilder"/>.
/// Constructed directly via the <c>ctor(string image)</c> overload (never the obsolete
/// parameterless <c>MilvusBuilder()</c> + <c>.WithImage(...)</c> pattern, which emits CS0618 as of
/// the pinned <c>4.13.0</c> <c>Testcontainers.Milvus</c> package version), pinned to
/// <c>milvusdb/milvus:v2.3.10</c> — confirmed to exist via <c>docker manifest inspect</c> against
/// the real registry at Core-phase implementation time (2026-07-22).
/// </para>
/// <para>
/// MINIMAL STANDALONE DEPLOYMENT MODE (the acceptance-criterion fact this fixture documents): the
/// <c>Testcontainers.Milvus</c> module's OWN DEFAULT configuration already runs Milvus in genuine
/// single-container standalone mode via its own <c>DEPLOY_MODE=STANDALONE</c> /
/// <c>ETCD_USE_EMBED=true</c> / <c>COMMON_STORAGETYPE=local</c> environment variables — embedded
/// etcd, no external etcd/MinIO sidecar container, no docker-compose orchestration. This fixture
/// uses that default AS-IS, with ZERO fixture-level environment-variable overrides — the module's
/// own <see cref="MilvusBuilder.WithEtcdEndpoint(string)"/> escape hatch (for switching to an
/// EXTERNAL etcd) is deliberately never called.
/// </para>
/// <para>
/// Relies on the module's own built-in <c>Wait.ForUnixContainer().UntilContainerIsHealthy()</c>
/// docker-healthcheck wait strategy (curl <c>/healthz</c> on management port 9091) — no override was
/// needed at implementation time, confirmed via a real-Docker smoke test.
/// </para>
/// <para>
/// <see cref="Endpoint"/> is sourced from <see cref="MilvusContainer.GetEndpoint"/> (gRPC port
/// 19530). Property naming is deliberately NOT reconciled against a <c>MilvusOptions</c> type, for
/// the identical reason <see cref="QdrantContainerFixture"/>'s remarks give — <c>MilvusOptions</c>
/// is folded into <c>10.Intelligence</c>'s own Core-phase task C-08, not yet ratified at Design
/// time. This fixture deliberately takes NO <c>ProjectReference</c> to
/// <c>SharedKernel.AI.Abstractions</c> or <c>.Milvus</c> — flat scalar connection properties only,
/// so it has zero build-time dependency on <c>10.Intelligence</c>'s own code landing.
/// </para>
/// </remarks>
public sealed class MilvusContainerFixture : IAsyncLifetime
{
    private const string ImageName = "milvusdb/milvus:v2.3.10";

    private readonly MilvusContainer _container = new MilvusBuilder(ImageName).Build();

    private bool _started;

    /// <summary>Gets the Milvus gRPC endpoint for the running container.</summary>
    /// <exception cref="InvalidOperationException">Read before <see cref="InitializeAsync"/> completes.</exception>
    public Uri Endpoint
    {
        get
        {
            EnsureStarted();
            return _container.GetEndpoint();
        }
    }

    /// <summary>Starts the underlying Milvus container in single-container standalone mode.</summary>
    /// <remarks>
    /// Relies exclusively on the module's own built-in
    /// <c>Wait.ForUnixContainer().UntilContainerIsHealthy()</c> docker-healthcheck wait strategy — no
    /// additional post-start poll is layered on top.
    /// </remarks>
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
