using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Cryptography.Internal;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Health;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Cryptography.KeyVault.Azure;

/// <summary>
/// Encryption keys backed by Azure Key Vault: AES-256 data keys, wrapped by a Key Vault master key and stored as
/// versions of one Key Vault secret.
/// </summary>
/// <remarks>
/// <para>
/// <b>Data keys.</b> Each version of <see cref="AzureKeyVaultEncryptionOptions.DataKeySecretName"/> holds one data
/// key, wrapped by the master key, and its version id is the key id written into every payload. The newest enabled
/// version is the current key, so every replica encrypts with the same key. <see cref="RotateDataKeyAsync"/> adds a
/// version; two concurrent rotations add two versions and lose nothing. Disabling a version retires its key.
/// </para>
/// <para>
/// <b>Caching.</b> The list of versions is read at most once per
/// <see cref="AzureKeyVaultEncryptionOptions.RefreshInterval"/>, and each data key is unwrapped once and kept in
/// memory. A key id that is not a known enabled version returns <see langword="null"/>; it can force a fresh list at
/// most once every ten seconds, so ids from forged payloads cannot drive Key Vault traffic.
/// </para>
/// <para>
/// <b>Envelope encryption.</b> As an <see cref="IEnvelopeEncryptionProvider"/> it wraps a fresh data key per call and
/// unwraps only with <see cref="AzureKeyVaultEncryptionOptions.MasterKeyName"/> or
/// <see cref="AzureKeyVaultEncryptionOptions.PreviousMasterKeyNames"/>, and only with an enabled version of those
/// keys; any other master key id fails without a cryptographic Key Vault call. Key versions are listed at most once
/// per <see cref="AzureKeyVaultEncryptionOptions.RefreshInterval"/>, with the same rate-limited refresh as data keys. With an RSA master key, anyone holding its public key can wrap a data key; see
/// <see cref="IEnvelopeEncryptionProvider"/>.
/// </para>
/// <para>
/// <b>Failures.</b> An unreachable vault, missing permission or corrupt registry throws. The readiness probe is the
/// exception: it reports those as unhealthy.
/// </para>
/// </remarks>
public sealed class AzureKeyVaultEncryptionKeyProvider : IEncryptionKeyProvider, IEnvelopeEncryptionProvider, IReadinessProbe
{
    private const int DataKeySize = 32;
    private static readonly TimeSpan UnknownKeyRefreshFloor = TimeSpan.FromSeconds(10);

    private readonly AzureKeyVaultEncryptionOptions _options;
    private readonly KeyClient _keyClient;
    private readonly SecretClient _secretClient;
    private readonly ISecureRandomGenerator _random;
    private readonly TimeProvider _timeProvider;
    private readonly HashSet<string> _allowedMasterKeyNames;
    private readonly SingleFlightCache<bool, RegistrySnapshot> _registry;
    private readonly SingleFlightCache<string, CryptographicKey> _dataKeys;
    private readonly SingleFlightCache<string, MasterKey> _masterKeys;
    private readonly SingleFlightCache<string, VersionList> _masterKeyVersions;
    private readonly ConcurrentDictionary<string, long> _lastForcedRefreshTicks = new(StringComparer.Ordinal);

