using System.Collections.Concurrent;
using System.Security.Cryptography;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.KeyVault.Azure.Options;
using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Cryptography.KeyVault.Azure;

/// <summary>
/// Azure Key Vault Keys implementation of <see cref="IAsymmetricKeyProvider"/> — backs
/// <see cref="RsaSignatureService"/>/<see cref="EcdsaSignatureService"/> with Azure Key Vault's
/// <b>remote</b> sign/verify operations rather than exporting private key material.
/// </summary>
/// <remarks>
/// <para>
/// <b>DELIBERATELY A SEPARATE CLASS FROM <see cref="AzureKeyVaultEncryptionKeyProvider"/>.</b>
/// Signing keys — used directly by Key Vault to produce/verify a signature — and wrap/unwrap keys
/// are a different Key Vault key <em>usage pattern</em> even when both live in the same vault
/// (Azure enforces which operations a given key's policy permits). Reuses the existing
/// <see cref="AzureKeyVaultCryptographyOptions.VaultUri"/>/<see cref="AzureKeyVaultCryptographyOptions.KeyNames"/>/
/// <see cref="AzureKeyVaultCryptographyOptions.Credential"/> — no new options type. Any entry in
/// <see cref="AzureKeyVaultCryptographyOptions.KeyNames"/> may be used as a signing
/// <c>keyId</c> — unlike <see cref="AzureKeyVaultEncryptionKeyProvider"/>, this class has no
/// "current key" concept, since <see cref="IAsymmetricSignatureService.Sign(byte[], string)"/>/
/// <see cref="IAsymmetricSignatureService.Verify(byte[], byte[], string)"/> always name the exact
/// <c>keyId</c> to use.
/// </para>
/// <para>
/// <b>Returned <see cref="RSA"/>/<see cref="ECDsa"/> instances never export private key
/// material.</b> Azure Key Vault Keys does not release raw HSM-protected key material — the
/// vendor-idiomatic operation is a <em>remote</em> sign/verify call
/// (<see cref="CryptographyClient.Sign(SignatureAlgorithm, byte[], System.Threading.CancellationToken)"/>/
/// <see cref="CryptographyClient.Verify(SignatureAlgorithm, byte[], byte[], System.Threading.CancellationToken)"/>).
/// <see cref="GetRsaKeyAsync"/>/<see cref="GetEcdsaKeyAsync"/> therefore return a thin internal
/// subclass (<see cref="KeyVaultRsaKey"/>/<see cref="KeyVaultEcdsaKey"/>) whose
/// <c>SignHash</c>/<c>VerifyHash</c> overrides — the real, overridable BCL extension points (see
/// those classes' own remarks for why, discovered during P-493's own test-double implementation) —
/// delegate to that remote call, so <see cref="RsaSignatureService"/>/<see cref="EcdsaSignatureService"/>
/// (P-493) consume this provider through the exact same contract as a local key, with <b>zero
/// changes to those two classes beyond what P-493 already introduced</b>.
/// </para>
/// <para>
/// <b>Connection reuse, baked in from day one.</b> Resolves each distinct Azure key name's
/// metadata and <see cref="CryptographyClient"/> exactly once, cached in a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by Azure key name — never re-resolved or
/// reconstructed on a per-call basis. This is the exact connection-reuse pattern P-496 has to
/// retroactively apply to the older <see cref="AzureKeyVaultEncryptionKeyProvider"/>; this class
/// bakes it in from its very first implementation instead of repeating that defect a second time.
/// A failed resolution attempt is never left permanently cached — the next call retries.
/// </para>
/// <para>
/// <b>NEVER implements <see cref="ISynchronousAsymmetricKeyProvider"/>.</b> Every call performs a
/// genuine Azure Key Vault network round trip (at minimum a key-metadata lookup on first
/// resolution of a given Azure key name; every sign/verify call is <em>always</em> a remote round
/// trip, with no local fast path). This is unlike a config/certificate-backed implementer, which
/// may genuinely never block.
/// </para>
/// <para>
/// <b>Fails closed</b> — mirrors <see cref="AzureKeyVaultEncryptionKeyProvider"/>'s contract
/// exactly. Every genuine Azure SDK exception (an unreachable vault, a
/// <see cref="RequestFailedException"/> for a permission/auth failure) propagates directly,
/// uncaught. An unrecognized <c>keyId</c> — one with no entry in
/// <see cref="AzureKeyVaultCryptographyOptions.KeyNames"/> — is detected purely locally, before
/// any Azure call, and throws <see cref="KeyNotFoundException"/> per
/// <see cref="IAsymmetricKeyProvider"/>'s documented contract.
/// </para>
/// </remarks>
public sealed class AzureKeyVaultAsymmetricKeyProvider : IAsymmetricKeyProvider
{
    private readonly KeyClient _keyClient;
    private readonly TokenCredential _credential;
    private readonly AzureKeyVaultCryptographyOptions _options;

