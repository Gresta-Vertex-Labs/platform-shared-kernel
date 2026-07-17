using FluentAssertions;
using SharedKernel.Storage.Abstractions.Errors;
using SharedKernel.Storage.Obs.Tests.Containers;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Storage.Obs.Tests.FileStorage;

/// <summary>
/// T-14: <c>ObsFileStorage.CheckHealthAsync</c> connectivity-probe coverage — mirrors
/// <c>SharedKernel.Storage.S3.Tests</c>' <c>S3ConnectivityProbeTests</c> one-for-one against the Obs
/// provider types.
/// </summary>
[Collection(MinioCollection.Name)]
public sealed class ObsConnectivityProbeTests(MinioContainerFixture fixture)
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
