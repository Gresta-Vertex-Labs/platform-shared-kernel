using System.Globalization;

namespace SharedKernel.Persistence.EfCore.Auditing.Chain;

/// <summary>
/// Builds the internal, non-nullable chain-key string a <c>(TenantId, ResourceType)</c> pair maps to.
/// </summary>
/// <remarks>
/// <para>
/// <c>AuditRecord.TenantId</c> is nullable (see its remarks), but a SQL unique index over a
/// nullable column cannot reliably enforce "at most one row per (tenant, sequence)" across every
/// provider/version combination without provider-specific syntax (PostgreSQL 15+'s <c>NULLS NOT
/// DISTINCT</c>) — because ordinary SQL <c>NULL</c> is never equal to another <c>NULL</c> for
/// uniqueness purposes. This type sidesteps that entirely: the DATABASE'S uniqueness/lookup key is
/// always this non-null string (a real Postgres <c>GENERATED ALWAYS AS</c> column — see
/// <c>AuditRecordEntityConfiguration</c>), computed from the nullable domain fields, while the DOMAIN
/// type (<see cref="Auditing.AuditRecord.TenantId"/>) stays honestly nullable.
/// </para>
/// </remarks>
internal static class AuditChainKeyFormat
{
    /// <summary>The tenant-position literal for a system (no-tenant) chain.</summary>
    public const string SystemTenantLiteral = "system";

    /// <summary>Builds the chain key for <paramref name="tenantId"/>/<paramref name="resourceType"/>.</summary>
    public static string Build(Guid? tenantId, string resourceType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);

        var tenantPart = tenantId is { } id
            ? id.ToString("D", CultureInfo.InvariantCulture)
            : SystemTenantLiteral;

        return $"{tenantPart}|{resourceType}";
    }
}
