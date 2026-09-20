using System.Diagnostics.Metrics;

namespace SharedKernel.Persistence.EfCore.Encryption.Diagnostics;

/// <summary>The shared <see cref="Meter"/> and instrument-name constants for <c>SharedKernel.Persistence.EfCore.Encryption</c> metrics.</summary>
/// <remarks>
/// BCL <see cref="Meter"/> — zero new NuGet dependency, AOT-safe. Mirrors
/// <c>SharedKernel.Persistence.EfCore.Diagnostics.PersistenceMeter</c>'s "one shared, well-known name" shape. A host
/// enables OpenTelemetry export for it with <c>AddMeter(EncryptionMeter.Name)</c>.
/// </remarks>
public static class EncryptionMeter
{
    /// <summary>The shared <see cref="Meter"/> name for this package.</summary>
    public const string Name = "SharedKernel.Persistence.EfCore.Encryption";

    /// <summary>
    /// Counter name: the number of encrypt attempts that failed, tagged with <c>reason</c>. Never tagged with a
    /// key id, property name or any plaintext/ciphertext value.
    /// </summary>
    public const string EncryptFailuresTotal = "persistence.encryption.encrypt_failures";

    /// <summary>
    /// Counter name: the number of decrypt attempts that failed, tagged with <c>reason</c> (one of
    /// <c>malformed_payload</c>, <c>unknown_key</c>, <c>decryption_failed</c>). Never tagged with a key id,
    /// property name or any plaintext/ciphertext value.
    /// </summary>
    public const string DecryptFailuresTotal = "persistence.encryption.decrypt_failures";

    /// <summary>Counter name: the number of rows re-encrypted by an <c>IEncryptionRotationJob</c> run, tagged with the target key id.</summary>
    public const string RotationRowsRotatedTotal = "persistence.encryption.rotation_rows_rotated";

    /// <summary>Counter name: the number of rows an <c>IEncryptionRotationJob</c> run failed to rotate, tagged with the source key id.</summary>
    public const string RotationRowsFailedTotal = "persistence.encryption.rotation_rows_failed";

    private static readonly Meter Instance = new(Name, "1.0");

    private static readonly Counter<long> EncryptFailures =
        Instance.CreateCounter<long>(EncryptFailuresTotal, unit: "{failure}", description: "Encrypt attempts that failed.");

    private static readonly Counter<long> DecryptFailures =
        Instance.CreateCounter<long>(DecryptFailuresTotal, unit: "{failure}", description: "Decrypt attempts that failed.");

    private static readonly Counter<long> RotationRowsRotated =
        Instance.CreateCounter<long>(RotationRowsRotatedTotal, unit: "{row}", description: "Rows re-encrypted by a rotation run.");

    private static readonly Counter<long> RotationRowsFailed =
        Instance.CreateCounter<long>(RotationRowsFailedTotal, unit: "{row}", description: "Rows a rotation run failed to re-encrypt.");

    /// <summary>Records a failed encrypt attempt.</summary>
    /// <param name="reason">A short, stable, non-sensitive reason code.</param>
    public static void RecordEncryptFailure(string reason) => EncryptFailures.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>Records a failed decrypt attempt.</summary>
    /// <param name="reason">A short, stable, non-sensitive reason code — <c>malformed_payload</c>, <c>unknown_key</c>, or <c>decryption_failed</c>.</param>
    public static void RecordDecryptFailure(string reason) => DecryptFailures.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>Records rows a rotation run re-encrypted to <paramref name="targetKeyId"/>.</summary>
    public static void RecordRotationRowsRotated(string targetKeyId, long count) =>
        RotationRowsRotated.Add(count, new KeyValuePair<string, object?>("key_id", targetKeyId));

    /// <summary>Records rows a rotation run failed to re-encrypt away from <paramref name="sourceKeyId"/>.</summary>
    public static void RecordRotationRowsFailed(string sourceKeyId, long count) =>
        RotationRowsFailed.Add(count, new KeyValuePair<string, object?>("key_id", sourceKeyId));
}