    // Resolved exactly once per distinct Azure key name (never per call) — see class-level
    // "Connection reuse" remarks. Lazy<Task<T>> (rather than a plain ConcurrentDictionary<string,
    // CryptographyClient>) so two concurrent first-callers for the same Azure key name share one
    // in-flight resolution instead of racing two independent Azure calls; mirrors
    // AzureKeyVaultEncryptionKeyProvider's own Lazy<Task<CryptographicKey>> "current key" slot.
    private readonly ConcurrentDictionary<string, Lazy<Task<ResolvedAzureKey>>> _resolvedKeysByAzureKeyName =
        new(StringComparer.Ordinal);

    /// <summary>Creates a new <see cref="AzureKeyVaultAsymmetricKeyProvider"/>.</summary>
    /// <param name="options">The validated Azure Key Vault configuration — the same options type <see cref="AzureKeyVaultEncryptionKeyProvider"/> binds.</param>
    public AzureKeyVaultAsymmetricKeyProvider(IOptions<AzureKeyVaultCryptographyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _credential = _options.Credential ?? new DefaultAzureCredential();
        _keyClient = new KeyClient(_options.VaultUri, _credential);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Resolves <paramref name="keyId"/> to an Azure key name, validates the underlying Azure key
    /// is of type <c>RSA</c>/<c>RSA-HSM</c>, and wraps the cached (or newly-resolved)
    /// <see cref="CryptographyClient"/> in a <see cref="KeyVaultRsaKey"/>. See the class-level
    /// remarks for the connection-reuse and fail-closed contracts.
    /// </remarks>
    public async ValueTask<RSA> GetRsaKeyAsync(string keyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keyId);

        string azureKeyName = ResolveAzureKeyName(keyId);
        ResolvedAzureKey resolved = await ResolveAsync(azureKeyName, ct).ConfigureAwait(false);

        if (resolved.KeyType != KeyType.Rsa && resolved.KeyType != KeyType.RsaHsm)
        {
            throw new InvalidOperationException(
                $"Azure Key Vault key '{azureKeyName}' (keyId '{keyId}') is of type '{resolved.KeyType}', " +
                $"not RSA/RSA-HSM. {nameof(GetRsaKeyAsync)} requires an RSA or RSA-HSM key.");
        }

        return new KeyVaultRsaKey(resolved.CryptographyClient, resolved.KeySizeBits);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Resolves <paramref name="keyId"/> to an Azure key name, validates the underlying Azure key
    /// is of type <c>EC</c>/<c>EC-HSM</c>, and wraps the cached (or newly-resolved)
    /// <see cref="CryptographyClient"/> in a <see cref="KeyVaultEcdsaKey"/>. See the class-level
    /// remarks for the connection-reuse and fail-closed contracts.
    /// </remarks>
    public async ValueTask<ECDsa> GetEcdsaKeyAsync(string keyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keyId);

        string azureKeyName = ResolveAzureKeyName(keyId);
        ResolvedAzureKey resolved = await ResolveAsync(azureKeyName, ct).ConfigureAwait(false);

        if (resolved.KeyType != KeyType.Ec && resolved.KeyType != KeyType.EcHsm)
        {
            throw new InvalidOperationException(
                $"Azure Key Vault key '{azureKeyName}' (keyId '{keyId}') is of type '{resolved.KeyType}', " +
                $"not EC/EC-HSM. {nameof(GetEcdsaKeyAsync)} requires an EC or EC-HSM key.");
        }

        return new KeyVaultEcdsaKey(resolved.CryptographyClient, resolved.KeySizeBits);
    }

