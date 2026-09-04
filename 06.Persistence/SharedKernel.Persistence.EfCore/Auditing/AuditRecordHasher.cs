using System.Globalization;
using System.Text;
using SharedKernel.Cryptography.Hashing;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// Canonical-serialization hashing helper shared by <see cref="EfAuditTrailWriter"/> (write-time)
/// and <see cref="EfAuditQueryService"/> (verify-time) — the ONE place the exact field order and
/// encoding is defined, so write-time and verify-time hashing can never drift apart.
/// </summary>
/// <remarks>
/// WO-071/P-457/D-122/D-124. Uses a delimited plain-text encoding (never JSON) — every field is a
/// flat scalar, so a fixed-order, fixed-delimiter concatenation is simpler and more robust than a
/// serializer round-trip, and carries no ambiguity about property ordering or escaping rules that a
/// generic JSON serializer's output could silently vary across versions.
/// </remarks>
internal static class AuditRecordHasher
{
    // ASCII Unit Separator (U+001F) — a control character that can never appear in any of the
    // canonicalized field values below, so it is a safe, unambiguous field delimiter.
    private const char FieldSeparator = '';

    /// <summary>
    /// Computes the hex SHA-256 digest of a canonical byte representation of the supplied fields.
    /// </summary>
    public static string ComputeHashHex(
        IContentHasher contentHasher,
        Guid id,
        Guid tenantId,
        string actorId,
        string action,
        string resourceType,
        string resourceId,
        DateTimeOffset occurredOn,
        string? beforeSnapshot,
        string? afterSnapshot,
        string? correlationId,
        string? approvalId,
        string? previousRecordHash)
    {
        var canonical = string.Join(
            FieldSeparator,
            id.ToString("D", CultureInfo.InvariantCulture),
            tenantId.ToString("D", CultureInfo.InvariantCulture),
            actorId,
            action,
            resourceType,
            resourceId,
            occurredOn.ToString("O", CultureInfo.InvariantCulture),
            beforeSnapshot ?? string.Empty,
            afterSnapshot ?? string.Empty,
            correlationId ?? string.Empty,
            approvalId ?? string.Empty,
            previousRecordHash ?? string.Empty);

        var bytes = Encoding.UTF8.GetBytes(canonical);
        var digest = contentHasher.ComputeHash(bytes);
        return Convert.ToHexStringLower(digest);
    }
}
