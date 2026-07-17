using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Storage.S3.BlobUri;
using SharedKernel.Storage.S3.FileStorage;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Storage.S3.Tests.Containers;

/// <summary>
/// Builds real, non-mocked <see cref="S3FileStorage"/>/<see cref="S3BlobUriGenerator"/> instances
/// wired directly against a running <see cref="MinioContainerFixture"/> — the construction shape
/// <c>AddSharedKernelS3Storage</c> itself uses, minus the DI container, so behavioral/round-trip
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
                ServiceURL = fixture.ServiceUrl,
                ForcePathStyle = fixture.ForcePathStyle,
                // MinIO's Testcontainers connection string is a plain http:// endpoint (no TLS) — the
                // SDK's presigned-URL generation needs UseHttp explicitly set to true or it emits an
                // https:// URL regardless of ServiceURL's own scheme, which then fails the TLS
                // handshake against MinIO's HTTP-only listener.
                UseHttp = fixture.ServiceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase),
                // Mirrors S3StorageServiceCollectionExtensions.CreateClient's own configuration exactly,
                // so these tests exercise the identical client shape a consuming host would build. Does
                // NOT fully resolve DeleteObjects against this fixture's pinned MinIO release — see
                // S3RoundTripTests' DeleteManyAsync test for the confirmed remaining limitation.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            });

    /// <summary>Creates an <see cref="S3FileStorage"/> backed by a fresh client against <paramref name="fixture"/>.</summary>
    public static S3FileStorage CreateFileStorage(MinioContainerFixture fixture) =>
        new(CreateClient(fixture), NullLogger<S3FileStorage>.Instance);

    /// <summary>Creates an <see cref="S3BlobUriGenerator"/> backed by a fresh client against <paramref name="fixture"/>.</summary>
    public static S3BlobUriGenerator CreateBlobUriGenerator(MinioContainerFixture fixture) =>
        new(CreateClient(fixture), new SystemClock(), NullLogger<S3BlobUriGenerator>.Instance);
}
