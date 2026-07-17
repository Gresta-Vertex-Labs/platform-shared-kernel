using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Storage.Abstractions.Abstractions;
using SharedKernel.Storage.Obs.BlobUri;
using SharedKernel.Storage.Obs.FileStorage;
using SharedKernel.Storage.Obs.Options;

namespace SharedKernel.Storage.Obs.Extensions;

/// <summary>
/// DI registration entry point for the Huawei Cloud OBS object-storage provider.
/// </summary>
public static class ObsStorageServiceCollectionExtensions
{
    /// <summary>
    /// Binds and validates <see cref="ObsStorageOptions"/>, registers a singleton
    /// <see cref="IAmazonS3"/> pointed at the OBS S3-compatible endpoint, and registers
    /// <see cref="IFileStorage"/>/<see cref="IBlobUriGenerator"/> as singletons backed by OBS.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">
    /// The configuration root <see cref="ObsStorageOptions"/> is bound from, at
    /// <see cref="ObsStorageOptions.SectionName"/>.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="IAmazonS3"/> is thread-safe and connection-pooled — it is registered as a
    /// singleton, never scoped or transient.
    /// </para>
    /// <para>
    /// When a consuming service registers both <c>AddSharedKernelS3Storage</c> and this method, the
    /// last <see cref="IFileStorage"/>/<see cref="IBlobUriGenerator"/> registration wins for the
    /// default (unkeyed) resolve. Use <c>AddKeyedSingleton</c> to register both providers side by
    /// side without a resolution collision — see <c>08.Storage/CLAUDE.md</c> for the documented
    /// keyed-DI pattern.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelObsStorage(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<ObsStorageOptions>(configuration.GetSection(ObsStorageOptions.SectionName));

        services.TryAddSingleton<IClock, SystemClock>();

        services.AddSingleton<IAmazonS3>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<ObsStorageOptions>>().Value;
            return CreateClient(options);
        });

        services.AddSingleton<IFileStorage, ObsFileStorage>();
        services.AddSingleton<IBlobUriGenerator, ObsBlobUriGenerator>();

        return services;
    }

    private static AmazonS3Client CreateClient(ObsStorageOptions options)
    {
        var credentials = new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey);
        var config = new AmazonS3Config
        {
            ServiceURL = options.Endpoint,
            ForcePathStyle = options.ForcePathStyle,
        };

        return new AmazonS3Client(credentials, config);
    }
}
