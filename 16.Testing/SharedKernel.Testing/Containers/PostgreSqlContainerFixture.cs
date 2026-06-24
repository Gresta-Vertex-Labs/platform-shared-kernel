using Testcontainers.PostgreSql;
using Xunit;

namespace SharedKernel.Testing.Containers;

/// <summary>
/// Testcontainers fixture wrapping a pinned PostgreSQL container for integration tests.
/// </summary>
/// <remarks>
/// Implements <see cref="IAsyncLifetime"/> exclusively — never a synchronous constructor that
/// blocks on the container startup task. Intended for use via xUnit's
/// <c>[CollectionDefinition]</c> + <c>ICollectionFixture&lt;T&gt;</c> — one instance per test
/// collection, never started per test method.
/// </remarks>
public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16.4")
        .WithDatabase("sharedkernel_test")
        .WithUsername("sharedkernel")
        .WithPassword("sharedkernel")
        .Build();

    private bool _started;

    /// <summary>Gets the connection string for the running container.</summary>
    /// <exception cref="InvalidOperationException">Read before <see cref="InitializeAsync"/> completes.</exception>
    public string ConnectionString
    {
        get
        {
            if (!_started)
            {
                throw new InvalidOperationException(
                    "ConnectionString cannot be read before InitializeAsync has completed.");
            }

            return _container.GetConnectionString();
        }
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        _started = true;
    }

    /// <inheritdoc />
    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}
