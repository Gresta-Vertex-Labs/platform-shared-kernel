using FluentAssertions;
using SharedKernel.Storage.Abstractions.Errors;
using SharedKernel.Storage.S3.Tests.Containers;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Storage.S3.Tests.FileStorage;

/// <summary>
/// T-07: <c>S3FileStorage.CheckHealthAsync</c> connectivity-probe coverage — succeeds against a
/// reachable, existing bucket; fails with <see cref="StorageErrors.ConnectivityFailure"/> against
/// an unreachable/misconfigured bucket.
/// </summary>
[Collection(MinioCollection.Name)]
public sealed class S3ConnectivityProbeTests(MinioContainerFixture fixture)
{
    [Fact]
    public async Task CheckHealthAsync_ReachableBucket_ReturnsSuccess()
    {
        var storage = MinioProviderFactory.CreateFileStorage(fixture);

        var result = await storage.CheckHealthAsync(fixture.DefaultBucket, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task CheckHealthAsync_NonExistentBucket_ReturnsConnectivityFailure()
    {
        var storage = MinioProviderFactory.CreateFileStorage(fixture);
        var nonExistentBucket = $"does-not-exist-{Guid.NewGuid():N}";

        var result = await storage.CheckHealthAsync(nonExistentBucket, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StorageErrors.ConnectivityFailure(nonExistentBucket));
    }
}
