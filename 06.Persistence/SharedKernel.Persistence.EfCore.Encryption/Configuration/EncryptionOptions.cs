using SharedKernel.Configuration;

namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>Configuration for field-level encryption, bound from <see cref="SectionName"/>.</summary>
/// <remarks>
/// Bound from the host's <c>IConfiguration</c> when one is registered, then adjusted by
/// <c>FieldEncryptionBuilder.Configure(...)</c>, and validated when the host starts. Read once through
/// <c>IOptions&lt;EncryptionOptions&gt;</c>: a configuration reload never changes a running process's encryption.
/// </remarks>
public sealed class EncryptionOptions : ISectionBoundOptions
{
    /// <inheritdoc />
    public static string SectionName => "SharedKernel:Persistence:Encryption";

    /// <summary>
    /// Encryption keys for <c>FieldEncryptionBuilder.FromConfiguration()</c>. Ignored when another key source is
    /// used. Keep real key material in a secret store, never in a checked-in file.
    /// </summary>
    public EncryptionKeyOptions Keys { get; set; } = new();

    /// <summary>The blind-index keys used when no custom <c>IBlindIndexKeyProvider</c> is registered.</summary>
    public BlindIndexKeyOptions BlindIndexKeys { get; set; } = new();

    /// <summary>
    /// How often keys are re-read from an asynchronous-only key provider (for example Azure Key Vault), which
    /// the synchronous encrypt/decrypt path serves from memory. Default 5 minutes.
    /// </summary>
    public TimeSpan KeyRefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Key ids, besides the current one, that must be decryptable from the first request after startup when keys
    /// come from an asynchronous-only provider. Any other id is fetched in the background the first time a value
    /// needs it; that one read fails with <see cref="EncryptionKeyNotFoundException"/> and later reads succeed.
    /// </summary>
    public IReadOnlyList<string> AdditionalDecryptionKeyIds { get; set; } = [];

    /// <summary>
    /// How old the last successful key refresh may be before the key-ring probe reports unhealthy. Default 30
    /// minutes. Only meaningful for an asynchronous-only provider.
    /// </summary>
    public TimeSpan MaxKeyStaleness { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How long an unwrapped tenant data key stays in memory. Default 5 minutes. Also the longest time another process can still decrypt a tenant's data after it was shredded.</summary>
    public TimeSpan TenantKeyCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>The schema of the tenant key table, or <see langword="null"/> for the connection's default schema.</summary>
    public string? TenantKeySchema { get; set; }

    /// <summary>
    /// When <see langword="true"/> (the default), the maintenance job and tenant shredding run with PostgreSQL's
    /// <c>row_security = off</c>, so a table whose row-level security would hide rows from the connecting role
    /// makes them fail with an error instead of silently processing only the visible rows. Connect as a role that
    /// bypasses row-level security (the owner without <c>FORCE</c>, or a <c>BYPASSRLS</c> role through
    /// <c>FieldEncryptionBuilder.UseMaintenanceDataSource</c>). Set to <see langword="false"/> only when the
    /// maintenance role sees every row through a policy of its own; the job then refuses a table that looks empty
    /// but whose statistics say otherwise.
    /// </summary>
    public bool RequireRowSecurityBypass { get; set; } = true;
}

/// <summary>Encryption keys read from configuration.</summary>
public sealed class EncryptionKeyOptions
{
    /// <summary>The id of the key new values are encrypted with.</summary>
    public string? CurrentKeyId { get; set; }

    /// <summary>Every key that may still be needed for decryption, by id, as base64 (32 bytes each).</summary>
    public Dictionary<string, string> Keys { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Blind-index keys read from configuration.</summary>
public sealed class BlindIndexKeyOptions
{
    /// <summary>The version new indexes are computed with.</summary>
    public string? CurrentVersion { get; set; }

    /// <summary>Every version lookups must still match, as base64 (at least 32 bytes each).</summary>
    public Dictionary<string, string> Keys { get; set; } = new(StringComparer.Ordinal);
}
