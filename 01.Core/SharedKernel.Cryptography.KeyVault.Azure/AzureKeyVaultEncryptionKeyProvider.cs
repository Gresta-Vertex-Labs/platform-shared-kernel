using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.KeyVault.Azure.Options;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Cryptography.KeyVault.Azure;

/// <summary>
/// Azure Key Vault Keys implementation of both <see cref="IEncryptionKeyProvider"/> (direct
/// key-material retrieval) and <see cref="IEnvelopeEncryptionProvider"/> (KMS-idiomatic
/// generate/wrap and unwrap).
/// </summary>
/// <remarks>
/// <para>
/// <b>DESIGN DECISION — direct retrieval is built on top of envelope wrapping, not a second,
/// independent code path.</b> Azure Key Vault Keys does not export raw HSM-protected key
/// material by default — the vendor-idiomatic operation is
/// <see cref="CryptographyClient.WrapKeyAsync(KeyWrapAlgorithm, byte[], System.Threading.CancellationToken)"/>/
/// <see cref="CryptographyClient.UnwrapKeyAsync(KeyWrapAlgorithm, byte[], System.Threading.CancellationToken)"/>,
/// which is exactly <see cref="IEnvelopeEncryptionProvider"/>'s shape. <see cref="GetCurrentKeyAsync"/>
/// therefore generates (or returns a process-lifetime-cached) local AES-256 data key via
/// <see cref="GenerateDataKeyAsync"/>, exposing only the already-in-memory plaintext data key as
/// <see cref="CryptographicKey.Material"/> — the vault's own master key material never crosses
/// the process boundary either way, whether reached via <see cref="IEncryptionKeyProvider"/> or
/// <see cref="IEnvelopeEncryptionProvider"/>. <see cref="GetKeyAsync"/> mirrors this: it decodes
/// the wrapped data key packed into the requested <c>keyId</c> string and calls
/// <see cref="UnwrapDataKeyAsync"/> internally. <b>DO NOT "fix" this into two divergent code
/// paths</b> — a second, parallel raw-key-export path is not something Azure Key Vault Keys
/// sanely supports in the general (HSM-backed) case, and maintaining two paths would let them
/// silently drift out of sync.
/// </para>
/// <para>
/// <b>How <see cref="CryptographicKey.Id"/> is constructed.</b> This provider holds no
/// persistent store of its own — it must be able to reconstruct which wrapped data key (and
/// which exact Azure key version) produced a given <see cref="CryptographicKey"/> purely from
/// the opaque <c>keyId</c> string embedded in <see cref="Symmetric.EncryptedPayload.KeyId"/>.
/// <see cref="GetCurrentKeyAsync"/> therefore packs the wrapped data key bytes and the versioned
/// Azure key identifier URI (<see cref="EnvelopeDataKey.MasterKeyId"/>) into a single
/// length-prefixed, Base64-encoded <c>keyId</c> string (mirroring
/// <c>AesGcmEncryptionService</c>'s own <c>Pack</c>/<c>TryUnpack</c> binary-encoding style) —
/// never a hand-rolled string-concatenation-with-separator scheme, which would be fragile
/// against a master key identifier URI that itself legitimately contains <c>':'</c>.
/// </para>
/// <para>
/// <b>Fail-closed.</b> Every genuine Azure SDK exception (an unreachable vault, a
/// <see cref="RequestFailedException"/> for a permission/auth failure, or the vault itself
/// rejecting a wrapped key as tampered) propagates directly from every member of this class —
/// there is no code path that catches an Azure SDK exception and substitutes a silent fallback.
/// The one narrow exception is <see cref="UnwrapDataKeyAsync"/>'s <see cref="Result{T}"/>
/// failure path: it is returned only for a <c>masterKeyId</c> that fails <em>local</em>
/// well-formedness validation, before any call ever reaches Azure — see that method's docs.
/// </para>
/// <para>
/// <b>Ships zero caching of its own</b> beyond the single process-lifetime "current data key"
/// slot required to keep <see cref="CryptographicKey.Id"/> stable across calls (see above) — it
/// never re-resolves an already-unwrapped historical key or applies a bounded TTL. A consumer
/// wanting bounded-TTL caching composes <c>SharedKernel.Cryptography</c>'s
/// <c>CachedEncryptionKeyProvider</c> externally: two independent caches with different TTL
/// semantics must never both wrap the same provider instance.
/// </para>
/// <para>
/// <b>Also implements <see cref="IEncryptionKeyProviderProbe"/>.</b> <see cref="ProbeAsync"/>
/// resolves only the current key's metadata (<see cref="KeyClient.GetKeyAsync(string, string?, System.Threading.CancellationToken)"/>)
/// — the exact same read-only call <see cref="GenerateDataKeyAsync"/> makes before it ever wraps
/// anything — and never reaches <see cref="CryptographyClient.WrapKeyAsync(KeyWrapAlgorithm, byte[], System.Threading.CancellationToken)"/>
/// or any other cryptographic operation. Unlike every other member of this class, it never lets an
/// Azure SDK exception propagate: see <see cref="ProbeAsync"/>'s own remarks for why this one
/// member is a deliberate, narrow exception to this class's fail-closed-via-exception contract.
/// </para>
/// </remarks>
public sealed class AzureKeyVaultEncryptionKeyProvider :
    IEncryptionKeyProvider, IEnvelopeEncryptionProvider, IEncryptionKeyProviderProbe
{
    // AES-256 data key size.
    private const int DataKeySizeBytes = 32;

    private readonly KeyClient _keyClient;
    private readonly TokenCredential _credential;
    private readonly AzureKeyVaultCryptographyOptions _options;
    private readonly ISecureRandomGenerator _secureRandomGenerator;

    // Process-lifetime cache for the single "current" data key — NOT a bounded-TTL/refreshing
    // cache (see class docs "Ships zero caching of its own"); this exists solely so
    // CryptographicKey.Id stays stable across repeated GetCurrentKeyAsync calls instead of
    // wrapping a brand-new data key (and therefore minting a new Id) on every single Encrypt
    // call. A failed generation attempt clears the slot so the next call retries.
    private Lazy<Task<CryptographicKey>>? _currentKeySlot;

    /// <summary>Creates a new <see cref="AzureKeyVaultEncryptionKeyProvider"/>.</summary>
    /// <param name="options">The validated Azure Key Vault configuration.</param>
    /// <param name="secureRandomGenerator">
    /// Generates the plaintext AES-256 data key material locally, before it is wrapped by the
    /// vault — never <see cref="System.Random"/> or a hand-rolled call to
    /// <see cref="System.Security.Cryptography.RandomNumberGenerator"/>, per this platform's
    /// single-sanctioned-source-of-randomness rule.
    /// </param>
    public AzureKeyVaultEncryptionKeyProvider(
        IOptions<AzureKeyVaultCryptographyOptions> options,
        ISecureRandomGenerator secureRandomGenerator)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(secureRandomGenerator);

        _options = options.Value;
        _secureRandomGenerator = secureRandomGenerator;
        _credential = _options.Credential ?? new DefaultAzureCredential();
        _keyClient = new KeyClient(_options.VaultUri, _credential);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Built atop <see cref="GenerateDataKeyAsync"/> — see the class-level "DESIGN DECISION"
    /// remarks. The generated data key is cached for the lifetime of this instance so repeated
    /// calls return the same <see cref="CryptographicKey.Id"/>; a failed generation attempt is
    /// never left permanently poisoned — the next call retries.
    /// </remarks>
    public async ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default)
    {
        Lazy<Task<CryptographicKey>> slot = LazyInitializer.EnsureInitialized(
            ref _currentKeySlot,
            () => new Lazy<Task<CryptographicKey>>(
                () => GenerateCurrentKeyCoreAsync(ct),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await slot.Value.ConfigureAwait(false);
        }
        catch
        {
            // Never leave a permanently-poisoned process-lifetime slot behind: clear it (only if
            // it is still the one that just failed — a concurrent successful call may already
            // have replaced it) so the next call attempts a fresh generation.
            Interlocked.CompareExchange(ref _currentKeySlot, null, slot);
            throw;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Decodes the wrapped data key packed into <paramref name="keyId"/> and unwraps it via
    /// <see cref="UnwrapDataKeyAsync"/> — see the class-level "DESIGN DECISION" remarks. Returns
    /// <see langword="null"/> (never throws) both when <paramref name="keyId"/> was not produced
    /// by this provider (unrecognized format) and when the local well-formedness check inside
    /// <see cref="UnwrapDataKeyAsync"/> fails, per <see cref="IEncryptionKeyProvider.GetKeyAsync"/>'s
    /// "retired or unknown" contract. A genuine Azure SDK exception (unreachable vault,
    /// permission denied, a tampered wrapped key rejected by the vault itself) still propagates
    /// directly, uncaught.
    /// </remarks>
    public async ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keyId);

        if (!TryDecodeKeyId(keyId, out string masterKeyId, out byte[]? wrappedKey))
        {
            return null;
        }

        Result<byte[]> unwrapped = await UnwrapDataKeyAsync(wrappedKey, masterKeyId, ct).ConfigureAwait(false);
        return unwrapped.IsSuccess ? new CryptographicKey(keyId, unwrapped.Value) : null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Generates <see cref="DataKeySizeBytes"/> bytes of AES-256 key material locally via the
    /// injected <see cref="ISecureRandomGenerator"/>, then asks
    /// <see cref="AzureKeyVaultCryptographyOptions.CurrentKeyId"/>'s vault-side master key to
    /// wrap it via <see cref="CryptographyClient.WrapKeyAsync(KeyWrapAlgorithm, byte[], System.Threading.CancellationToken)"/>.
    /// Azure Key Vault Keys exposes no single "generate a data key server-side" operation
    /// equivalent to (e.g.) AWS KMS's <c>GenerateDataKey</c> that works uniformly across both
    /// standard-vault and Managed-HSM key types, so local generation + server-side wrap is the
    /// portable, KMS-boundary-respecting equivalent: the master key material never leaves the
    /// vault, only the freshly-generated data key is exposed to it (to be wrapped) and returns.
    /// The wrap algorithm is derived from the target key's <see cref="KeyType"/> — RSA keys use
    /// <see cref="KeyWrapAlgorithm.RsaOaep256"/>, symmetric (<c>oct</c>) keys use
    /// <see cref="KeyWrapAlgorithm.A256KW"/>; an EC key is rejected with
    /// <see cref="NotSupportedException"/> since Key Vault does not support key-wrap operations
    /// on EC keys at all — this is a configuration error, not a data/tamper issue, so it is
    /// thrown rather than returned as a <see cref="Result{T}"/> failure.
    /// </remarks>
    public async ValueTask<EnvelopeDataKey> GenerateDataKeyAsync(CancellationToken ct = default)
    {
        string azureKeyName = ResolveAzureKeyName(_options.CurrentKeyId!);

        Response<KeyVaultKey> keyResponse = await _keyClient.GetKeyAsync(azureKeyName, version: null, ct)
            .ConfigureAwait(false);
        KeyVaultKey key = keyResponse.Value;
        KeyWrapAlgorithm algorithm = ResolveWrapAlgorithm(key.KeyType);

        byte[] plaintextKey = _secureRandomGenerator.NextBytes(DataKeySizeBytes);

        var cryptographyClient = new CryptographyClient(key.Id, _credential);
        WrapResult wrapResult = await cryptographyClient.WrapKeyAsync(algorithm, plaintextKey, ct).ConfigureAwait(false);

        return new EnvelopeDataKey(plaintextKey, wrapResult.EncryptedKey, key.Id.ToString());
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <paramref name="masterKeyId"/> must be a versioned Azure Key Vault key identifier URI of
    /// the shape <c>https://{vault}.vault.azure.net/keys/{name}/{version}</c> — exactly what
    /// <see cref="GenerateDataKeyAsync"/> returns as <see cref="EnvelopeDataKey.MasterKeyId"/>.
    /// If it is not well-formed, this method returns a <see cref="Result{T}"/> failure
    /// (<see cref="AzureKeyVaultCryptographyErrorCodes.MalformedMasterKeyId"/>) WITHOUT ever
    /// calling Azure — this is the one narrow case where a problem with the input surfaces as a
    /// <see cref="Result{T}"/> failure instead of a thrown exception.
    /// </para>
    /// <para>
    /// Once the URI is confirmed well-formed, every subsequent step is a real Azure SDK call
    /// (resolving the key's <see cref="KeyType"/> to pick the wrap algorithm, then
    /// <see cref="CryptographyClient.UnwrapKeyAsync(KeyWrapAlgorithm, byte[], System.Threading.CancellationToken)"/>
    /// itself) — any exception either throws, including the vault rejecting
    /// <paramref name="wrappedDataKey"/> as tampered or wrapped by a different key, propagates
    /// directly and is never caught or converted to a <see cref="Result{T}"/> failure here. This
    /// is a deliberate, narrow reading of "fail-closed": Azure Key Vault itself is the only
    /// party that can determine whether a wrapped key blob is authentic, so its rejection is
    /// treated exactly like every other Azure SDK failure in this class (unreachable vault,
    /// permission denied) — a thrown exception, never silently downgraded to a soft
    /// <see cref="Result{T}"/> failure.
    /// </para>
    /// </remarks>
    public async ValueTask<Result<byte[]>> UnwrapDataKeyAsync(
        byte[] wrappedDataKey,
        string masterKeyId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(wrappedDataKey);
        ArgumentNullException.ThrowIfNull(masterKeyId);

        if (!Uri.TryCreate(masterKeyId, UriKind.Absolute, out Uri? keyUri) ||
            !TryParseKeyVaultKeyIdUri(keyUri, out string name, out string version))
        {
            return Error.Unexpected(
                AzureKeyVaultCryptographyErrorCodes.MalformedMasterKeyId,
                $"'{masterKeyId}' is not a recognized Azure Key Vault key identifier URI " +
                "of the shape https://{vault}/keys/{name}/{version}.");
        }

        // Everything below is a real Azure SDK call — any exception it throws propagates
        // directly (fail-closed), never caught or converted to a Result failure. See remarks.
        Response<KeyVaultKey> keyResponse = await _keyClient.GetKeyAsync(name, version, ct).ConfigureAwait(false);
        KeyWrapAlgorithm algorithm = ResolveWrapAlgorithm(keyResponse.Value.KeyType);

        var cryptographyClient = new CryptographyClient(keyUri, _credential);
        UnwrapResult unwrapResult = await cryptographyClient.UnwrapKeyAsync(algorithm, wrappedDataKey, ct)
            .ConfigureAwait(false);

        return unwrapResult.Key;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Performs exactly one Azure SDK call — a read-only key-metadata lookup for
    /// <see cref="AzureKeyVaultCryptographyOptions.CurrentKeyId"/>'s configured Azure key name,
    /// the same call <see cref="GenerateDataKeyAsync"/> makes before it ever wraps anything. This
    /// is deliberately never a wrap/unwrap/sign/verify operation: those register as genuine key
    /// usage in Key Vault's own audit trail, which a readiness probe must not generate as a side
    /// effect.
    /// </para>
    /// <para>
    /// <b>Never throws for an ordinary reachability failure</b> — every Azure SDK exception other
    /// than <see cref="OperationCanceledException"/> is caught here and reported as
    /// <see cref="EncryptionKeyProviderHealth.IsHealthy"/> <see langword="false"/> with
    /// <see cref="EncryptionKeyProviderHealth.Description"/> set from the exception's message.
    /// This is the one deliberate, narrow exception to this class's otherwise-universal
    /// fail-closed-via-exception contract (see the class-level "Fail-closed" remarks) — a
    /// readiness probe's purpose is to report status to a health-check pipeline, not to gate a
    /// cryptographic operation.
    /// </para>
    /// </remarks>
    public async Task<EncryptionKeyProviderHealth> ProbeAsync(CancellationToken ct = default)
    {
        try
        {
            string azureKeyName = ResolveAzureKeyName(_options.CurrentKeyId!);
            await _keyClient.GetKeyAsync(azureKeyName, version: null, ct).ConfigureAwait(false);
            return new EncryptionKeyProviderHealth(IsHealthy: true, Description: null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new EncryptionKeyProviderHealth(IsHealthy: false, Description: exception.Message);
        }
    }

    private async Task<CryptographicKey> GenerateCurrentKeyCoreAsync(CancellationToken ct)
    {
        EnvelopeDataKey dataKey = await GenerateDataKeyAsync(ct).ConfigureAwait(false);
        string id = EncodeKeyId(dataKey.MasterKeyId, dataKey.WrappedKey);
        return new CryptographicKey(id, dataKey.PlaintextKey);
    }

    private string ResolveAzureKeyName(string keyId)
    {
        if (_options.KeyNames.TryGetValue(keyId, out string? azureKeyName))
        {
            return azureKeyName;
        }

        // Startup validation (AzureKeyVaultCryptographyOptionsValidator) already guarantees
        // CurrentKeyId names an entry present in KeyNames — reaching here means the options
        // instance was constructed/mutated after that validation ran (e.g. hot-reload with no
        // re-validation), which is itself a configuration bug worth failing loudly on.
        throw new InvalidOperationException(
            $"No Azure Key Vault key name is configured for keyId '{keyId}' " +
            $"({nameof(AzureKeyVaultCryptographyOptions)}.{nameof(AzureKeyVaultCryptographyOptions.KeyNames)}).");
    }

    private static KeyWrapAlgorithm ResolveWrapAlgorithm(KeyType keyType)
    {
        if (keyType == KeyType.Rsa || keyType == KeyType.RsaHsm)
        {
            return KeyWrapAlgorithm.RsaOaep256;
        }

        if (keyType == KeyType.Oct || keyType == KeyType.OctHsm)
        {
            return KeyWrapAlgorithm.A256KW;
        }

        throw new NotSupportedException(
            $"Azure Key Vault key type '{keyType}' does not support key-wrap operations. " +
            "Configure an RSA or symmetric (oct) key for SharedKernel.Cryptography.KeyVault.Azure.");
    }

    /// <summary>
    /// Parses an Azure Key Vault key identifier URI's <c>/keys/{name}/{version}</c> path shape.
    /// </summary>
    private static bool TryParseKeyVaultKeyIdUri(Uri keyUri, out string name, out string version)
    {
        string[] segments = keyUri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length == 3 && string.Equals(segments[0], "keys", StringComparison.Ordinal) &&
            segments[1].Length > 0 && segments[2].Length > 0)
        {
            name = segments[1];
            version = segments[2];
            return true;
        }

        name = string.Empty;
        version = string.Empty;
        return false;
    }

    /// <summary>
    /// Packs <paramref name="masterKeyId"/> (length-prefixed UTF-8) followed by
    /// <paramref name="wrappedKey"/> (the remainder of the buffer) into a single Base64 string —
    /// mirrors <c>AesGcmEncryptionService</c>'s own <c>Pack</c> binary-encoding style rather than
    /// a fragile separator-character scheme (a master key identifier URI legitimately contains
    /// <c>':'</c> and <c>/</c>).
    /// </summary>
    private static string EncodeKeyId(string masterKeyId, byte[] wrappedKey)
    {
        byte[] masterKeyIdBytes = Encoding.UTF8.GetBytes(masterKeyId);
        byte[] buffer = new byte[4 + masterKeyIdBytes.Length + wrappedKey.Length];
        Span<byte> span = buffer;

        BinaryPrimitives.WriteInt32BigEndian(span[..4], masterKeyIdBytes.Length);
        masterKeyIdBytes.CopyTo(span[4..]);
        wrappedKey.CopyTo(span[(4 + masterKeyIdBytes.Length)..]);

        return Convert.ToBase64String(buffer);
    }

    private static bool TryDecodeKeyId(
        string keyId,
        out string masterKeyId,
        [NotNullWhen(true)] out byte[]? wrappedKey)
    {
        masterKeyId = string.Empty;
        wrappedKey = null;

        byte[] buffer;
        try
        {
            buffer = Convert.FromBase64String(keyId);
        }
        catch (FormatException)
        {
            return false;
        }

        if (buffer.Length < 4)
        {
            return false;
        }

        ReadOnlySpan<byte> span = buffer;
        int masterKeyIdLength = BinaryPrimitives.ReadInt32BigEndian(span[..4]);
        if (masterKeyIdLength < 0 || 4 + masterKeyIdLength > buffer.Length)
        {
            return false;
        }

        masterKeyId = Encoding.UTF8.GetString(span.Slice(4, masterKeyIdLength));
        wrappedKey = span[(4 + masterKeyIdLength)..].ToArray();
        return true;
    }
}
