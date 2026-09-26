using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption.BlindIndex;

namespace SharedKernel.Persistence.EfCore.Encryption.Configuration;

/// <summary>The choices a <see cref="FieldEncryptionBuilder"/> recorded, one instance per service collection.</summary>
internal sealed class FieldEncryptionSettings
{
    /// <summary>The <see cref="KeySource"/> of <c>FromConfiguration()</c>.</summary>
    public const string ConfigurationKeySource = "configuration";

    private Func<IServiceProvider, IEncryptionKeyProvider>? _keyProviderFactory;
    private string? _keySource;

    public bool TenantDataKeys { get; set; }

    /// <summary>The description of the chosen key source, or <see langword="null"/> for the container's provider.</summary>
    public string? KeySource => _keySource;

    /// <summary>Whether a builder call chose the key source; otherwise the one registered in the container is used.</summary>
    public bool HasExplicitKeySource => _keyProviderFactory is not null;

    public Func<IServiceProvider, IEnvelopeEncryptionProvider>? EnvelopeProviderFactory { get; set; }

    public Func<IServiceProvider, DbDataSource>? MaintenanceDataSourceFactory { get; set; }

    public Func<IServiceProvider, IBlindIndexKeyProvider>? BlindIndexKeyProviderFactory { get; set; }

    /// <summary>Resolves the blind-index key provider: the builder's choice, a registered one, else configuration.</summary>
    public IBlindIndexKeyProvider ResolveBlindIndexKeyProvider(IServiceProvider services) =>
        BlindIndexKeyProviderFactory?.Invoke(services)
        ?? services.GetService<IBlindIndexKeyProvider>()
        ?? services.GetRequiredService<ConfigurationBlindIndexKeyProvider>();

    /// <summary>Records the key source; a second, different choice is a configuration error.</summary>
    public void SetKeySource(string description, Func<IServiceProvider, IEncryptionKeyProvider> factory)
    {
        if (_keySource is not null && !string.Equals(_keySource, description, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Field encryption already uses key source '{_keySource}'; it cannot also use '{description}'. " +
                "Every DbContext in a service shares one key source.");
        }

        _keySource = description;
        _keyProviderFactory = factory;
    }

    /// <summary>Resolves the asynchronous key provider this service encrypts with.</summary>
    public IEncryptionKeyProvider ResolveKeyProvider(IServiceProvider services)
    {
        if (_keyProviderFactory is not null)
            return _keyProviderFactory(services);

        if (services.GetService<IEncryptionKeyProvider>() is { } provider)
            return provider;

        if (services.GetService<ISynchronousEncryptionKeyProvider>() is { } synchronous)
            return new SynchronousKeyProviderAdapter(synchronous);

        throw new InvalidOperationException(
            "Field encryption has no key source. Call 'UseFieldEncryption(k => k.FromConfiguration())' to read keys " +
            "from 'SharedKernel:Persistence:Encryption:Keys', 'k.UseKeyProvider<TProvider>()' for a KMS provider, or " +
            "register an 'IEncryptionKeyProvider' (for example 'AddSharedKernelCryptography(configuration)" +
            ".AddAzureKeyVaultEncryption(configuration)' from SharedKernel.Cryptography.KeyVault.Azure).");
    }

    /// <summary>Resolves the data source for side connections: the maintenance source, else a registered <see cref="NpgsqlDataSource"/>.</summary>
    public DbDataSource? ResolveSideDataSource(IServiceProvider services) =>
        MaintenanceDataSourceFactory?.Invoke(services) ?? services.GetService<NpgsqlDataSource>();

    private sealed class SynchronousKeyProviderAdapter(ISynchronousEncryptionKeyProvider inner)
        : IEncryptionKeyProvider, ISynchronousEncryptionKeyProvider
    {
        public CryptographicKey GetCurrentKey() => inner.GetCurrentKey();

        public CryptographicKey? GetKey(string keyId) => inner.GetKey(keyId);

        public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default) =>
            new(inner.GetCurrentKey());

        public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
            new(inner.GetKey(keyId));
    }
}
