using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Cryptography.KeyVault.Azure;
using SharedKernel.Cryptography.KeyVault.Azure.Extensions;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.ServiceDefaults.Cryptography;

/// <summary>
/// Composition-root registration for <c>01.Core</c>'s Azure Key Vault Keys
/// <c>IEncryptionKeyProvider</c>/<c>IEnvelopeEncryptionProvider</c> implementation.
/// </summary>
/// <remarks>
/// <para>
/// <b>DISTINCT FROM <c>Configuration.KeyVaultConfigurationExtensions.AddSharedKernelKeyVaultConfiguration</c>
/// — DO NOT CONFUSE THE TWO.</b> That method wires Azure Key Vault as an
/// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> <i>source</i> (secrets read
/// into configuration). <see cref="AddSharedKernelKeyVaultKeyProvider"/> registers Azure Key
/// Vault <i>Keys</i> as the platform's <c>IEncryptionKeyProvider</c>/
/// <c>IEnvelopeEncryptionProvider</c> — the key material used to encrypt/decrypt data (e.g.
/// <c>06.Persistence</c>'s <c>EncryptedValueConverter</c>, <c>02.Caching</c>'s cache-value
/// encryption). A service can use either, both, or neither independently.
/// </para>
/// <para>
/// Implementation is a thin call-through to <c>01.Core</c>'s already-fully-specified
/// <see cref="AzureKeyVaultCryptographyServiceCollectionExtensions.AddSharedKernelAzureKeyVaultCryptography"/> —
/// <c>13.ServiceDefaults</c> never reimplements Key Vault key resolution itself, mirroring the
/// standing "owning domain ships the provider, this domain ships the composition wiring" rule
/// already applied to <c>06.Persistence</c>/<c>08.Storage</c>/<c>09.Search</c>/
/// <c>10.Intelligence</c>/<c>17.Workflows</c>/<c>12.Security.Mtls</c>.
/// </para>
/// </remarks>
public static class KeyVaultKeyProviderExtensions
{
    /// <summary>
    /// The default cache TTL applied to the <see cref="IEncryptionKeyProvider"/> wrap when
    /// <c>cacheTtl</c> is left <see langword="null"/>.
    /// </summary>
    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Registers <c>01.Core</c>'s <see cref="AzureKeyVaultEncryptionKeyProvider"/> as the
    /// platform's <c>IEncryptionKeyProvider</c>/<c>IEnvelopeEncryptionProvider</c>/
    /// <c>IEncryptionKeyProviderProbe</c>, and — unless explicitly disabled — wraps the
    /// <c>IEncryptionKeyProvider</c> registration in a bounded-TTL
    /// <see cref="CachedEncryptionKeyProvider"/>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="cacheTtl">
    /// How long a resolved key is served from the in-process cache before the next call re-fetches
    /// from the raw <see cref="AzureKeyVaultEncryptionKeyProvider"/> — see the
    /// <see cref="CachedEncryptionKeyProvider"/> caching remarks below. <see langword="null"/>
    /// (the default) uses an internal 5-minute TTL. <see cref="TimeSpan.Zero"/> is the explicit
    /// opt-out — <c>IEncryptionKeyProvider</c> then resolves the RAW, uncached
    /// <see cref="AzureKeyVaultEncryptionKeyProvider"/> singleton, exactly as before this
    /// parameter existed. A negative value throws <see cref="ArgumentOutOfRangeException"/>
    /// before any service is registered, mirroring <see cref="CachedEncryptionKeyProvider"/>'s own
    /// constructor guard.
    /// </param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// Idempotent: calling this method more than once registers
    /// <see cref="AzureKeyVaultEncryptionKeyProvider"/>/<c>IEncryptionKeyProvider</c>/
    /// <c>IEnvelopeEncryptionProvider</c>/<c>IEncryptionKeyProviderProbe</c>/
    /// <see cref="CachedEncryptionKeyProvider"/> exactly once — the second call is a no-op
    /// (including its <paramref name="cacheTtl"/> argument, which is silently ignored on any call
    /// after the first — this mirrors the method's own pre-existing idempotency contract), guarded
    /// by checking whether <see cref="AzureKeyVaultEncryptionKeyProvider"/> is already registered.
    /// This guard exists here, not in <c>01.Core</c>'s own
    /// <c>AddSharedKernelAzureKeyVaultCryptography</c>, which registers unconditionally via
    /// <c>AddSingleton</c> (correct for its own single-call contract) — a bare call-through would
    /// otherwise double-register on a repeated call.
    /// </para>
    /// <para>
    /// <b>ONLY <c>IEncryptionKeyProvider</c> IS EVER CACHE-WRAPPED.</b>
    /// <c>IEnvelopeEncryptionProvider</c> and <c>IEncryptionKeyProviderProbe</c> are DELIBERATELY
    /// LEFT resolving the RAW <see cref="AzureKeyVaultEncryptionKeyProvider"/> singleton,
    /// untouched, for two independent, load-bearing reasons:
    /// <see cref="CachedEncryptionKeyProvider"/> implements <c>IEncryptionKeyProvider</c> ONLY —
    /// never <c>IEnvelopeEncryptionProvider</c> (envelope wrap/unwrap is a real per-call crypto
    /// operation against the vault, not a cacheable key lookup — there is nothing to cache); and a
    /// readiness/health probe caching its own reachability signal would defeat the entire purpose
    /// of <c>AddKeyVaultKeyProviderReadinessCheck</c> — a probe must always observe LIVE KMS
    /// state, never a stale cache entry.
    /// </para>
    /// <para>
    /// <see cref="CachedEncryptionKeyProvider"/> is ALSO registered as its own resolvable concrete
    /// type (not merely behind <c>IEncryptionKeyProvider</c>) so a consuming service's
    /// <c>06.Persistence</c> builder chain can target it explicitly via
    /// <c>.WithExternalEncryptionKeyProvider&lt;CachedEncryptionKeyProvider&gt;()</c>, alongside
    /// the still-independently-available raw
    /// <c>.WithExternalEncryptionKeyProvider&lt;AzureKeyVaultEncryptionKeyProvider&gt;()</c>.
    /// </para>
    /// <para>
    /// <b>THIS WRAP CAN NEVER UNLOCK A SYNCHRONOUS PATH.</b>
    /// <see cref="CachedEncryptionKeyProvider"/> never implements <c>01.Core</c>'s
    /// <c>ISynchronousEncryptionKeyProvider</c> marker, regardless of cache warmth or TTL — a
    /// KMS-backed provider wrapped here still causes <c>IsGenuinelySynchronous</c> to report
    /// <see langword="false"/>, and the platform's sync <c>Encrypt</c>/<c>Decrypt</c>/
    /// <c>EncryptToString</c>/<c>DecryptToString</c> members still throw
    /// <c>NotSupportedException</c> against it. The value here is strictly for ASYNC consumers.
    /// </para>
    /// <para>
    /// <b>STANDING DESIGN NOTE — DO NOT MAKE THIS AUTOMATIC.</b> This method deliberately never
    /// wires its registered <c>IEncryptionKeyProvider</c>/<see cref="CachedEncryptionKeyProvider"/>
    /// into <c>06.Persistence</c>'s ambient, unkeyed encryption-key-provider slot on a consuming
    /// service's behalf. A service wanting BOTH KMS-backed general-purpose crypto (via this
    /// method) AND KMS-backed persistence-layer column encryption must call
    /// <c>.WithExternalEncryptionKeyProvider&lt;TProvider&gt;()</c> itself, explicitly, in its own
    /// <c>06.Persistence</c> builder chain. <c>06.Persistence</c>'s own fix for its P-498 SEVERE
    /// defect (an accidental ambient-registration-order collision between
    /// <c>.WithEncryption()</c> and this method, both wiring the SAME unkeyed
    /// <c>IEncryptionKeyProvider</c>) deliberately made that connection an EXPLICIT,
    /// consumer-driven opt-in for exactly this reason — making it automatic here would recreate
    /// the exact ambient-collision hazard that fix exists to eliminate. This is a standing design
    /// decision, not an oversight to "fix" in a future session.
    /// </para>
    /// <para>
    /// <b>CROSS-DOMAIN HAZARD — DO NOT COMPOSE WITH <c>07.Messaging</c> PAYLOAD ENCRYPTION
    /// AGAINST THE SAME AMBIENT SLOT.</b> <c>07.Messaging</c>'s payload-encryption serializer
    /// path is HARD-SYNCHRONOUS, with no async overload. A KMS-backed
    /// <c>IEncryptionKeyProvider</c> — cache-wrapped by this method or not — can NEVER satisfy
    /// <c>01.Core</c>'s synchronous-capability gate. A SERVICE THAT ENABLES BOTH THIS METHOD AND
    /// <c>07.Messaging</c>'S <c>WithPayloadTransform()</c> AGAINST THE SAME AMBIENT
    /// <c>IEncryptionKeyProvider</c>/<c>ISymmetricEncryptionService</c> SLOT WILL BREAK
    /// UNCONDITIONALLY, WITH <c>NotSupportedException</c> ON EVERY MESSAGE, ONCE <c>01.Core</c>'s
    /// synchronous-capability gate ships. Keep messaging payload encryption on an
    /// independently-configured, config-backed <c>IEncryptionKeyProvider</c> — never the ambient
    /// slot this method registers — until/unless <c>07.Messaging</c> ships its own keyed-DI
    /// isolation mirroring <c>06.Persistence</c>'s fix above. This is documentation only —
    /// <c>13.ServiceDefaults</c> is the one composition-root location where a service would wire
    /// both together in the same <c>Program.cs</c>, so it is documented here; fixing the
    /// underlying incompatibility is <c>07.Messaging</c>'s own jurisdiction.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder AddSharedKernelKeyVaultKeyProvider(
        this IHostApplicationBuilder builder,
        TimeSpan? cacheTtl = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (cacheTtl is { } requestedCacheTtl && requestedCacheTtl < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cacheTtl),
                requestedCacheTtl,
                "The cache TTL must not be negative. Pass null for the default TTL, or TimeSpan.Zero to disable caching.");
        }

        if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(AzureKeyVaultEncryptionKeyProvider)))
        {
            return builder;
        }

        builder.Services.AddSharedKernelAzureKeyVaultCryptography(builder.Configuration);

        TimeSpan effectiveCacheTtl = cacheTtl ?? DefaultCacheTtl;
        if (effectiveCacheTtl != TimeSpan.Zero)
        {
            builder.Services.AddSingleton(sp => new CachedEncryptionKeyProvider(
                sp.GetRequiredService<AzureKeyVaultEncryptionKeyProvider>(),
                sp.GetService<TimeProvider>() ?? TimeProvider.System,
                effectiveCacheTtl));

            // Re-registers ONLY IEncryptionKeyProvider — the last registration wins for a
            // single-instance resolve, so this cleanly supersedes 01.Core's own raw
            // IEncryptionKeyProvider registration for this one interface without touching
            // 01.Core's own call. IEnvelopeEncryptionProvider/IEncryptionKeyProviderProbe stay
            // resolving the raw AzureKeyVaultEncryptionKeyProvider singleton, untouched.
            builder.Services.AddSingleton<IEncryptionKeyProvider>(sp =>
                sp.GetRequiredService<CachedEncryptionKeyProvider>());
        }

        return builder;
    }
}
