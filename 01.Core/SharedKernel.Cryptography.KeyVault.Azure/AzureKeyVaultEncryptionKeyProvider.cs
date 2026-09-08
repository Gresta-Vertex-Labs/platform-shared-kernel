using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Azure.Security.KeyVault.Secrets;
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
/// and <see cref="GetKeyAsync"/> are both built atop <see cref="GenerateDataKeyAsync"/>/
/// <see cref="UnwrapDataKeyAsync"/> — the vault's own master key material never crosses the
/// process boundary either way, whether reached via <see cref="IEncryptionKeyProvider"/> or
/// <see cref="IEnvelopeEncryptionProvider"/>. <b>DO NOT "fix" this into two divergent code
/// paths</b> — a second, parallel raw-key-export path is not something Azure Key Vault Keys
/// sanely supports in the general (HSM-backed) case, and maintaining two paths would let them
/// silently drift out of sync.
/// </para>
/// <para>
/// <b>P-496/WO-081 — a durable, shared, deliberately-minted "current version" registry, not a
/// per-process accident.</b> Prior to this phase, <see cref="GetCurrentKeyAsync"/>
/// process-lifetime-cached a single locally-generated data key the first time it was called on a
/// given process — meaning every process/pod/replica of a service silently minted its <em>own</em>
/// unique AES-256 data key on first use, with no sharing across replicas and no genuine rotation
/// intent. "Current key" was never actually a shared, stable concept — just a per-process
/// accident. This is now replaced with a durable, <b>Key-Vault-Secrets-backed</b> version
/// registry: every version this provider ever mints (via <see cref="MintNewVersionAsync"/>) is
/// stored as its own Key Vault Secret (named <c>"{sanitized CurrentKeyId}-data-key-{tag}"</c>,
/// tag shaped <c>"v1"</c>, <c>"v2"</c>, …), holding the wrapped (never plaintext) data key plus
/// the short master-key identifier that wrapped it (<see cref="VersionSecretPayload"/>). A single
/// additional secret (<c>"{sanitized CurrentKeyId}-current-version"</c>) holds the tag that is
/// currently "current" — every replica of a service sharing the same vault and the same
/// <see cref="AzureKeyVaultCryptographyOptions.CurrentKeyId"/> reads the SAME pointer, so
/// <see cref="GetCurrentKeyAsync"/> genuinely converges to one shared version across replicas
/// instead of minting N independent ones. <see cref="CryptographicKey.Id"/> is now a short opaque
/// version tag rather than the previous self-decodable ~470-byte envelope — see
/// <see cref="GetKeyAsync"/>'s remarks for how a pre-P-496 envelope-shaped <c>keyId</c> still
/// resolves.
/// </para>
/// <para>
/// <b><see cref="GetCurrentKeyAsync"/> deliberately never auto-mints a version on first use.</b>
/// Doing so would silently reintroduce the exact per-process-accident behavior this phase exists
/// to eliminate (two replicas racing to independently "discover" there is no current version yet
/// and each minting their own). Minting is a deliberate, explicit, operator-triggered act — call
/// <see cref="MintNewVersionAsync"/> once during initial provisioning, before the first encrypt.
/// If no version has ever been minted, <see cref="GetCurrentKeyAsync"/> throws
/// <see cref="InvalidOperationException"/> rather than guessing.
/// </para>
/// <para>
/// <b>Per-tag plaintext memoization — <see cref="GetKeyAsync"/>'s own stated acceptance
/// criterion.</b> Once a version tag has been resolved to its plaintext data key once (via
/// <see cref="GetCurrentKeyAsync"/> or <see cref="GetKeyAsync"/>), that plaintext key is memoized
/// in-memory (<c>ConcurrentDictionary&lt;string, byte[]&gt;</c>-shaped) for the remainder of this
/// instance's lifetime — a second decrypt citing an already-seen tag costs zero further Key Vault
/// calls. This is what actually bounds a wrapping <c>CachedEncryptionKeyProvider</c>'s working
/// set now: every replica converges on the same small, deliberately-minted set of live version
/// tags, rather than accumulating one entry per pod restart over a service's entire operational
/// history (the old behavior).
/// </para>
/// <para>
/// <b>Connection reuse.</b> A <see cref="CryptographyClient"/> is resolved at most once per
/// distinct (Azure key name, key version) pair — never reconstructed inside a per-call code path
/// — cached in a <see cref="ConcurrentDictionary{TKey,TValue}"/>, mirroring the exact
/// <see cref="Lazy{T}"/>-per-entry concurrency-safe pattern <see cref="AzureKeyVaultAsymmetricKeyProvider"/>
/// (P-494) already established. <b>A necessary refinement of that pattern, not a literal copy:</b>
/// <see cref="AzureKeyVaultAsymmetricKeyProvider"/> caches purely by (unversioned) Azure key name
/// because it has no "historical version" concept — every signing call always wants whichever
/// version was current when first resolved. This provider's <see cref="UnwrapDataKeyAsync"/> has
/// the opposite requirement: a data key minted months ago may have been wrapped under an Azure
/// master key version that has since been superseded by an out-of-band Azure-side key rotation,
/// and unwrapping it correctly requires a <see cref="CryptographyClient"/> pinned to that EXACT
/// historical version, not whatever is "current" today. Keying the cache purely by Azure key name
/// (discarding the version) would silently collide two genuinely different key versions onto one
/// cache slot and break historical-version unwrap the first time the underlying Azure key is ever
/// rotated. This provider therefore keys its cache by the resolved (name, version) pair — which
/// coincides with "Azure key name" in the common case where the underlying Azure key has never
/// been rotated, and only diverges (correctly) once it has.
/// </para>
/// <para>
/// <b>Backward-read compatibility (mandatory, additive/MINOR repack — not a breaking change).</b>
/// <see cref="GetKeyAsync"/> first checks whether <c>keyId</c> matches the new short version-tag
/// shape (<c>"v{N}"</c>); if not, it falls back to parsing the legacy self-decodable envelope
/// shape the pre-P-496 <see cref="AzureKeyVaultEncryptionKeyProvider"/> produced, so any row
/// already encrypted under the old shape remains decryptable indefinitely — no forced data
/// migration. <see cref="GetCurrentKeyAsync"/> (used only for new encryption going forward)
/// always resolves through the new short-tag registry. No public member's signature changes.
/// </para>
/// <para>
/// <b>Fail-closed.</b> Every genuine Azure SDK exception (an unreachable vault, a
/// <see cref="RequestFailedException"/> for a permission/auth failure, or the vault itself
/// rejecting a wrapped key as tampered) propagates directly from every member of this class —
/// there is no code path that catches an Azure SDK exception and substitutes a silent fallback.
/// The narrow exceptions are: <see cref="UnwrapDataKeyAsync"/>'s <see cref="Result{T}"/> failure
/// path for a locally-malformed <c>masterKeyId</c> (before any Azure call — see that method's
/// docs), and <see cref="GetKeyAsync"/> translating a Key Vault "secret not found" (HTTP 404) into
/// <see langword="null"/> per its documented "retired or unknown" contract — every other Azure SDK
/// failure (auth, unreachable vault, a non-404 error) still propagates as a thrown exception even
/// from inside <see cref="GetKeyAsync"/>.
/// </para>
/// <para>
/// <b>Also implements <see cref="IEncryptionKeyProviderProbe"/>.</b> <see cref="ProbeAsync"/>
/// resolves only the current key's metadata (<see cref="KeyClient.GetKeyAsync(string, string?, System.Threading.CancellationToken)"/>)
/// — the exact same read-only call <see cref="GenerateDataKeyAsync"/> makes before it ever wraps
/// anything — and never reaches <see cref="CryptographyClient.WrapKeyAsync(KeyWrapAlgorithm, byte[], System.Threading.CancellationToken)"/>,
/// the Key Vault Secrets registry, or any other operation. Unlike every other member of this
/// class, it never lets an Azure SDK exception propagate: see <see cref="ProbeAsync"/>'s own
/// remarks for why this one member is a deliberate, narrow exception to this class's fail-closed
/// contract.
/// </para>
/// <para>
/// <b><see cref="MintNewVersionAsync"/> provides no cross-caller mutual exclusion.</b> It is a
/// deliberate, explicitly-callable operational surface — NOT part of
/// <see cref="IEncryptionKeyProvider"/>/<see cref="IEnvelopeEncryptionProvider"/> — meant to be
/// invoked rarely (an ops script, a hosted job, or a future <c>19.Scheduling</c> job), never a hot
/// path or an automatic/policy-driven schedule (deliberately out of scope for this phase — see
/// this method's own remarks). Two genuinely concurrent callers can race on the shared "current
/// version" pointer secret; this is an accepted limitation given the intended low-frequency,
/// human/ops-triggered usage pattern — a deployment that needs strict mutual exclusion across
/// concurrent minters should serialize invocations externally (e.g. via a deployment pipeline
/// lock). No data is ever lost by such a race: every minted version's own secret remains
/// resolvable via <see cref="GetKeyAsync"/> regardless of which mint "won" the pointer.
/// </para>
/// </remarks>
public sealed class AzureKeyVaultEncryptionKeyProvider :
    IEncryptionKeyProvider, IEnvelopeEncryptionProvider, IEncryptionKeyProviderProbe
{
    // AES-256 data key size.
    private const int DataKeySizeBytes = 32;

    private readonly KeyClient _keyClient;
    private readonly SecretClient _secretClient;
    private readonly TokenCredential _credential;
    private readonly AzureKeyVaultCryptographyOptions _options;
    private readonly ISecureRandomGenerator _secureRandomGenerator;
    private readonly Func<Uri, TokenCredential, CryptographyClient> _cryptographyClientFactory;

    // Connection-reuse cache: a CryptographyClient (plus the resolved KeyType, needed to pick the
    // wrap algorithm) is resolved at most once per distinct cache key — never per call. Cache key
    // is either the bare (unversioned) Azure key name (used by GenerateDataKeyAsync, which always
    // wants "whichever version is current"), or "{azureKeyName}/{version}" (used by
    // UnwrapDataKeyAsync, which must pin to the exact historical version that wrapped a given data
    // key). These two key shapes never collide: Azure Key Vault key names cannot contain '/'. See
    // class-level "Connection reuse" remarks for why this must key on more than just the
    // unversioned Azure key name, unlike AzureKeyVaultAsymmetricKeyProvider's own cache.
    private readonly ConcurrentDictionary<string, Lazy<Task<ResolvedAzureKey>>> _resolvedKeysByCacheKey =
        new(StringComparer.Ordinal);

    // Per-tag plaintext data-key memoization — see class-level "Per-tag plaintext memoization"
    // remarks. A failed resolution attempt is never left permanently cached; the next call
    // retries.
    private readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> _plaintextKeysByTag =
        new(StringComparer.Ordinal);

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
        : this(options, secureRandomGenerator, keyClient: null, secretClient: null, cryptographyClientFactory: null)
    {
    }

    /// <summary>
    /// Test-only seam enabling call-counting verification of this class's connection-reuse and
    /// durable-registry behavior (P-496/WO-081, T-73) without a reachable Azure Key Vault. NEVER
    /// used by production DI wiring (<see cref="Extensions.AzureKeyVaultCryptographyServiceCollectionExtensions.AddSharedKernelAzureKeyVaultCryptography"/>
    /// always resolves the public two-parameter constructor above — this overload's extra
    /// parameters have no registered DI service to resolve from, so the container's
    /// constructor-selection can never pick this one by accident). A <see langword="null"/>
    /// argument for any of the three test-seam parameters falls back to the real Azure SDK
    /// client/factory, exactly matching the public constructor's behavior.
    /// </summary>
    internal AzureKeyVaultEncryptionKeyProvider(
        IOptions<AzureKeyVaultCryptographyOptions> options,
        ISecureRandomGenerator secureRandomGenerator,
        KeyClient? keyClient,
        SecretClient? secretClient,
        Func<Uri, TokenCredential, CryptographyClient>? cryptographyClientFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(secureRandomGenerator);

        _options = options.Value;
        _secureRandomGenerator = secureRandomGenerator;
        _credential = _options.Credential ?? new DefaultAzureCredential();
        _keyClient = keyClient ?? new KeyClient(_options.VaultUri, _credential);
        _secretClient = secretClient ?? new SecretClient(_options.VaultUri, _credential);
        _cryptographyClientFactory = cryptographyClientFactory ?? ((keyId, credential) => new CryptographyClient(keyId, credential));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Reads the shared "current version" pointer secret (always live — this class ships zero
    /// caching of "which tag is current" itself; a consumer wanting bounded-TTL caching composes
    /// <c>SharedKernel.Cryptography</c>'s <c>CachedEncryptionKeyProvider</c> externally), then
    /// resolves that tag's plaintext data key through the same memoized path
    /// <see cref="GetKeyAsync"/> uses. Throws <see cref="InvalidOperationException"/> if no
    /// version has ever been minted — see the class-level remarks for why this deliberately never
    /// auto-mints one.
    /// </remarks>
    public async ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default)
    {
        string? currentTag = await TryReadCurrentVersionPointerAsync(ct).ConfigureAwait(false);
        if (currentTag is null)
        {
            throw new InvalidOperationException(
                "No current data-key version has been minted yet for this provider's configured " +
                $"CurrentKeyId ('{_options.CurrentKeyId}'). Call {nameof(MintNewVersionAsync)} once " +
                "during initial provisioning, before encrypting anything. GetCurrentKeyAsync " +
                "deliberately never auto-mints a version on first use — doing so would reintroduce " +
                "the exact per-process 'current key' accident this provider was redesigned to " +
                "eliminate (see this type's class-level remarks).");
        }

        byte[] plaintextKey = await GetOrAddMemoizedPlaintextKeyAsync(currentTag, ct).ConfigureAwait(false);
        return new CryptographicKey(currentTag, plaintextKey);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// If <paramref name="keyId"/> matches the new short version-tag shape (<c>"v{N}"</c>), it is
    /// resolved through the durable Key Vault Secrets registry (memoized — see class-level
    /// remarks), returning <see langword="null"/> for a syntactically tag-shaped but genuinely
    /// unminted tag (a Key Vault "secret not found" translated to <see langword="null"/>, never
    /// thrown) per this method's documented "retired or unknown" contract.
    /// </para>
    /// <para>
    /// Otherwise, <paramref name="keyId"/> is assumed to be a pre-P-496 self-decodable envelope
    /// (length-prefixed Base64 master-key-id + wrapped-key blob) and decoded via the legacy path,
    /// then unwrapped via <see cref="UnwrapDataKeyAsync"/> — see the class-level "Backward-read
    /// compatibility" remarks. Returns <see langword="null"/> (never throws) when
    /// <paramref name="keyId"/> matches neither shape. A genuine Azure SDK exception (unreachable
    /// vault, permission denied, a tampered wrapped key rejected by the vault itself, or any
    /// non-404 failure) still propagates directly, uncaught.
    /// </para>
    /// </remarks>
    public async ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keyId);

        if (TryParseVersionNumber(keyId, out _))
        {
            try
            {
                byte[] plaintextKey = await GetOrAddMemoizedPlaintextKeyAsync(keyId, ct).ConfigureAwait(false);
                return new CryptographicKey(keyId, plaintextKey);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        // Legacy fallback: pre-P-496 self-decodable envelope shape.
        if (!TryDecodeLegacyKeyId(keyId, out string legacyMasterKeyId, out byte[]? wrappedKey))
        {
            return null;
        }

        Result<byte[]> unwrapped = await UnwrapDataKeyAsync(wrappedKey, legacyMasterKeyId, ct).ConfigureAwait(false);
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
    /// thrown rather than returned as a <see cref="Result{T}"/> failure. The resolved Azure key
    /// metadata (and its <see cref="CryptographyClient"/>) is cached by Azure key name — see
    /// class-level "Connection reuse" remarks.
    /// </remarks>
    public async ValueTask<EnvelopeDataKey> GenerateDataKeyAsync(CancellationToken ct = default)
    {
        string azureKeyName = ResolveAzureKeyName(_options.CurrentKeyId!);
        ResolvedAzureKey resolved = await ResolveAsync(azureKeyName, azureKeyName, version: null, ct).ConfigureAwait(false);
        KeyWrapAlgorithm algorithm = ResolveWrapAlgorithm(resolved.KeyType);

        byte[] plaintextKey = _secureRandomGenerator.NextBytes(DataKeySizeBytes);
        WrapResult wrapResult = await resolved.CryptographyClient.WrapKeyAsync(algorithm, plaintextKey, ct).ConfigureAwait(false);

        string shortMasterKeyId = BuildShortMasterKeyId(azureKeyName, resolved.KeyId);
        return new EnvelopeDataKey(plaintextKey, wrapResult.EncryptedKey, shortMasterKeyId);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <paramref name="masterKeyId"/> is normally the real, short <c>"{azureKeyName}/{version}"</c>
    /// identifier <see cref="GenerateDataKeyAsync"/> now produces (D-83/P-496) — since this
    /// provider's caller already supplies <paramref name="wrappedDataKey"/> directly (never
    /// round-tripped through this provider's own durable store for the envelope-wrap path), it
    /// never needed the oversized self-decodable envelope shape at all. For defensive robustness
    /// (and at negligible cost) this method ALSO still accepts the legacy pre-P-496 full Key Vault
    /// key identifier URI shape — the exact shape <see cref="GetKeyAsync"/>'s legacy fallback
    /// path decodes from an old envelope-shaped <c>keyId</c>. If <paramref name="masterKeyId"/>
    /// matches neither shape, this method returns a <see cref="Result{T}"/> failure
    /// (<see cref="AzureKeyVaultCryptographyErrorCodes.MalformedMasterKeyId"/>) WITHOUT ever
    /// calling Azure — this is the one narrow case where a problem with the input surfaces as a
    /// <see cref="Result{T}"/> failure instead of a thrown exception.
    /// </para>
    /// <para>
    /// Once <paramref name="masterKeyId"/> is confirmed well-formed, every subsequent step is a
    /// real Azure SDK call (resolving — or reusing a cached resolution of — the key's
    /// <see cref="KeyType"/> to pick the wrap algorithm, then
    /// <see cref="CryptographyClient.UnwrapKeyAsync(KeyWrapAlgorithm, byte[], System.Threading.CancellationToken)"/>
    /// itself) — any exception it throws, including the vault rejecting
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

        if (!TryParseMasterKeyId(masterKeyId, out string azureKeyName, out string version))
        {
            return Error.Unexpected(
                AzureKeyVaultCryptographyErrorCodes.MalformedMasterKeyId,
                $"'{masterKeyId}' is not a recognized Azure Key Vault master key identifier — " +
                "expected either the short 'name/version' shape or a full key identifier URI of " +
                "the shape https://{vault}/keys/{name}/{version}.");
        }

        // Everything below is a real Azure SDK call (or a cached resolution of one) — any
        // exception it throws propagates directly (fail-closed), never caught or converted to a
        // Result failure. See remarks.
        string cacheKey = $"{azureKeyName}/{version}";
        ResolvedAzureKey resolved = await ResolveAsync(cacheKey, azureKeyName, version, ct).ConfigureAwait(false);
        KeyWrapAlgorithm algorithm = ResolveWrapAlgorithm(resolved.KeyType);

        UnwrapResult unwrapResult = await resolved.CryptographyClient.UnwrapKeyAsync(algorithm, wrappedDataKey, ct)
            .ConfigureAwait(false);

        return unwrapResult.Key;
    }

    /// <summary>
    /// Mints a fresh data-key version, wraps it under the current Azure master key, stores it as a
    /// new Key Vault Secret, and atomically repoints the shared "current version" pointer secret
    /// to it — the real, explicitly-callable rotation story this provider previously lacked.
    /// </summary>
    /// <param name="ct">A token to observe while minting the new version.</param>
    /// <returns>The newly-minted version's short tag (e.g. <c>"v4"</c>).</returns>
    /// <remarks>
    /// <para>
    /// Every previously-minted version's secret is left untouched and remains resolvable via
    /// <see cref="GetKeyAsync"/> indefinitely after this call — this method never deletes or
    /// overwrites an existing version's secret, only ever creates a new one and repoints the
    /// pointer.
    /// </para>
    /// <para>
    /// <b>Deliberately NOT part of <see cref="IEncryptionKeyProvider"/> or
    /// <see cref="IEnvelopeEncryptionProvider"/></b> — a provider-specific operational surface,
    /// mirroring <c>06.Persistence</c>'s <c>IEncryptionRotationJob</c> precedent of leaving
    /// scheduling to the caller. <b>Automatic or policy-driven (crypto-period-enforced) rotation
    /// scheduling is explicitly OUT OF SCOPE for this method</b> — it makes rotation possible and
    /// cheap to call, never automatic. Invoke it from an ops script, a hosted job, or a future
    /// <c>19.Scheduling</c> job.
    /// </para>
    /// <para>
    /// See the class-level "MintNewVersionAsync provides no cross-caller mutual exclusion" remarks
    /// for this method's accepted concurrent-invocation limitation.
    /// </para>
    /// </remarks>
    public async ValueTask<string> MintNewVersionAsync(CancellationToken ct = default)
    {
        EnvelopeDataKey dataKey = await GenerateDataKeyAsync(ct).ConfigureAwait(false);
        string? currentTag = await TryReadCurrentVersionPointerAsync(ct).ConfigureAwait(false);
        string newTag = NextVersionTag(currentTag);

        var payload = new VersionSecretPayload(dataKey.MasterKeyId, Convert.ToBase64String(dataKey.WrappedKey));
        string payloadJson = JsonSerializer.Serialize(payload, AzureKeyVaultJsonSerializerContext.Default.VersionSecretPayload);

        await _secretClient.SetSecretAsync(VersionSecretName(newTag), payloadJson, ct).ConfigureAwait(false);
        await _secretClient.SetSecretAsync(CurrentVersionPointerSecretName(), newTag, ct).ConfigureAwait(false);

        // Memoize locally: this replica already knows the plaintext key it just minted — no
        // reason to force a round trip back through Key Vault Secrets to re-learn it.
        _plaintextKeysByTag[newTag] = new Lazy<Task<byte[]>>(Task.FromResult(dataKey.PlaintextKey));

        return newTag;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Performs exactly one Azure SDK call — a read-only key-metadata lookup for
    /// <see cref="AzureKeyVaultCryptographyOptions.CurrentKeyId"/>'s configured Azure key name.
    /// This is deliberately never a wrap/unwrap/sign/verify operation, nor a Key Vault Secrets
    /// call: those register as genuine key usage / an extra dependency in Key Vault's own audit
    /// trail, which a readiness probe must not generate as a side effect.
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

    private Task<byte[]> GetOrAddMemoizedPlaintextKeyAsync(string tag, CancellationToken ct)
    {
        Lazy<Task<byte[]>> lazy = _plaintextKeysByTag.GetOrAdd(
            tag,
            t => new Lazy<Task<byte[]>>(
                () => ResolvePlaintextKeyForTagCoreAsync(t, ct),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return AwaitAndUncacheOnFailureAsync(tag, lazy);
    }

    private async Task<byte[]> AwaitAndUncacheOnFailureAsync(string tag, Lazy<Task<byte[]>> lazy)
    {
        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        catch
        {
            // Never leave a permanently-poisoned cache entry behind: remove it (only if it is
            // still the one that just failed — a concurrent successful resolution may already
            // have replaced it) so the next call retries instead of repeating a stale failure.
            _plaintextKeysByTag.TryRemove(new KeyValuePair<string, Lazy<Task<byte[]>>>(tag, lazy));
            throw;
        }
    }

    private async Task<byte[]> ResolvePlaintextKeyForTagCoreAsync(string tag, CancellationToken ct)
    {
        Response<KeyVaultSecret> secretResponse = await _secretClient
            .GetSecretAsync(VersionSecretName(tag), version: null, ct)
            .ConfigureAwait(false);

        VersionSecretPayload payload = JsonSerializer.Deserialize(
            secretResponse.Value.Value,
            AzureKeyVaultJsonSerializerContext.Default.VersionSecretPayload)!;

        byte[] wrappedKey = Convert.FromBase64String(payload.WrappedKeyBase64);
        Result<byte[]> unwrapped = await UnwrapDataKeyAsync(wrappedKey, payload.MasterKeyId, ct).ConfigureAwait(false);

        if (unwrapped.IsFailure)
        {
            // The masterKeyId stored alongside this tag came from this provider's own earlier
            // MintNewVersionAsync call, so a local well-formedness failure here indicates the
            // durable registry itself is corrupted, not a caller input problem — throw rather
            // than translate to a Result failure or a null.
            throw new InvalidOperationException(
                $"Version '{tag}''s stored master key id is malformed: {unwrapped.Error.Message}");
        }

        return unwrapped.Value;
    }

    private async Task<string?> TryReadCurrentVersionPointerAsync(CancellationToken ct)
    {
        try
        {
            Response<KeyVaultSecret> response = await _secretClient
                .GetSecretAsync(CurrentVersionPointerSecretName(), version: null, ct)
                .ConfigureAwait(false);
            return response.Value.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task<ResolvedAzureKey> ResolveAsync(string cacheKey, string azureKeyName, string? version, CancellationToken ct)
    {
        Lazy<Task<ResolvedAzureKey>> lazy = _resolvedKeysByCacheKey.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<ResolvedAzureKey>>(
                () => ResolveCoreAsync(azureKeyName, version, ct),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        catch
        {
            // Never leave a permanently-poisoned cache entry behind: remove it (only if it is
            // still the one that just failed — a concurrent successful resolution may already
            // have replaced it) so the next call retries.
            _resolvedKeysByCacheKey.TryRemove(new KeyValuePair<string, Lazy<Task<ResolvedAzureKey>>>(cacheKey, lazy));
            throw;
        }
    }

    private async Task<ResolvedAzureKey> ResolveCoreAsync(string azureKeyName, string? version, CancellationToken ct)
    {
        Response<KeyVaultKey> keyResponse = await _keyClient.GetKeyAsync(azureKeyName, version, ct).ConfigureAwait(false);
        KeyVaultKey key = keyResponse.Value;
        CryptographyClient cryptographyClient = _cryptographyClientFactory(key.Id, _credential);

        return new ResolvedAzureKey(cryptographyClient, key.KeyType, key.Id);
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
    /// Builds the short <c>"{azureKeyName}/{version}"</c> master-key identifier (D-83) from the
    /// resolved Azure key's full identifier URI.
    /// </summary>
    private static string BuildShortMasterKeyId(string azureKeyName, Uri resolvedKeyId)
    {
        string[] segments = resolvedKeyId.AbsolutePath.Trim('/').Split('/');
        string version = segments.Length == 3 ? segments[2] : string.Empty;
        return $"{azureKeyName}/{version}";
    }

    /// <summary>
    /// Parses a <c>masterKeyId</c> in either the new short <c>"{azureKeyName}/{version}"</c> shape
    /// or the legacy full Key Vault key identifier URI shape — see <see cref="UnwrapDataKeyAsync"/>'s
    /// remarks for why both are accepted.
    /// </summary>
    private static bool TryParseMasterKeyId(string masterKeyId, out string azureKeyName, out string version)
    {
        if (Uri.TryCreate(masterKeyId, UriKind.Absolute, out Uri? absoluteUri))
        {
            return TryParseKeyVaultKeyIdUri(absoluteUri, out azureKeyName, out version);
        }

        string[] segments = masterKeyId.Split('/');
        if (segments.Length == 2 && segments[0].Length > 0 && segments[1].Length > 0)
        {
            azureKeyName = segments[0];
            version = segments[1];
            return true;
        }

        azureKeyName = string.Empty;
        version = string.Empty;
        return false;
    }

    /// <summary>
    /// Parses an Azure Key Vault key identifier URI's <c>/keys/{name}/{version}</c> path shape —
    /// the legacy (pre-P-496) <c>masterKeyId</c> shape.
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
    /// Matches the new short version-tag shape (<c>"v{N}"</c>, <c>N &gt;= 1</c>, no leading
    /// zeroes) that <see cref="NextVersionTag"/> generates.
    /// </summary>
    private static bool TryParseVersionNumber(string tag, out int number)
    {
        if (tag.Length > 1 && tag[0] == 'v' && tag[1] != '0' &&
            int.TryParse(tag.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out number) &&
            number > 0)
        {
            return true;
        }

        number = 0;
        return false;
    }

    private static string NextVersionTag(string? currentTag)
    {
        int next = currentTag is not null && TryParseVersionNumber(currentTag, out int n) ? n + 1 : 1;
        return $"v{next.ToString(CultureInfo.InvariantCulture)}";
    }

    private string CurrentVersionPointerSecretName() =>
        $"{SanitizeForSecretName(_options.CurrentKeyId!)}-current-version";

    private string VersionSecretName(string tag) =>
        $"{SanitizeForSecretName(_options.CurrentKeyId!)}-data-key-{tag}";

    /// <summary>
    /// Sanitizes an arbitrary <c>CurrentKeyId</c> value into a Key Vault-legal secret name
    /// (Azure Key Vault secret names must match <c>^[0-9a-zA-Z-]+$</c>), replacing any
    /// disallowed character with <c>'-'</c> — never assumes the caller-configured
    /// <see cref="AzureKeyVaultCryptographyOptions.CurrentKeyId"/> already satisfies that
    /// character set.
    /// </summary>
    private static string SanitizeForSecretName(string value)
    {
        char[] buffer = new char[value.Length];
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            buffer[i] = char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '-';
        }

        return new string(buffer);
    }

    /// <summary>
    /// Decodes the pre-P-496 self-decodable envelope shape: a length-prefixed UTF-8
    /// <c>masterKeyId</c> followed by the raw wrapped-key bytes, Base64-encoded as a whole. Kept
    /// solely for <see cref="GetKeyAsync"/>'s legacy fallback path — never produced for new keys.
    /// </summary>
    private static bool TryDecodeLegacyKeyId(
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

    private sealed record ResolvedAzureKey(CryptographyClient CryptographyClient, KeyType KeyType, Uri KeyId);
}
