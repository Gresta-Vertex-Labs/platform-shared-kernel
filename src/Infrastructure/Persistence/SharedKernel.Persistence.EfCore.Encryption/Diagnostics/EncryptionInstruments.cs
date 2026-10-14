using System.Diagnostics.Metrics;
using SharedKernel.Persistence.EfCore.Diagnostics;

namespace SharedKernel.Persistence.EfCore.Encryption.Diagnostics;

/// <summary>
/// The instruments of field encryption, created from the one persistence <see cref="Meter"/>
/// (<c>SharedKernel.Persistence</c>) and named <c>sharedkernel.persistence.encryption.*</c>.
/// </summary>
/// <remarks>
/// A host subscribes to <c>SharedKernel.*</c> (or to <c>PersistenceTelemetry.Name</c>) once. No instrument is ever tagged
/// with key material, a row's primary key, a tenant id or any value; the tag values are the closed sets in
/// <see cref="EncryptionTagValues"/>.
/// </remarks>
internal static class EncryptionInstruments
{
    /// <summary>Counter (<c>{failure}</c>): failed encryptions, tagged <see cref="PersistenceTagKeys.EncryptionReason"/>.</summary>
    internal const string EncryptFailuresInstrument = "sharedkernel.persistence.encryption.encrypt_failures";

    /// <summary>Counter (<c>{failure}</c>): failed decryptions, tagged <see cref="PersistenceTagKeys.EncryptionReason"/>.</summary>
    internal const string DecryptFailuresInstrument = "sharedkernel.persistence.encryption.decrypt_failures";

    /// <summary>Counter (<c>{row}</c>): values the maintenance job wrote, tagged <see cref="PersistenceTagKeys.EncryptionMaintenanceOperation"/>.</summary>
    internal const string MaintenanceRowsWrittenInstrument = "sharedkernel.persistence.encryption.maintenance_rows_written";

    /// <summary>Counter (<c>{row}</c>): values the maintenance job could not process, tagged <see cref="PersistenceTagKeys.EncryptionReason"/>.</summary>
    internal const string MaintenanceRowsSkippedInstrument = "sharedkernel.persistence.encryption.maintenance_rows_skipped";

    /// <summary>Counter (<c>{key}</c>): tenant data keys shredded.</summary>
    internal const string TenantKeysShreddedInstrument = "sharedkernel.persistence.encryption.tenant_keys_shredded";

    private static readonly Counter<long> EncryptFailures =
        PersistenceMeter.Instance.CreateCounter<long>(EncryptFailuresInstrument, unit: "{failure}", description: "Encryptions that failed.");

    private static readonly Counter<long> DecryptFailures =
        PersistenceMeter.Instance.CreateCounter<long>(DecryptFailuresInstrument, unit: "{failure}", description: "Decryptions that failed.");

    private static readonly Counter<long> MaintenanceRowsWritten =
        PersistenceMeter.Instance.CreateCounter<long>(MaintenanceRowsWrittenInstrument, unit: "{row}", description: "Values the encryption maintenance job wrote.");

    private static readonly Counter<long> MaintenanceRowsSkipped =
        PersistenceMeter.Instance.CreateCounter<long>(MaintenanceRowsSkippedInstrument, unit: "{row}", description: "Values the encryption maintenance job could not process.");

    private static readonly Counter<long> TenantKeysShredded =
        PersistenceMeter.Instance.CreateCounter<long>(TenantKeysShreddedInstrument, unit: "{key}", description: "Tenant data keys shredded.");

    internal static void RecordEncryptFailure(string reason) =>
        EncryptFailures.Add(1, new KeyValuePair<string, object?>(PersistenceTagKeys.EncryptionReason, reason));

    internal static void RecordDecryptFailure(string reason) =>
        DecryptFailures.Add(1, new KeyValuePair<string, object?>(PersistenceTagKeys.EncryptionReason, reason));

    internal static void RecordMaintenanceRowsWritten(string operation, long count)
    {
        if (count > 0)
            MaintenanceRowsWritten.Add(count, new KeyValuePair<string, object?>(PersistenceTagKeys.EncryptionMaintenanceOperation, operation));
    }

    internal static void RecordMaintenanceRowsSkipped(string reason, long count)
    {
        if (count > 0)
            MaintenanceRowsSkipped.Add(count, new KeyValuePair<string, object?>(PersistenceTagKeys.EncryptionReason, reason));
    }

    internal static void RecordTenantKeyShredded() => TenantKeysShredded.Add(1);
}

/// <summary>The closed sets of tag values of the field-encryption instruments.</summary>
internal static class EncryptionTagValues
{
    /// <summary>An encryption failed.</summary>
    internal const string EncryptFailed = "encrypt_failed";

    /// <summary>A stored value is not an encrypted payload.</summary>
    internal const string MalformedPayload = "malformed_payload";

    /// <summary>A stored value names a key the process does not know.</summary>
    internal const string UnknownKey = "unknown_key";

    /// <summary>A payload did not authenticate.</summary>
    internal const string DecryptionFailed = "decryption_failed";

    /// <summary>A value of a shredded tenant was read.</summary>
    internal const string TenantKeyShredded = "tenant_key_shredded";

    /// <summary>The maintenance job re-encrypted a value under the current key.</summary>
    internal const string ReEncrypt = "re_encrypt";

    /// <summary>The maintenance job encrypted a plaintext value.</summary>
    internal const string EncryptPlaintext = "encrypt_plaintext";

    /// <summary>The maintenance job recomputed a blind index.</summary>
    internal const string RecomputeBlindIndex = "recompute_blind_index";

    /// <summary>The maintenance job skipped a row another writer changed.</summary>
    internal const string ConcurrentWrite = "concurrent_write";

    /// <summary>The maintenance job skipped a plaintext value.</summary>
    internal const string Plaintext = "plaintext";

    /// <summary>The maintenance job skipped a value it could not decrypt.</summary>
    internal const string Undecryptable = "undecryptable";

    /// <summary>The maintenance job skipped a value of a shredded tenant.</summary>
    internal const string Shredded = "shredded";

    /// <summary>The maintenance job skipped a value of a shredded tenant whose rows are not erased.</summary>
    internal const string ShreddedTenantNotErased = "shredded_tenant_not_erased";
}