    /// <summary>Creates the provider.</summary>
    /// <param name="options">The encryption settings.</param>
    /// <param name="keyClient">A client for the vault holding the master key.</param>
    /// <param name="secretClient">A client for the vault holding the data key secret.</param>
    /// <param name="random">Generates data keys.</param>
    /// <param name="timeProvider">The clock used for cache expiry.</param>
    public AzureKeyVaultEncryptionKeyProvider(
        IOptions<AzureKeyVaultEncryptionOptions> options,
        KeyClient keyClient,
        SecretClient secretClient,
        ISecureRandomGenerator random,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(keyClient);
        ArgumentNullException.ThrowIfNull(secretClient);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _options = options.Value;
        ArgumentException.ThrowIfNullOrEmpty(_options.MasterKeyName, "options.MasterKeyName");
        ArgumentException.ThrowIfNullOrEmpty(_options.DataKeySecretName, "options.DataKeySecretName");

        _keyClient = keyClient;
        _secretClient = secretClient;
        _random = random;
        _timeProvider = timeProvider;
        _allowedMasterKeyNames = new HashSet<string>([_options.MasterKeyName, .. _options.PreviousMasterKeyNames ?? []], StringComparer.Ordinal);
        _registry = new SingleFlightCache<bool, RegistrySnapshot>(timeProvider, _options.RefreshInterval, maxEntries: 1);
        _masterKeyVersions = new SingleFlightCache<string, VersionList>(
            timeProvider,
            _options.RefreshInterval,
            maxEntries: _allowedMasterKeyNames.Count,
            comparer: StringComparer.Ordinal);
        _dataKeys = new SingleFlightCache<string, CryptographicKey>(timeProvider, timeToLive: null, maxEntries: 1024, comparer: StringComparer.Ordinal);
        _masterKeys = new SingleFlightCache<string, MasterKey>(timeProvider, _options.RefreshInterval, maxEntries: 64, comparer: StringComparer.Ordinal);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">No data key exists yet; call <see cref="RotateDataKeyAsync"/> once.</exception>
    public async ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default)
    {
        RegistrySnapshot snapshot = await GetRegistryAsync(cancellationToken).ConfigureAwait(false);
        string version = snapshot.CurrentVersion ?? throw new InvalidOperationException(
            $"The Key Vault secret '{_options.DataKeySecretName}' has no enabled data key version. " +
            $"Call {nameof(RotateDataKeyAsync)} once when provisioning.");

        return await GetDataKeyAsync(version, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyId);

        if (!KeyVaultNames.IsVersion(keyId))
        {
            return null;
        }

        RegistrySnapshot snapshot = await GetRegistryAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.EnabledVersions.Contains(keyId) && TryClaimForcedRefresh("\0data-keys", snapshot.LoadedAt))
        {
            _registry.Remove(true);
            snapshot = await GetRegistryAsync(cancellationToken).ConfigureAwait(false);
        }

