using System.ComponentModel;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption.BlindIndex;
using SharedKernel.Persistence.EfCore.Encryption.Configuration;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>Configures field encryption; the argument of <c>UseFieldEncryption(k =&gt; …)</c>.</summary>
/// <remarks>
/// Every choice is shared by all contexts of the service. Without a key-source call, the
/// <see cref="IEncryptionKeyProvider"/> (or <see cref="ISynchronousEncryptionKeyProvider"/>) registered in the
/// container is used; nothing needs registering twice.
/// </remarks>
public sealed class FieldEncryptionBuilder
{
    private readonly FieldEncryptionSettings _settings;

    internal FieldEncryptionBuilder(IServiceCollection services, FieldEncryptionSettings settings)
    {
        Services = services;
        _settings = settings;
    }

    /// <summary>The service collection, for extension methods.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public IServiceCollection Services { get; }

    /// <summary>
    /// Reads encryption keys from <c>SharedKernel:Persistence:Encryption:Keys</c> (<c>CurrentKeyId</c> and a base64
    /// <c>Keys</c> map). Suited to development and to keys injected from a secret store as configuration.
    /// </summary>
    /// <returns>This builder.</returns>
    public FieldEncryptionBuilder FromConfiguration()
    {
        _settings.SetKeySource(FieldEncryptionSettings.ConfigurationKeySource, static services =>
        {
            var keys = services.GetRequiredService<IOptions<EncryptionOptions>>().Value.Keys;
            if (keys.CurrentKeyId is null || keys.Keys.Count == 0)
            {
                throw new InvalidOperationException(
                    "Field encryption reads its keys from configuration, but 'SharedKernel:Persistence:Encryption:Keys' " +
                    "has no CurrentKeyId and Keys.");
            }

            return new StaticEncryptionKeyProvider(
                keys.CurrentKeyId, keys.Keys.Select(kv => new CryptographicKey(kv.Key, Convert.FromBase64String(kv.Value))));
        });
        return this;
    }

    /// <summary>Uses <typeparamref name="TProvider"/> (for example a KMS provider) as the key source.</summary>
    /// <typeparam name="TProvider">The provider; registered as a singleton unless already registered.</typeparam>
    /// <returns>This builder.</returns>
    public FieldEncryptionBuilder UseKeyProvider<TProvider>()
        where TProvider : class, IEncryptionKeyProvider
    {
        Services.TryAddSingleton<TProvider>();
        _settings.SetKeySource(typeof(TProvider).FullName!, static services => services.GetRequiredService<TProvider>());
        return this;
    }

    /// <summary>Uses the provider <paramref name="factory"/> returns as the key source.</summary>
    /// <param name="factory">Creates the provider, once.</param>
    /// <returns>This builder.</returns>
    public FieldEncryptionBuilder UseKeyProvider(Func<IServiceProvider, IEncryptionKeyProvider> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _settings.SetKeySource("factory", factory);
        return this;
    }

    /// <summary>Uses <typeparamref name="TProvider"/> for blind-index keys instead of configuration.</summary>
    /// <typeparam name="TProvider">The provider; registered as a singleton unless already registered.</typeparam>
    /// <returns>This builder.</returns>
    public FieldEncryptionBuilder UseBlindIndexKeys<TProvider>()
        where TProvider : class, IBlindIndexKeyProvider
    {
        Services.TryAddSingleton<TProvider>();
        _settings.BlindIndexKeyProviderFactory = static services => services.GetRequiredService<TProvider>();
        return this;
    }

    /// <summary>Registers a named blind-index normalizer that properties select with <c>.WithBlindIndex(normalizer: name)</c>.</summary>
    /// <typeparam name="TNormalizer">The normalizer.</typeparam>
    /// <returns>This builder.</returns>
    public FieldEncryptionBuilder AddBlindIndexNormalizer<TNormalizer>()
        where TNormalizer : class, IBlindIndexNormalizer
    {
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IBlindIndexNormalizer, TNormalizer>());
        return this;
    }

    /// <summary>
    /// Encrypts every encrypted property of a tenanted entity under its tenant's own data key, so a tenant can be
    /// erased by destroying its key (<c>ITenantEncryptionKeyManager.ShredTenantAsync</c>). Uses the registered
    /// <see cref="IEnvelopeEncryptionProvider"/> to generate and wrap the keys.
    /// </summary>
    /// <returns>This builder.</returns>
    public FieldEncryptionBuilder UseTenantDataKeys()
    {
        _settings.TenantDataKeys = true;
        return this;
    }

    /// <summary>As <see cref="UseTenantDataKeys()"/>, wrapping tenant keys with <typeparamref name="TEnvelopeProvider"/>.</summary>
    /// <typeparam name="TEnvelopeProvider">The envelope provider; registered as a singleton unless already registered.</typeparam>
    /// <returns>This builder.</returns>
    public FieldEncryptionBuilder UseTenantDataKeys<TEnvelopeProvider>()
        where TEnvelopeProvider : class, IEnvelopeEncryptionProvider
    {
        Services.TryAddSingleton<TEnvelopeProvider>();
        _settings.TenantDataKeys = true;
        _settings.EnvelopeProviderFactory = static services => services.GetRequiredService<TEnvelopeProvider>();
        return this;
    }

    /// <summary>
    /// The data source maintenance runs (key rotation, plaintext migration, tenant shredding) and tenant key reads
    /// connect with; typically a role that bypasses row-level security. Without it, maintenance uses the context's
    /// connection and tenant keys a registered <c>NpgsqlDataSource</c>.
    /// </summary>
    /// <param name="factory">Returns the data source; it is not disposed by field encryption.</param>
    /// <returns>This builder.</returns>
    public FieldEncryptionBuilder UseMaintenanceDataSource(Func<IServiceProvider, DbDataSource> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _settings.MaintenanceDataSourceFactory = factory;
        return this;
    }

    /// <summary>Adjusts <see cref="EncryptionOptions"/> after configuration binding.</summary>
    /// <param name="configure">The adjustment.</param>
    /// <returns>This builder.</returns>
    public FieldEncryptionBuilder Configure(Action<EncryptionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure(configure);
        return this;
    }
}
