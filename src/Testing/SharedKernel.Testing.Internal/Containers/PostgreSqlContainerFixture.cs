using SharedKernel.Persistence.Testing;
using Xunit;

namespace SharedKernel.Testing.Containers;

/// <summary>
/// xUnit fixture over <see cref="PostgresTestServer"/> (<c>SharedKernel.Persistence.Testing</c>): one pinned
/// PostgreSQL container with the canonical roles, shared by a test collection.
/// </summary>
/// <remarks>
/// Implements <see cref="IAsyncLifetime"/> exclusively — never a synchronous constructor that
/// blocks on the container startup task. Intended for use via xUnit's
/// <c>[CollectionDefinition]</c> + <c>ICollectionFixture&lt;T&gt;</c> — one instance per test
/// collection, never started per test method. <see cref="ConnectionString"/> is the superuser's; tests that need
/// the production role split create a database through <see cref="Server"/>.
/// </remarks>
public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    private PostgresTestServer? _server;

    /// <summary>Gets the running server.</summary>
    /// <exception cref="InvalidOperationException">Read before <see cref="InitializeAsync"/> completes.</exception>
    public PostgresTestServer Server =>
        _server ?? throw new InvalidOperationException("Server cannot be read before InitializeAsync has completed.");

    /// <summary>Gets the superuser connection string for the running container.</summary>
    /// <exception cref="InvalidOperationException">Read before <see cref="InitializeAsync"/> completes.</exception>
    public string ConnectionString =>
        _server?.AdminConnectionString
        ?? throw new InvalidOperationException("ConnectionString cannot be read before InitializeAsync has completed.");

    /// <inheritdoc />
    public async Task InitializeAsync() => _server = await PostgresTestServer.StartAsync().ConfigureAwait(false);

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_server is not null)
            await _server.DisposeAsync().ConfigureAwait(false);
    }
}