        return snapshot.EnabledVersions.Contains(keyId)
            ? await GetDataKeyAsync(keyId, cancellationToken).ConfigureAwait(false)
            : null;
    }

    /// <summary>
    /// Creates a data key, stores it wrapped as a new version of the data key secret, and makes it the current key.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The new key id (the secret version).</returns>
    /// <remarks>
    /// Other replicas start using the new key within <see cref="AzureKeyVaultEncryptionOptions.RefreshInterval"/>.
    /// Existing keys stay available for decryption until their versions are disabled.
    /// </remarks>
    public async ValueTask<string> RotateDataKeyAsync(CancellationToken cancellationToken = default)
    {
        using EnvelopeDataKey dataKey = await GenerateDataKeyAsync(cancellationToken).ConfigureAwait(false);

        string json = JsonSerializer.Serialize(
            new DataKeyRecord(dataKey.MasterKeyId, Convert.ToBase64String(dataKey.WrappedKey)),
            DataKeyRecordContext.Default.DataKeyRecord);

        var secret = new KeyVaultSecret(_options.DataKeySecretName, json);
        secret.Properties.ContentType = "application/json";
        Response<KeyVaultSecret> stored = await _secretClient.SetSecretAsync(secret, cancellationToken).ConfigureAwait(false);

        string version = stored.Value.Properties.Version;
        _dataKeys.Set(version, new CryptographicKey(version, dataKey.PlaintextKey));
        _registry.Remove(true);
        return version;
    }

    /// <inheritdoc />
    public async ValueTask<EnvelopeDataKey> GenerateDataKeyAsync(CancellationToken cancellationToken = default)
    {
        MasterKey master = await GetMasterKeyAsync(_options.MasterKeyName!, version: null, cancellationToken).ConfigureAwait(false);

        byte[] plaintext = _random.GetBytes(DataKeySize);
        try
        {
            WrapResult wrapped = await master.Client.WrapKeyAsync(master.Algorithm, plaintext, cancellationToken).ConfigureAwait(false);
            return new EnvelopeDataKey(plaintext, wrapped.EncryptedKey, master.Id);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <inheritdoc />
    public async ValueTask<Result<byte[]>> UnwrapDataKeyAsync(
        ReadOnlyMemory<byte> wrappedKey,
        string masterKeyId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(masterKeyId);

        int separator = masterKeyId.IndexOf('/', StringComparison.Ordinal);
        string name = separator < 0 ? string.Empty : masterKeyId[..separator];
        string version = separator < 0 ? string.Empty : masterKeyId[(separator + 1)..];
        if (!_allowedMasterKeyNames.Contains(name) || !KeyVaultNames.IsVersion(version) || wrappedKey.IsEmpty)
        {
            return UnwrapFailed();
        }

        VersionList versions = await GetMasterKeyVersionsAsync(name, cancellationToken).ConfigureAwait(false);
        if (!versions.Enabled.Contains(version) && TryClaimForcedRefresh(name, versions.LoadedAt))
        {
            _masterKeyVersions.Remove(name);
            versions = await GetMasterKeyVersionsAsync(name, cancellationToken).ConfigureAwait(false);
        }

        if (!versions.Enabled.Contains(version))
        {
            return UnwrapFailed();
        }

        try
        {
            MasterKey master = await GetMasterKeyAsync(name, version, cancellationToken).ConfigureAwait(false);
            UnwrapResult unwrapped = await master.Client
                .UnwrapKeyAsync(master.Algorithm, wrappedKey.ToArray(), cancellationToken)
                .ConfigureAwait(false);
            return unwrapped.Key;
        }
        catch (RequestFailedException exception) when (exception.Status is 400 or 404)
        {
            // 400: the wrapped key was altered or wrapped by another key. 404: the version was deleted since listing.
            return UnwrapFailed();
        }
    }

    /// <summary>
    /// The <see cref="IReadinessProbe.Name"/> of this provider's readiness probe, registered by
    /// <c>AddAzureKeyVaultEncryption</c>.
    /// </summary>
    public const string ReadinessProbeName = "encryption-key-provider";

    /// <inheritdoc />
    public string Name => ReadinessProbeName;

    /// <summary>
    /// Checks that the master key can be read — a metadata read only, never a wrap, unwrap, sign or verify.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the check.</param>
    /// <returns>
    /// A healthy report, or an unhealthy one naming the Key Vault status code or exception type — never the
    /// exception message, which can contain request details.
    /// </returns>
    /// <exception cref="OperationCanceledException">The check was canceled.</exception>
    public async Task<ReadinessReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _keyClient.GetKeyAsync(_options.MasterKeyName, version: null, cancellationToken).ConfigureAwait(false);
            return ReadinessReport.Healthy();
        }
        catch (RequestFailedException exception)
        {
            return ReadinessReport.Unhealthy($"Azure Key Vault returned status {exception.Status} for the master key.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ReadinessReport.Unhealthy($"Azure Key Vault could not be reached ({exception.GetType().Name}).");
        }
    }

    private ValueTask<RegistrySnapshot> GetRegistryAsync(CancellationToken cancellationToken) =>
        _registry.GetOrAddAsync(true, LoadRegistryAsync, cancellationToken);

    private async ValueTask<RegistrySnapshot> LoadRegistryAsync(CancellationToken cancellationToken)
    {
        var enabled = new HashSet<string>(StringComparer.Ordinal);
        SecretProperties? newest = null;

        try
        {
            await foreach (SecretProperties properties in _secretClient
                .GetPropertiesOfSecretVersionsAsync(_options.DataKeySecretName, cancellationToken)
                .ConfigureAwait(false))
            {
                if (properties.Enabled == false || properties.Version is null)
                {
                    continue;
                }

                enabled.Add(properties.Version);
                if (newest is null
                    || properties.CreatedOn > newest.CreatedOn
                    || (properties.CreatedOn == newest.CreatedOn && string.CompareOrdinal(properties.Version, newest.Version) > 0))
                {
                    newest = properties;
                }
            }
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            // No version has been created yet.
        }

        return new RegistrySnapshot(enabled, newest?.Version, _timeProvider.GetUtcNow());
    }

    private ValueTask<VersionList> GetMasterKeyVersionsAsync(string name, CancellationToken cancellationToken) =>
        _masterKeyVersions.GetOrAddAsync(name, token => LoadMasterKeyVersionsAsync(name, token), cancellationToken);

    private async ValueTask<VersionList> LoadMasterKeyVersionsAsync(string name, CancellationToken cancellationToken)
    {
        var enabled = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            await foreach (KeyProperties properties in _keyClient
                .GetPropertiesOfKeyVersionsAsync(name, cancellationToken)
                .ConfigureAwait(false))
            {
                if (properties.Enabled != false && properties.Version is not null)
                {
                    enabled.Add(properties.Version);
                }
            }
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            // The key does not exist: no version is usable.
        }

        return new VersionList(enabled, _timeProvider.GetUtcNow());
    }

    private ValueTask<CryptographicKey> GetDataKeyAsync(string version, CancellationToken cancellationToken) =>
        _dataKeys.GetOrAddAsync(version, token => LoadDataKeyAsync(version, token), cancellationToken);

    private async ValueTask<CryptographicKey> LoadDataKeyAsync(string version, CancellationToken cancellationToken)
    {
        Response<KeyVaultSecret> secret = await _secretClient
            .GetSecretAsync(_options.DataKeySecretName, version, cancellationToken)
            .ConfigureAwait(false);

        DataKeyRecord? record;
        byte[] wrapped;
        try
        {
            record = JsonSerializer.Deserialize(secret.Value.Value, DataKeyRecordContext.Default.DataKeyRecord);
            wrapped = Convert.FromBase64String(record?.WrappedKey ?? string.Empty);
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            throw CorruptRegistry(version, exception);
        }

        if (record?.MasterKeyId is null)
        {
            throw CorruptRegistry(version, null);
        }

        Result<byte[]> unwrapped = await UnwrapDataKeyAsync(wrapped, record.MasterKeyId, cancellationToken).ConfigureAwait(false);
        if (unwrapped.IsFailure || unwrapped.Value.Length != DataKeySize)
        {
            throw CorruptRegistry(version, null);
        }

        try
        {
            return new CryptographicKey(version, unwrapped.Value);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(unwrapped.Value);
        }
    }

    private ValueTask<MasterKey> GetMasterKeyAsync(string name, string? version, CancellationToken cancellationToken) =>
        _masterKeys.GetOrAddAsync($"{name}/{version}", token => LoadMasterKeyAsync(name, version, token), cancellationToken);

    private async ValueTask<MasterKey> LoadMasterKeyAsync(string name, string? version, CancellationToken cancellationToken)
    {
        Response<KeyVaultKey> response = await _keyClient.GetKeyAsync(name, version, cancellationToken).ConfigureAwait(false);
        KeyVaultKey key = response.Value;

        KeyWrapAlgorithm algorithm = key.KeyType == KeyType.Rsa || key.KeyType == KeyType.RsaHsm
            ? KeyWrapAlgorithm.RsaOaep256
            : key.KeyType == KeyType.Oct || key.KeyType == KeyType.OctHsm
                ? KeyWrapAlgorithm.A256KW
                : throw new NotSupportedException(
                    $"The Key Vault key '{name}' is of type {key.KeyType}; wrapping requires an RSA or AES (oct) key.");

        string resolvedVersion = key.Properties.Version;
        return new MasterKey($"{name}/{resolvedVersion}", _keyClient.GetCryptographyClient(name, resolvedVersion), algorithm);
    }

    /// <summary>
    /// Allows a list to be reloaded early for an unknown version, at most once per ten seconds per list and never for a
    /// list loaded within the last ten seconds, so untrusted ids cannot drive Key Vault traffic.
    /// </summary>
    private bool TryClaimForcedRefresh(string list, DateTimeOffset loadedAt)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (now - loadedAt < UnknownKeyRefreshFloor)
        {
            return false;
        }

        long nowTicks = now.UtcTicks;
        while (true)
        {
            if (!_lastForcedRefreshTicks.TryGetValue(list, out long last))
            {
                if (_lastForcedRefreshTicks.TryAdd(list, nowTicks))
                {
                    return true;
                }

                continue;
            }

            if (nowTicks - last < UnknownKeyRefreshFloor.Ticks)
            {
                return false;
            }

            if (_lastForcedRefreshTicks.TryUpdate(list, nowTicks, last))
            {
                return true;
            }
        }
    }

    private InvalidOperationException CorruptRegistry(string version, Exception? inner) => new(
        $"Version '{version}' of the Key Vault secret '{_options.DataKeySecretName}' does not hold a valid wrapped data key.",
        inner);

    private static Error UnwrapFailed() => Error.Validation(
        CryptographyErrorCodes.DataKeyUnwrapFailed,
        "The data key could not be unwrapped with a configured master key.");

    private sealed record RegistrySnapshot(HashSet<string> EnabledVersions, string? CurrentVersion, DateTimeOffset LoadedAt);

    private sealed record VersionList(HashSet<string> Enabled, DateTimeOffset LoadedAt);

    private sealed record MasterKey(string Id, CryptographyClient Client, KeyWrapAlgorithm Algorithm);
}

/// <summary>The JSON stored in each version of the data key secret.</summary>
internal sealed record DataKeyRecord(
    [property: JsonPropertyName("masterKeyId")] string MasterKeyId,
    [property: JsonPropertyName("wrappedKey")] string WrappedKey);

[JsonSerializable(typeof(DataKeyRecord))]
internal sealed partial class DataKeyRecordContext : JsonSerializerContext;
