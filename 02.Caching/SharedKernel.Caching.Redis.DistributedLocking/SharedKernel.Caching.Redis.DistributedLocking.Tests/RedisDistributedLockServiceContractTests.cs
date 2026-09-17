using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.DistributedLocking.Tests.Abstractions;
using Xunit;

namespace SharedKernel.Caching.Redis.DistributedLocking.Tests;

/// <summary>
/// Runs the <see cref="IDistributedLockService"/> contract against the Redis implementation on a real Redis server.
/// </summary>
[Collection("Redis")]
public sealed class RedisDistributedLockServiceContractTests(RedisFixture fixture) : IDistributedLockServiceContractTests
{
    protected override IDistributedLockService Service => fixture.LockService;

    protected override string NewResource(string prefix) => RedisFixture.NewResource(prefix);
}
