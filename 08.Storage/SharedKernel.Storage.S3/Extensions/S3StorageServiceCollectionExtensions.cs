using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Storage.Abstractions.Abstractions;
using SharedKernel.Storage.S3.BlobUri;
using SharedKernel.Storage.S3.FileStorage;
using SharedKernel.Storage.S3.Options;

namespace SharedKernel.Storage.S3.Extensions;

/// <summary>
/// DI registration entry point for the AWS S3 (and MinIO) object-storage provider.
/// </summary>
public static class S3StorageServiceCollectionExtensions
{
    /// <summary>
    /// Binds and validates <see cref="S3StorageOptions"/>, registers a singleton
    /// <see cref="IAmazonS3"/> built from those options, and registers
    /// <see cref="IFileStorage"/>/<see cref="IBlobUriGenerator"/> as singletons backed by S3.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">
    /// The configuration root <see cref="S3StorageOptions"/> is bound from, at
    /// <see cref="S3StorageOptions.SectionName"/>.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <see cref="IAmazonS3"/> is thread-safe and connection-pooled — it is registered as a
    /// singleton, never scoped or transient. MinIO is served by the identical code path via
    /// <see cref="S3StorageOptions.ServiceUrl"/> + <see cref="S3StorageOptions.ForcePathStyle"/> —
    /// no MinIO-specific branch exists anywhere in this registration or in
    /// <see cref="S3FileStorage"/>/<see cref="S3BlobUriGenerator"/>.
    /// </remarks>
    public static IServiceCollection AddSharedKernelS3Storage(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<S3StorageOptions>(configuration.GetSection(S3StorageOptions.SectionName));

        services.TryAddSingleton<IClock, SystemClock>();

        services.AddSingleton<IAmazonS3>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<S3StorageOptions>>().Value;
            return CreateClient(options);
        });

        services.AddSingleton<IFileStorage, S3FileStorage>();
        services.AddSingleton<IBlobUriGenerator, S3BlobUriGenerator>();

        return services;
    }

    private static AmazonS3Client CreateClient(S3StorageOptions options)
    {
        var credentials = new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey);
        var config = new AmazonS3Config
        {
            ForcePathStyle = options.ForcePathStyle,
            // AWSSDK.S3 4.x defaults to WHEN_SUPPORTED, which eagerly attaches the newer CRC32-based
            // flexible-checksum header to every request/operation that supports one. WHEN_REQUIRED
            // narrows that to only the operations that strictly need a checksum, which is the safer,
            // more broadly-compatible default for both real AWS S3 and S3-compatible endpoints
            // (MinIO/OBS) alike. NOTE: this does not by itself guarantee compatibility with every
            // S3-compatible endpoint's DeleteObjects handling — some older MinIO releases (confirmed:
            // RELEASE.2024-01-16T16-07-38Z, the version this domain's own test fixture pins) still
            // reject DeleteObjects unconditionally without a classic Content-MD5 header, which
            // AWSSDK.S3 4.x no longer auto-computes for this operation under any
            // RequestChecksumCalculation setting available in this SDK version. See
            // SharedKernel.Storage.S3.Tests' DeleteManyAsync round-trip test for the full
            // investigation and reproduction evidence.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
        };

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.ServiceURL = options.ServiceUrl;
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }

        return new AmazonS3Client(credentials, config);
    }
}
