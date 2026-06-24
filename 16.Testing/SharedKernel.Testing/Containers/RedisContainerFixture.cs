using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Testing.Containers;

/// <summary>
/// Testcontainers fixture wrapping a pinned Redis container for integration tests.
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="IAsyncLifetime"/> exclusively. Intended for use via xUnit's
/// <c>[CollectionDefinition]</c> + <c>ICollectionFixture&lt;T&gt;</c> — one instance per test
/// collection, never started per test method.
/// </para>
/// <para>
/// Must be independently usable by each of the four split <c>02.Caching</c> capability
/// <c>.Tests</c> projects (FusionCache, Redis L2, DistributedLocking, HashStore, PubSub) without
/// requiring all four capabilities wired up simultaneously.
/// </para>
/// </remarks>
public sealed class RedisContainerFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder()
        .WithImage("redis:7.4")
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
