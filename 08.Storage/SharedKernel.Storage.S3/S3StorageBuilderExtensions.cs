using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Storage.S3;
using SharedKernel.Storage.S3.Internal;

namespace SharedKernel.Storage;

/// <summary>
/// Adds Amazon S3 and S3-compatible (MinIO, and others through <see cref="AddS3Compatible"/>) connections to
/// SharedKernel storage. Each connection is one S3 client; add stores to it with the returned
/// <see cref="S3StorageBuilder"/>.
/// </summary>
/// <remarks>
/// <para>
/// A connection's client is created when its first store is first used, shared by every store on the connection,
/// and disposed with the container. It is never registered as <c>IAmazonS3</c>, so it cannot collide with a
/// client the service registers itself.
/// </para>
/// <para>
/// Failures reach callers as <see cref="StorageErrorCodes"/> values after the SDK's own retries (standard retry
/// mode); throttling, 5xx, timeouts and network errors become <see cref="StorageErrorCodes.Unavailable"/>. Details
/// (bucket, status, S3 error code, request id) are logged through <c>ILogger</c> with EventIds 8100–8105; object
/// keys are never logged. Operations emit spans and metrics under the <c>SharedKernel.Storage</c>
/// <c>ActivitySource</c>/<c>Meter</c>.
/// </para>
/// </remarks>
public static class S3StorageBuilderExtensions
{
    /// <summary>
    /// The connection name the default <see cref="AddS3(IStorageBuilder, IConfiguration, Action{S3StorageOptions})"/>
    /// registers: <c>S3</c>. A named connection may not reuse it.
    /// </summary>
    public const string S3ConnectionName = "S3";

    /// <summary>
    /// Adds the default Amazon S3 (or MinIO) connection, named <see cref="S3ConnectionName"/> and configured at
    /// <c>SharedKernel:Storage:S3</c> (<see cref="S3StorageOptions"/>). Add stores to it with
    /// <see cref="S3StorageBuilder.AddStore"/> and <see cref="S3StorageBuilder.AddTenantStore"/>.
    /// </summary>
    /// <param name="builder">The storage builder from <c>AddSharedKernelStorage()</c>.</param>
    /// <param name="configuration">
    /// The root <see cref="IConfiguration"/>, not a section: the connection reads <c>SharedKernel:Storage:S3</c> and
    /// each store <c>SharedKernel:Storage:Stores:{name}</c> from it.
    /// </param>
    /// <param name="configure">
    /// Sets connection options in code, after configuration is bound — for example
    /// <see cref="S3StorageOptions.Compatibility"/>; <see langword="null"/> for none.
    /// </param>
    /// <returns>A builder to add stores to.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="configuration"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The default S3 connection is already registered.</exception>
    /// <remarks>
    /// The settings are validated when the host starts: a missing region, a half-configured credential or an
    /// out-of-range value fails <c>IHost.StartAsync</c> with <c>OptionsValidationException</c> (without a started
    /// host, the first resolution of a store on the connection throws it). With no <c>AccessKeyId</c>, the SDK's
    /// default credential chain is used: environment, shared profile, web identity (IRSA), EKS Pod Identity, ECS
    /// task role, EC2 instance profile.
    /// </remarks>
    /// <example>
    /// <code>
    /// // "SharedKernel": { "Storage": {
    /// //   "S3": { "Region": "eu-central-1" },
    /// //   "Stores": { "invoices": { "Bucket": "acme-invoices" }, "documents": { "Bucket": "acme-documents" } } } }
    /// builder.Services.AddSharedKernelStorage()
    ///     .AddS3(builder.Configuration)
    ///     .AddStore("invoices")
    ///     .AddTenantStore("documents");
    ///
    /// // MinIO, or any endpoint lacking a feature:
    /// builder.Services.AddSharedKernelStorage()
    ///     .AddS3(builder.Configuration, s3 => s3.Compatibility.ObjectTags = false)
    ///     .AddStore("uploads");
    /// </code>
    /// </example>
    public static S3StorageBuilder AddS3(
        this IStorageBuilder builder,
        IConfiguration configuration,
        Action<S3StorageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        return builder.AddS3Connection(
            S3ConnectionName,
            configuration,
            configuration.GetSection(S3StorageOptions.SectionName),
            Microsoft.Extensions.Options.Options.DefaultName,
            configure);
    }

    /// <summary>
    /// Adds a named S3 connection configured at <c>SharedKernel:Storage:S3:{connectionName}</c>
    /// (<see cref="S3StorageOptions"/>) — for a service whose buckets need different credentials, accounts or
    /// regions. Stores added to it share its client; stores on different connections copy by streaming.
    /// </summary>
    /// <param name="builder">The storage builder from <c>AddSharedKernelStorage()</c>.</param>
    /// <param name="configuration">
    /// The root <see cref="IConfiguration"/>, not a section: the connection reads
    /// <c>SharedKernel:Storage:S3:{connectionName}</c> and each store <c>SharedKernel:Storage:Stores:{name}</c>.
    /// </param>
    /// <param name="connectionName">
    /// The connection name: 1 to 64 characters from <c>A-Z a-z 0-9 . _ -</c>, starting with a letter or digit, and
    /// unique among connections (not <see cref="S3ConnectionName"/> when the default connection is also added).
    /// Used in the section path and as the <c>storage.provider</c> telemetry tag.
    /// </param>
    /// <param name="configure">
    /// Sets connection options in code, after configuration is bound; <see langword="null"/> for none.
    /// </param>
    /// <returns>A builder to add stores to.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> or <paramref name="configuration"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="connectionName"/> is not a valid name.</exception>
    /// <exception cref="InvalidOperationException">A connection with this name is already registered.</exception>
    /// <remarks>
    /// Validated at host startup like the default connection; validation messages name the connection.
    /// </remarks>
    /// <example>
    /// <code>
    /// // "S3": { "Public": { "Region": "eu-central-1", ... }, "Private": { "Region": "eu-central-1", ... } }
    /// IStorageBuilder storage = builder.Services.AddSharedKernelStorage();
    /// storage.AddS3(builder.Configuration, "Public").AddStore("assets");
    /// storage.AddS3(builder.Configuration, "Private").AddTenantStore("documents");
    /// </code>
    /// </example>
    public static S3StorageBuilder AddS3(
        this IStorageBuilder builder,
        IConfiguration configuration,
        string connectionName,
        Action<S3StorageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!FileStoreRegistration.IsValidStoreName(connectionName))
        {
            throw new ArgumentException(
                $"Connection name '{connectionName}' is invalid: use 1 to 64 characters from A-Z, a-z, 0-9, '.', '_' and '-'.",
                nameof(connectionName));
        }

