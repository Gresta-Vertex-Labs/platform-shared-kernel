using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Storage.Obs;
using SharedKernel.Storage.S3;

namespace SharedKernel.Storage;

/// <summary>
/// Adds a Huawei Cloud OBS connection to SharedKernel storage, over OBS's S3-compatible API and the
/// <c>SharedKernel.Storage.S3</c> implementation.
/// </summary>
public static class ObsStorageBuilderExtensions
{
    /// <summary>
    /// The connection name <see cref="AddObs"/> registers: <c>Obs</c>; also the <c>storage.provider</c> telemetry
    /// tag of OBS stores.
    /// </summary>
    public const string ObsConnectionName = "Obs";

    /// <summary>
    /// Adds the Huawei Cloud OBS connection configured at <c>SharedKernel:Storage:Obs</c>
    /// (<see cref="ObsStorageOptions"/>). Add stores to it with <see cref="S3StorageBuilder.AddStore"/> and
    /// <see cref="S3StorageBuilder.AddTenantStore"/>; they are ordinary stores, configured at
    /// <c>SharedKernel:Storage:Stores:{name}</c> like S3 stores, and can sit beside S3 stores in the same service.
    /// </summary>
    /// <param name="builder">The storage builder from <c>AddSharedKernelStorage()</c>.</param>
    /// <param name="configuration">
    /// The root <see cref="IConfiguration"/>, not a section: the connection reads <c>SharedKernel:Storage:Obs</c>
    /// and each store <c>SharedKernel:Storage:Stores:{name}</c> from it.
    /// </param>
    /// <param name="configure">
    /// Sets connection options in code, after configuration is bound; <see langword="null"/> for none.
    /// </param>
    /// <returns>A builder to add stores to.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="configuration"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// An OBS connection is already registered; a service has at most one.
    /// </exception>
    /// <remarks>
    /// <para>
    /// OBS's S3-compatible API accepts but silently ignores <c>If-None-Match</c>, <c>If-Match</c> and
    /// <c>x-amz-checksum-sha256</c> (verified against OBS <c>tr-west-1</c>), so the connection's
    /// <see cref="S3Compatibility"/> profile switches them off: <see cref="WriteCondition"/>, conditional deletes,
    /// create-only presigned uploads and <see cref="FileUploadOptions.ChecksumSha256"/> fail with
    /// <see cref="StorageErrorCodes.NotSupported"/> instead of overwriting or storing unchecked bytes. Object tags,
    /// SSE-S3/SSE-KMS and presigned forms are supported. Because an encrypted object's ETag is not the MD5 of its
    /// content, full downloads are requested as <c>bytes=0-</c> (<see cref="S3Compatibility.ETagIsContentMd5"/> is
    /// <see langword="false"/>). The profile cannot be changed through <paramref name="configure"/>.
    /// </para>
    /// <para>
    /// Credentials are always the static AK/SK pair (optionally with a security token); there is no default
    /// credential chain. Settings are validated when the host starts (<c>OptionsValidationException</c> from
    /// <c>IHost.StartAsync</c>; without a started host, on first resolution of a store). The client is created on
    /// first use and disposed with the container.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // "SharedKernel": { "Storage": {
    /// //   "Obs": { "Endpoint": "https://obs.tr-west-1.myhuaweicloud.com",
    /// //            "AccessKeyId": "...", "SecretAccessKey": "..." },
    /// //   "Stores": { "archive": { "Bucket": "acme-archive" } } } }
    /// builder.Services.AddSharedKernelStorage()
    ///     .AddObs(builder.Configuration)
    ///     .AddStore("archive");
    /// </code>
    /// </example>
    public static S3StorageBuilder AddObs(
        this IStorageBuilder builder,
        IConfiguration configuration,
        Action<ObsStorageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        builder.Services.AddValidatedOptions<ObsStorageOptions, ObsStorageOptionsValidator>(configuration);
        if (configure is not null)
        {
            builder.Services.Configure(configure);
        }

        return builder.AddS3Compatible(
            ObsConnectionName,
            configuration,
            sp => (CreateClient(sp.GetRequiredService<IOptions<ObsStorageOptions>>().Value), CreateCompatibility()));
    }

    /// <summary>What OBS's S3-compatible API is known to support.</summary>
    internal static S3Compatibility CreateCompatibility() => new()
    {
        // Verified against OBS tr-west-1 (P-559): If-None-Match/If-Match and x-amz-checksum-sha256 are accepted and
        // silently ignored, so they are refused here instead; tags work; an encrypted object's ETag is not its MD5.
        ConditionalWrites = false,
        Sha256Checksums = false,
        ObjectTags = true,
        KmsEncryption = true,
        PresignedPost = true,
        ETagIsContentMd5 = false,
    };

    private static AmazonS3Client CreateClient(ObsStorageOptions options)
    {
        AWSCredentials credentials = string.IsNullOrWhiteSpace(options.SecurityToken)
            ? new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey)
            : new SessionAWSCredentials(options.AccessKeyId, options.SecretAccessKey, options.SecurityToken);

        return new AmazonS3Client(credentials, new AmazonS3Config
        {
            ServiceURL = options.Endpoint,
            AuthenticationRegion = ObsStorageOptionsValidator.ResolveRegion(options),
            ForcePathStyle = options.ForcePathStyle,
            MaxErrorRetry = options.MaxRetries,
            RetryMode = RequestRetryMode.Standard,
            Timeout = options.RequestTimeout,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        });
    }
}
