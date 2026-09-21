using SharedKernel.Configuration;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// Configuration of the audit ledger: the sealing keyring, checkpoint signing, the sealer and the
/// startup self-check. Bound from <see cref="SectionName"/> and validated at startup.
/// </summary>
/// <example>
/// <code>
/// "SharedKernel": { "Persistence": { "Auditing": {
///   "CurrentKeyId": "k2",
///   "Keys": {
///     "k1": { "Material": "&lt;base64, 32+ bytes&gt;", "Order": 1 },
///     "k2": { "Material": "&lt;base64, 32+ bytes&gt;", "Order": 2 }
///   },
///   "CheckpointSigningKeyId": "audit-checkpoints-2026",
///   "AcceptedCheckpointSigningKeyIds": [ "audit-checkpoints-2025" ],
///   "Sealer": { "Interval": "00:00:02", "BatchSize": 500, "CheckpointInterval": "01:00:00" },
///   "SelfCheck": "Fail"
/// } } }
/// </code>
/// </example>
public sealed class AuditLedgerOptions : ISectionBoundOptions
{
    /// <inheritdoc />
    public static string SectionName => "SharedKernel:Persistence:Auditing";

    /// <summary>
    /// Gets or sets the id of the key new records are sealed under. Must name an entry of <see cref="Keys"/>
    /// with the highest <see cref="AuditKeyOptions.Order"/>. Not needed when a custom
    /// <see cref="IAuditRecordAuthenticator"/> is registered.
    /// </summary>
    public string? CurrentKeyId { get; set; }

    /// <summary>
    /// Gets the sealing keyring, by key id. Keep retired keys here for as long as records sealed under them
    /// must stay verifiable; removing one makes those records <see cref="AuditVerificationStatus.Unverifiable"/>.
    /// Ignored when a custom <see cref="IAuditRecordAuthenticator"/> is registered.
    /// </summary>
    public Dictionary<string, AuditKeyOptions> Keys { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the <c>IAsymmetricSignatureService</c> key id checkpoints are signed with.
    /// <see langword="null"/> disables checkpoint creation.
    /// </summary>
    public string? CheckpointSigningKeyId { get; set; }

    /// <summary>
    /// Gets the signing key ids a checkpoint may be signed with to be accepted by verification, besides
    /// <see cref="CheckpointSigningKeyId"/> (A20: a checkpoint never chooses its own trusted key).
    /// </summary>
    public List<string> AcceptedCheckpointSigningKeyIds { get; } = [];

    /// <summary>Gets or sets the background sealer settings.</summary>
    public AuditSealerOptions Sealer { get; set; } = new();

    /// <summary>
    /// Gets or sets what the startup self-check does when the database lets the runtime role alter the
    /// ledger (owner, superuser, UPDATE/DELETE/TRUNCATE privilege, missing or disabled triggers).
    /// Defaults to <see cref="AuditSelfCheckMode.Warn"/>; use <see cref="AuditSelfCheckMode.Fail"/> in production.
    /// </summary>
    public AuditSelfCheckMode SelfCheck { get; set; } = AuditSelfCheckMode.Warn;

    /// <summary>Gets or sets a value indicating whether a custom authenticator replaces the keyring (set by the registration, never bound).</summary>
    internal bool UsesCustomAuthenticator { get; set; }

    /// <summary>Returns every accepted checkpoint signing key id (the pinned list plus the current signing key).</summary>
    internal IReadOnlySet<string> EffectiveAcceptedCheckpointSigningKeyIds()
    {
        var accepted = new HashSet<string>(AcceptedCheckpointSigningKeyIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(CheckpointSigningKeyId))
            accepted.Add(CheckpointSigningKeyId);
        return accepted;
    }
}

/// <summary>One sealing key of <see cref="AuditLedgerOptions.Keys"/>.</summary>
public sealed class AuditKeyOptions
{
    /// <summary>Gets or sets the key material, Base64, at least 32 bytes. Load it from a secret store.</summary>
    public string? Material { get; set; }

    /// <summary>Gets or sets the rotation order; every key needs a distinct value and newer keys a higher one.</summary>
    public int Order { get; set; }
}

/// <summary>Settings of the background sealer.</summary>
public sealed class AuditSealerOptions
{
    /// <summary>Gets or sets a value indicating whether this instance runs the sealer. Defaults to <see langword="true"/>; every instance may run it, only one seals at a time.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the pause between sealing rounds. Defaults to 2 seconds.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Gets or sets the maximum number of records sealed per transaction. Defaults to 500.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// Gets or sets how often the sealer checkpoints every chain whose head moved. Defaults to one hour.
    /// Only effective when <see cref="AuditLedgerOptions.CheckpointSigningKeyId"/> is set.
    /// </summary>
    public TimeSpan CheckpointInterval { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>What the startup self-check does with a finding.</summary>
public enum AuditSelfCheckMode
{
    /// <summary>The check does not run.</summary>
    Off = 0,

    /// <summary>Each finding is logged as a warning.</summary>
    Warn = 1,

    /// <summary>Any finding stops the host from starting.</summary>
    Fail = 2,
}
