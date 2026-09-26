using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Storage.S3;
using SharedKernel.Storage.S3.Internal;

namespace SharedKernel.Storage;

/// <summary>
/// Adds named stores on one S3 connection; returned by both <c>AddS3</c> overloads of
/// <see cref="S3StorageBuilderExtensions"/>, by <see cref="S3StorageBuilderExtensions.AddS3Compatible"/> and by
/// S3-compatible providers such as <c>AddObs</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every store added here shares the connection's client; stores on the same connection copy between each other
/// server-side. The builder is also an <see cref="IStorageBuilder"/>, so another connection can be chained:
/// <c>.AddS3(configuration).AddStore("a").AddObs(configuration).AddStore("b")</c>.
/// </para>
/// <para>
/// Each store reads <c>SharedKernel:Storage:Stores:{name}</c> (<see cref="S3StoreOptions"/>) from the root
/// configuration given to the connection, then applies the <c>configure</c> delegate. The settings are validated
/// when the host starts: an invalid store fails <c>IHost.StartAsync</c> with <c>OptionsValidationException</c>
/// naming the store and setting (without a started host, the first resolution of the store throws it).
/// </para>
/// </remarks>
public sealed class S3StorageBuilder : IStorageBuilder
{
    private readonly IConfiguration _configuration;

    internal S3StorageBuilder(IServiceCollection services, string connectionName, IConfiguration configuration)
    {
        Services = services;
        ConnectionName = connectionName;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public IServiceCollection Services { get; }

    /// <summary>
    /// Gets the connection name: <c>S3</c> for the default connection, <c>Obs</c> for OBS, or the name given to a
    /// named connection. Reported as the <c>storage.provider</c> telemetry tag.
    /// </summary>
    public string ConnectionName { get; }

    /// <summary>
    /// Adds a store shared by all tenants on this connection, configured from
    /// <c>SharedKernel:Storage:Stores:{name}</c> and then <paramref name="configure"/>, and resolvable as
    /// <c>[FromKeyedServices(name)] IFileStorage</c> (or unkeyed <see cref="IFileStorage"/> when it is the only
    /// shared store).
    /// </summary>
    /// <param name="name">
    /// The store name: 1 to 64 characters from <c>A-Z a-z 0-9 . _ -</c>, starting with a letter or digit, unique
    /// ignoring case across every provider of the service.
    /// </param>
    /// <param name="configure">
    /// Sets store options in code, after configuration is bound; <see langword="null"/> for none.
    /// </param>
    /// <returns>The same builder, to add more stores.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a valid store name.</exception>
    /// <exception cref="InvalidOperationException">A store with this name is already registered.</exception>
    /// <example>
    /// <code>
    /// storage.AddS3(builder.Configuration)
    ///     .AddStore("invoices")                                                // Bucket etc. from configuration
    ///     .AddStore("exports", s => s.MaxPresignExpiry = TimeSpan.FromMinutes(15));
    /// </code>
    /// </example>
    public S3StorageBuilder AddStore(string name, Action<S3StoreOptions>? configure = null) => Add(name, tenantScoped: false, configure);

    /// <summary>
    /// Adds a tenant-scoped store on this connection, configured like <see cref="AddStore"/>: resolvable only as
    /// <c>[FromKeyedServices(name)] ITenantFileStorage</c> (or unkeyed when it is the only tenant store), whose
    /// <see cref="ITenantFileStorage.ForTenant"/> views keep every tenant under
    /// <c>{KeyPrefix}tenants/{tenantId}/</c>. Never resolvable as <see cref="IFileStorage"/>.
    /// </summary>
    /// <param name="name">
    /// The store name: 1 to 64 characters from <c>A-Z a-z 0-9 . _ -</c>, starting with a letter or digit, unique
    /// ignoring case across every provider of the service.
    /// </param>
    /// <param name="configure">
    /// Sets store options in code, after configuration is bound; <see langword="null"/> for none.
    /// </param>
    /// <returns>The same builder, to add more stores.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a valid store name.</exception>
    /// <exception cref="InvalidOperationException">A store with this name is already registered.</exception>
    /// <example>
    /// <code>
    /// storage.AddS3(builder.Configuration).AddTenantStore("documents");
    ///
    /// public sealed class Contracts([FromKeyedServices("documents")] ITenantFileStorage documents) { ... }
    /// </code>
    /// </example>
    public S3StorageBuilder AddTenantStore(string name, Action<S3StoreOptions>? configure = null) => Add(name, tenantScoped: true, configure);

    private S3StorageBuilder Add(string name, bool tenantScoped, Action<S3StoreOptions>? configure)
    {
        if (!FileStoreRegistration.IsValidStoreName(name))
        {
            throw new ArgumentException(
                $"Store name '{name}' is invalid: use 1 to 64 characters from A-Z, a-z, 0-9, '.', '_' and '-', starting with a letter or digit.",
                nameof(name));
        }

        Services.AddValidatedOptions<S3StoreOptions, S3StoreOptionsValidator>(
            _configuration.GetSection(S3StoreOptions.SectionFor(name)),
            name: name);
        if (configure is not null)
        {
            Services.Configure(name, configure);
        }

        string connectionName = ConnectionName;
        Services.AddKeyedSingleton<S3FileStorage>(
            name,
            (sp, _) => new S3FileStorage(
                name,
                sp.GetRequiredService<IOptionsMonitor<S3StoreOptions>>().Get(name),
                sp.GetRequiredKeyedService<S3Connection>(connectionName),
                sp.GetService<IClock>() ?? new SystemClock(),
                sp.GetRequiredService<ILogger<S3FileStorage>>()));

        this.AddStore(new FileStoreRegistration(
            name,
            tenantScoped,
            sp => sp.GetRequiredKeyedService<S3FileStorage>(name),
            (sp, cancellationToken) => sp.GetRequiredKeyedService<S3FileStorage>(name).ProbeAsync(cancellationToken)));

        return this;
    }
}
