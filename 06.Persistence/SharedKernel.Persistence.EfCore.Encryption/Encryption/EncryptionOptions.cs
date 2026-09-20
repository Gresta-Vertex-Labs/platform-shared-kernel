using SharedKernel.Configuration;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Configuration for field-level AES-256-GCM transparent encryption.
/// </summary>
/// <remarks>
/// <para>
/// <strong>No key material lives here.</strong> Keys come exclusively from whichever <c>01.Core</c>
/// <c>IEncryptionKeyProvider</c>/<c>ISynchronousEncryptionKeyProvider</c> the consuming service registers (a
/// <c>StaticEncryptionKeyProvider</c> for development/config-backed keys, a KMS-backed provider such as
/// <c>SharedKernel.Cryptography.KeyVault.Azure</c>'s for production) — <c>EfCorePersistenceBuilder{TContext}
/// .WithEncryption()</c> requires one to already be registered and fails loudly at <c>Build()</c> time if none is.
/// </para>
/// <para>
/// Resolved once via <c>IOptions&lt;EncryptionOptions&gt;</c>, never <c>IOptionsMonitor</c> — encryption is fixed at
/// startup by design, so a configuration reload can never silently flip a running process from encrypting to
/// plaintext pass-through.
/// </para>
/// </remarks>
public sealed class EncryptionOptions : ISectionBoundOptions
{
    /// <inheritdoc />
    public static string SectionName => "SharedKernel:Persistence:Encryption";

    /// <summary>
    /// TEMPORARY MIGRATION SETTING. When <see langword="true"/>, a stored value in an encrypted column that does
    /// not parse as an encrypted payload is returned unchanged by <see cref="EncryptionInterceptor"/> instead of
    /// throwing. Default is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Enable only while migrating a column that already holds unencrypted data: every insert/update still
    /// encrypts, and re-saving each row (for example via <c>IEncryptionRotationJob</c>, which re-encrypts unrelated
    /// to this flag) leaves fewer unencrypted rows over time. Turn it off once no row is unencrypted.
    /// </para>
    /// <para>
    /// <strong>SECURITY:</strong> while enabled, anyone who can write to the database can plant arbitrary plaintext
    /// that the application reads back as if it had been decrypted, bypassing AES-GCM integrity entirely. With the
    /// default (<see langword="false"/>), such a value fails closed with a
    /// <see cref="System.Security.Cryptography.CryptographicException"/>.
    /// </para>
    /// </remarks>
    public bool AllowUnencryptedValues { get; set; }

    /// <summary>
    /// Historical key ids that must be kept warm for decryption when the consuming service registered only an
    /// asynchronous <c>IEncryptionKeyProvider</c> (no <c>ISynchronousEncryptionKeyProvider</c>) — see
    /// <see cref="KeyRing.EncryptionKeyRingCache"/>. Ignored when a genuine <c>ISynchronousEncryptionKeyProvider</c> is
    /// already registered, since that provider answers every key id from memory on its own.
    /// </summary>
    /// <remarks>
    /// The current key is always kept warm automatically. A key id not listed here and not current cannot be
    /// decrypted through the bridge — <see cref="EncryptionInterceptor"/> fails closed with
    /// <see cref="EncryptionKeyNotFoundException"/> for it, never silently.
    /// </remarks>
    public IReadOnlyList<string> KeyRingRetiredKeyIds { get; set; } = [];

    /// <summary>How often <see cref="KeyRing.EncryptionKeyRingCache"/> re-reads the current key id from the asynchronous provider. Default 5 minutes.</summary>
    public TimeSpan KeyRingRefreshInterval { get; set; } = TimeSpan.FromMinutes(5);
}
