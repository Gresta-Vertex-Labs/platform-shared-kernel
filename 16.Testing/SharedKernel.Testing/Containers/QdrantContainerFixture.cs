using Testcontainers.Qdrant;
using Xunit;

namespace SharedKernel.Testing.Containers;

/// <summary>
/// Testcontainers fixture wrapping a pinned Qdrant container for integration tests.
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="IAsyncLifetime"/> exclusively — never a synchronous constructor that
/// blocks on the container startup task. Intended for use via xUnit's
/// <c>[CollectionDefinition]</c> + <c>ICollectionFixture&lt;T&gt;</c> — one instance per test
/// collection, never started per test method.
/// </para>
/// <para>
/// Built on the OFFICIAL <c>Testcontainers.Qdrant</c> module's <see cref="QdrantBuilder"/> —
/// unlike <see cref="MeilisearchContainerFixture"/>, no hand-rolled generic-builder fallback is
/// needed. Constructed directly via the <c>ctor(string image)</c> overload (never the obsolete
/// parameterless <c>QdrantBuilder()</c> + <c>.WithImage(...)</c> pattern, which emits CS0618 as of
/// the pinned <c>4.13.0</c> <c>Testcontainers.Qdrant</c> package version), pinned to
/// <c>qdrant/qdrant:v1.13.4</c> — confirmed to exist via <c>docker manifest inspect</c> against the
/// real registry at Core-phase implementation time (2026-07-22).
/// </para>
/// <para>
/// <see cref="GrpcEndpoint"/> is the PRIMARY endpoint since <c>Qdrant.Client</c>, the official .NET
/// SDK <c>10.Intelligence</c>'s own Technology Stack pins, is gRPC/protobuf-based.
/// <see cref="HttpEndpoint"/> is a secondary convenience for manual debugging/readiness-probe reuse,
/// not because any current consumer needs it. Both rely on the module's own built-in HTTP
/// <c>/readyz</c> wait strategy — no explicit post-start poll override is added here; one would be
/// added only if Core-phase smoke-testing against real Docker revealed a readiness race, mirroring
/// the <see cref="ElasticsearchContainerFixture"/> precedent, never added preemptively. A real-Docker
/// smoke test at implementation time found no such race — the module's own wait strategy alone was
/// sufficient.
/// </para>
/// <para>
/// Property names are deliberately NOT reconciled 1:1 against a <c>QdrantOptions</c> type (unlike
/// <see cref="MinioContainerFixture"/>/<see cref="MeilisearchContainerFixture"/>/
/// <see cref="ElasticsearchContainerFixture"/>, each matching a live, already-ratified
/// <c>XOptions</c> type in the owning domain) — <c>QdrantOptions</c> is not yet designed in
/// <c>10.Intelligence</c>; its shape is folded into that domain's own Core-phase task C-05, not yet
/// ratified at Design time. This fixture instead exposes the official
/// <c>Testcontainers.Qdrant</c> module's own natural connection surface. A future reconciliation
/// pass, once <c>QdrantOptions</c> lands, is a tracked cross-domain follow-up, not performed here.
/// This fixture deliberately takes NO <c>ProjectReference</c> to <c>SharedKernel.AI.Abstractions</c>
/// or <c>.Qdrant</c> — flat scalar connection properties only, so it has zero build-time dependency
/// on <c>10.Intelligence</c>'s own code landing (unlike
/// <c>Intelligence/InMemoryVectorCollection&lt;TRecord&gt;</c>, which does).
/// </para>
/// </remarks>
public sealed class QdrantContainerFixture : IAsyncLifetime
{
    private const string ImageName = "qdrant/qdrant:v1.13.4";

    private readonly QdrantContainer _container = new QdrantBuilder(ImageName).Build();

    private bool _started;

    /// <summary>
    /// Gets the gRPC connection string for the running Qdrant container — the PRIMARY endpoint,
    /// since <c>Qdrant.Client</c> (the official .NET SDK) is gRPC/protobuf-based.
    /// </summary>
    /// <exception cref="InvalidOperationException">Read before <see cref="InitializeAsync"/> completes.</exception>
    public string GrpcEndpoint
    {
        get
        {
            EnsureStarted();
            return _container.GetGrpcConnectionString();
        }
    }

    /// <summary>
    /// Gets the HTTP/REST connection string for the running Qdrant container — a secondary
    /// convenience for manual debugging/readiness-probe reuse.
    /// </summary>
    /// <exception cref="InvalidOperationException">Read before <see cref="InitializeAsync"/> completes.</exception>
    public string HttpEndpoint
    {
        get
        {
            EnsureStarted();
            return _container.GetHttpConnectionString();
        }
    }

    /// <summary>Starts the underlying Qdrant container.</summary>
    /// <remarks>
    /// Relies exclusively on the module's own built-in HTTP <c>/readyz</c> wait strategy — no
    /// additional post-start poll is layered on top, since a real-Docker smoke test at
    /// implementation time found the module's own wait strategy alone to be sufficient (unlike
    /// <see cref="ElasticsearchContainerFixture"/>, which needed one).
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