        return builder.AddS3Connection(
            connectionName,
            configuration,
            configuration.GetSection($"{S3StorageOptions.SectionName}:{connectionName}"),
            connectionName,
            configure);
    }

    /// <summary>
    /// Adds a connection to an S3-compatible service with a client you build — for provider packages such as
    /// <c>SharedKernel.Storage.Obs</c>, or a service whose client needs settings <see cref="S3StorageOptions"/>
    /// does not offer. Stores added to it share the client.
    /// </summary>
    /// <param name="builder">The storage builder.</param>
    /// <param name="connectionName">
    /// A name unique among connections (including <see cref="S3ConnectionName"/> and <c>Obs</c>), used as the
    /// <c>storage.provider</c> telemetry tag. No configuration section is read for it.
    /// </param>
    /// <param name="configuration">
    /// The root <see cref="IConfiguration"/> stores read <c>SharedKernel:Storage:Stores:{name}</c> from.
    /// </param>
    /// <param name="clientFactory">
    /// Creates the client and states what the endpoint supports; called once, when the first store on the
    /// connection is first used. The connection owns the client and disposes it with the container, so return a
    /// new client, not one registered elsewhere. Set <c>RequestChecksumCalculation</c> and
    /// <c>ResponseChecksumValidation</c> to <c>WHEN_REQUIRED</c> for services that reject the SDK's default
    /// checksum headers.
    /// </param>
    /// <returns>A builder to add stores to.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/>, <paramref name="connectionName"/>, <paramref name="configuration"/> or
    /// <paramref name="clientFactory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="connectionName"/> is empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">A connection with this name is already registered.</exception>
    /// <example>
    /// <code>
    /// storage.AddS3Compatible("Backup", builder.Configuration, sp =>
    /// {
    ///     var client = new AmazonS3Client(credentials, new AmazonS3Config
    ///     {
    ///         ServiceURL = "https://s3.backup.example.com",
    ///         ForcePathStyle = true,
    ///         RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
    ///         ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
    ///     });
    ///     return (client, new S3Compatibility { ConditionalWrites = false });
    /// }).AddStore("backup");
    /// </code>
    /// </example>
    public static S3StorageBuilder AddS3Compatible(
        this IStorageBuilder builder,
        string connectionName,
        IConfiguration configuration,
        Func<IServiceProvider, (IAmazonS3 Client, S3Compatibility Compatibility)> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(clientFactory);

        if (builder.Services.Any(d => d.ServiceType == typeof(S3Connection) && Equals(d.ServiceKey, connectionName)))
        {
            throw new InvalidOperationException($"An S3 storage connection named '{connectionName}' is already registered.");
        }

        builder.Services.AddKeyedSingleton<S3Connection>(
            connectionName,
            (sp, _) =>
            {
                (IAmazonS3 client, S3Compatibility compatibility) = clientFactory(sp);
                return new S3Connection(connectionName, client, compatibility);
            });

        return new S3StorageBuilder(builder.Services, connectionName, configuration);
    }

    private static S3StorageBuilder AddS3Connection(
        this IStorageBuilder builder,
        string connectionName,
        IConfiguration configuration,
        IConfigurationSection section,
        string optionsName,
        Action<S3StorageOptions>? configure)
    {
        builder.Services.AddValidatedOptions<S3StorageOptions, S3StorageOptionsValidator>(section, name: optionsName);
        if (configure is not null)
        {
            builder.Services.Configure(optionsName, configure);
        }

        return builder.AddS3Compatible(
            connectionName,
            configuration,
            sp =>
            {
                S3StorageOptions options = sp.GetRequiredService<IOptionsMonitor<S3StorageOptions>>().Get(optionsName);
                return (CreateClient(options), options.Compatibility);
            });
    }

    private static AmazonS3Client CreateClient(S3StorageOptions options)
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = options.ForcePathStyle,
            MaxErrorRetry = options.MaxRetries,
            RetryMode = RequestRetryMode.Standard,
            Timeout = options.RequestTimeout,
            // Only send checksums an operation requires, or that a request asks for explicitly: several
            // S3-compatible services reject the newer default checksum headers.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.ServiceURL = options.ServiceUrl;
            if (!string.IsNullOrWhiteSpace(options.Region))
            {
                config.AuthenticationRegion = options.Region;
            }
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }

        if (string.IsNullOrWhiteSpace(options.AccessKeyId))
        {
            // Default credential chain: environment, profile, web identity (IRSA), EKS Pod Identity, ECS, EC2.
            return new AmazonS3Client(config);
        }

        AWSCredentials credentials = string.IsNullOrWhiteSpace(options.SessionToken)
            ? new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey)
            : new SessionAWSCredentials(options.AccessKeyId, options.SecretAccessKey, options.SessionToken);
        return new AmazonS3Client(credentials, config);
    }
}
