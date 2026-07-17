using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Storage.Obs.BlobUri;
using SharedKernel.Storage.Obs.FileStorage;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Storage.Obs.Tests.Containers;

/// <summary>
/// Builds real, non-mocked <see cref="ObsFileStorage"/>/<see cref="ObsBlobUriGenerator"/> instances
/// wired directly against a running <see cref="MinioContainerFixture"/> — MinIO stands in for the
/// OBS S3-compatible endpoint in CI, since OBS itself is never available there. The construction
/// shape mirrors <c>AddSharedKernelObsStorage</c> minus the DI container, so behavioral/round-trip
/// tests exercise the exact same <see cref="IAmazonS3"/> configuration a consuming host would.
/// </summary>
internal static class MinioProviderFactory
{
    /// <summary>Creates an <see cref="IAmazonS3"/> client pointed at <paramref name="fixture"/>'s running MinIO container.</summary>
    public static AmazonS3Client CreateClient(MinioContainerFixture fixture) =>
        new(
            new BasicAWSCredentials(fixture.AccessKeyId, fixture.SecretAccessKey),
            new AmazonS3Config
            {
                // ObsStorageOptions.Endpoint maps 1:1 to the fixture's ServiceUrl — a straight
                // property assignment, never provider-specific branching (per MinioContainerFixture's
                // own documented design intent).
                ServiceURL = fixture.ServiceUrl,
                ForcePathStyle = fixture.ForcePathStyle,
                // MinIO's Testcontainers connection string is a plain http:// endpoint (no TLS) — the
                // SDK's presigned-URL generation needs UseHttp explicitly set to true or it emits an
                // https:// URL regardless of ServiceURL's own scheme, which then fails the TLS
                // handshake against MinIO's HTTP-only listener.
                UseHttp = fixture.ServiceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase),
                // Mirrors ObsStorageServiceCollectionExtensions.CreateClient's own configuration
                // exactly. Does NOT fully resolve DeleteObjects against this fixture's pinned MinIO
                // release — see ObsRoundTripTests' DeleteManyAsync test for the confirmed remaining
                // limitation (same root cause already investigated in SharedKernel.Storage.S3.Tests).
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            });

    /// <summary>Creates an <see cref="ObsFileStorage"/> backed by a fresh client against <paramref name="fixture"/>.</summary>
    public static ObsFileStorage CreateFileStorage(MinioContainerFixture fixture) =>
        new(CreateClient(fixture), NullLogger<ObsFileStorage>.Instance);

    /// <summary>Creates an <see cref="ObsBlobUriGenerator"/> backed by a fresh client against <paramref name="fixture"/>.</summary>
    public static ObsBlobUriGenerator CreateBlobUriGenerator(MinioContainerFixture fixture) =>
        new(CreateClient(fixture), new SystemClock(), NullLogger<ObsBlobUriGenerator>.Instance);
}