    private string ResolveAzureKeyName(string keyId)
    {
        if (_options.KeyNames.TryGetValue(keyId, out string? azureKeyName))
        {
            return azureKeyName;
        }

        // A local, expected failure mode per IAsymmetricKeyProvider's documented contract — never
        // an Azure call is attempted for an unrecognized keyId.
        throw new KeyNotFoundException(
            $"No Azure Key Vault key name is configured for keyId '{keyId}' " +
            $"({nameof(AzureKeyVaultCryptographyOptions)}.{nameof(AzureKeyVaultCryptographyOptions.KeyNames)}).");
    }

    private async Task<ResolvedAzureKey> ResolveAsync(string azureKeyName, CancellationToken ct)
    {
        Lazy<Task<ResolvedAzureKey>> lazy = _resolvedKeysByAzureKeyName.GetOrAdd(
            azureKeyName,
            name => new Lazy<Task<ResolvedAzureKey>>(
                () => ResolveCoreAsync(name, ct),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        catch
        {
            // Never leave a permanently-poisoned cache entry behind: remove it (only if it is
            // still the one that just failed — a concurrent successful resolution may already
            // have replaced it) so the next call retries instead of repeating a stale failure.
            _resolvedKeysByAzureKeyName.TryRemove(
                new KeyValuePair<string, Lazy<Task<ResolvedAzureKey>>>(azureKeyName, lazy));
            throw;
        }
    }

    private async Task<ResolvedAzureKey> ResolveCoreAsync(string azureKeyName, CancellationToken ct)
    {
        Response<KeyVaultKey> keyResponse = await _keyClient.GetKeyAsync(azureKeyName, version: null, ct)
            .ConfigureAwait(false);
        KeyVaultKey key = keyResponse.Value;

        int keySizeBits = ComputeKeySizeBits(key);
        var cryptographyClient = new CryptographyClient(key.Id, _credential);

        return new ResolvedAzureKey(cryptographyClient, key.KeyType, keySizeBits);
    }

    /// <summary>
    /// Derives the key size in bits from the vault key's <em>public</em> material only — never the
    /// private components — by delegating to the BCL's own size computation
    /// (<see cref="RSA.KeySize"/>/<see cref="ECDsa.KeySize"/>) rather than hand-rolling
    /// modulus-byte-length math.
    /// </summary>
    private static int ComputeKeySizeBits(KeyVaultKey key)
    {
        if (key.KeyType == KeyType.Rsa || key.KeyType == KeyType.RsaHsm)
        {
            using RSA rsa = key.Key.ToRSA(includePrivateParameters: false);
            return rsa.KeySize;
        }

        if (key.KeyType == KeyType.Ec || key.KeyType == KeyType.EcHsm)
        {
            using ECDsa ecdsa = key.Key.ToECDsa(includePrivateParameters: false);
            return ecdsa.KeySize;
        }

        throw new NotSupportedException(
            $"Azure Key Vault key type '{key.KeyType}' is not supported for remote signing. " +
            "Configure an RSA, RSA-HSM, EC, or EC-HSM key for SharedKernel.Cryptography.KeyVault.Azure's " +
            $"{nameof(AzureKeyVaultAsymmetricKeyProvider)}.");
    }

    private sealed record ResolvedAzureKey(CryptographyClient CryptographyClient, KeyType KeyType, int KeySizeBits);
}
