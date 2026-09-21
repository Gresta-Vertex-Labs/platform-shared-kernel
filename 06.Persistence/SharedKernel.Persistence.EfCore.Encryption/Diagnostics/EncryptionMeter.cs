using System.Diagnostics.Metrics;

namespace SharedKernel.Persistence.EfCore.Encryption.Diagnostics;

/// <summary>The <see cref="Meter"/> name and instrument names of field encryption's metrics.</summary>
/// <remarks>
/// Enable export with <c>AddMeter(EncryptionMeter.Name)</c> (<c>13.ServiceDefaults</c>' persistence telemetry does
/// this). No instrument is ever tagged with key material, a row's primary key, a tenant id or any value.
/// </remarks>
public static class EncryptionMeter
{
    /// <summary>The meter name.</summary>
    public const string Name = "SharedKernel.Persistence.EfCore.Encryption";

    /// <summary>Counter: failed encryptions, tagged <c>reason</c>.</summary>
    internal const string EncryptFailuresTotal = "persistence.encryption.encrypt_failures";

    /// <summary>
    /// Counter: failed decryptions, tagged <c>reason</c> (<c>malformed_payload</c>, <c>unknown_key</c>,
    /// <c>decryption_failed</c>, <c>tenant_key_shredded</c>).
    /// </summary>
    internal const string DecryptFailuresTotal = "persistence.encryption.decrypt_failures";

    /// <summary>
    /// Counter: values the maintenance job wrote, tagged <c>operation</c> (<c>re_encrypt</c>,
    /// <c>encrypt_plaintext</c>, <c>recompute_blind_index</c>).
    /// </summary>
    internal const string MaintenanceRowsWrittenTotal = "persistence.encryption.maintenance_rows_written";

    /// <summary>
    /// Counter: values the maintenance job could not process, tagged <c>reason</c> (<c>concurrent_write</c>,
    /// <c>plaintext</c>, <c>undecryptable</c>, <c>shredded</c>).
    /// </summary>
    internal const string MaintenanceRowsSkippedTotal = "persistence.encryption.maintenance_rows_skipped";

    /// <summary>Counter: tenant data keys shredded.</summary>
    internal const string TenantKeysShreddedTotal = "persistence.encryption.tenant_keys_shredded";

    private static readonly Meter Instance = new(Name, "1.0");

    private static readonly Counter<long> EncryptFailures =
        Instance.CreateCounter<long>(EncryptFailuresTotal, unit: "{failure}", description: "Encryptions that failed.");

    private static readonly Counter<long> DecryptFailures =
        Instance.CreateCounter<long>(DecryptFailuresTotal, unit: "{failure}", description: "Decryptions that failed.");

    private static readonly Counter<long> MaintenanceRowsWritten =
        Instance.CreateCounter<long>(MaintenanceRowsWrittenTotal, unit: "{row}", description: "Values the encryption maintenance job wrote.");

    private static readonly Counter<long> MaintenanceRowsSkipped =
        Instance.CreateCounter<long>(MaintenanceRowsSkippedTotal, unit: "{row}", description: "Values the encryption maintenance job could not process.");

    private static readonly Counter<long> TenantKeysShredded =
        Instance.CreateCounter<long>(TenantKeysShreddedTotal, unit: "{key}", description: "Tenant data keys shredded.");

    internal static void RecordEncryptFailure(string reason) =>
        EncryptFailures.Add(1, new KeyValuePair<string, object?>("reason", reason));

    internal static void RecordDecryptFailure(string reason) =>
        DecryptFailures.Add(1, new KeyValuePair<string, object?>("reason", reason));

    internal static void RecordMaintenanceRowsWritten(string operation, long count)
    {
        if (count > 0)
            MaintenanceRowsWritten.Add(count, new KeyValuePair<string, object?>("operation", operation));
    }

    internal static void RecordMaintenanceRowsSkipped(string reason, long count)
    {
        if (count > 0)
            MaintenanceRowsSkipped.Add(count, new KeyValuePair<string, object?>("reason", reason));
    }

    internal static void RecordTenantKeyShredded() => TenantKeysShredded.Add(1);
}
