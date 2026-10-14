using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using SharedKernel.Persistence.Abstractions.Errors;

namespace SharedKernel.Persistence.Abstractions.Tests.Errors;

/// <summary>
/// The persistence error-code registry: every constant of <see cref="PersistenceErrorCodes"/> follows the C1 shape, is
/// unique, and is pinned by a literal, so a rename shows up as a deliberate edit here.
/// </summary>
public sealed partial class PersistenceErrorCodesTests
{
    private static readonly IReadOnlyDictionary<string, string> Pinned = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Tenancy.TenantRequired"] = "persistence.tenant_required",
        ["Tenancy.TenantIsolationViolation"] = "persistence.tenant_isolation_violation",
        ["Concurrency.Conflict"] = "persistence.concurrency_conflict",
        ["Postgresql.UniqueViolation"] = "persistence.postgresql.unique_violation",
        ["Postgresql.ForeignKeyReferenceMissing"] = "persistence.postgresql.foreign_key_reference_missing",
        ["Postgresql.ForeignKeyDependentExists"] = "persistence.postgresql.foreign_key_dependent_exists",
        ["Postgresql.ForeignKeyViolation"] = "persistence.postgresql.foreign_key_violation",
        ["Postgresql.NotNullViolation"] = "persistence.postgresql.not_null_violation",
        ["Postgresql.CheckViolation"] = "persistence.postgresql.check_violation",
        ["Postgresql.ExclusionViolation"] = "persistence.postgresql.exclusion_violation",
        ["Postgresql.ValueTooLong"] = "persistence.postgresql.value_too_long",
        ["Postgresql.TransientConflict"] = "persistence.postgresql.transient_conflict",
        ["Postgresql.LockTimeout"] = "persistence.postgresql.lock_timeout",
        ["Postgresql.StatementTimeout"] = "persistence.postgresql.statement_timeout",
        ["Postgresql.InsufficientPrivilege"] = "persistence.postgresql.insufficient_privilege",
        ["Audit.RecordNotFound"] = "persistence.audit_record_not_found",
        ["Audit.ChainNotIntact"] = "persistence.audit_chain_not_intact",
        ["Encryption.MalformedPayload"] = "persistence.encryption.malformed_payload",
        ["Encryption.DecryptionFailed"] = "persistence.encryption.decryption_failed",
        ["Encryption.DataKeyInvalid"] = "persistence.encryption.data_key_invalid",
        ["Encryption.DataKeyUnwrapFailed"] = "persistence.encryption.data_key_unwrap_failed",
        ["Encryption.KeyNotFound"] = "persistence.encryption.key_not_found",
        ["Encryption.TenantKeyShredded"] = "persistence.encryption.tenant_key_shredded",
        ["Encryption.TenantShredIncomplete"] = "persistence.encryption.tenant_shred_incomplete",
        ["BulkMutation.SpecificationNotSupported"] = "persistence.bulk_mutation.specification_not_supported",
    };

    [GeneratedRegex(@"^[a-z][a-z0-9]*(\.[a-z][a-z0-9]*(_[a-z0-9]+)*){1,2}$")]
    private static partial Regex CodeShape();

    private static Dictionary<string, string> Declared()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in typeof(PersistenceErrorCodes).GetNestedTypes(BindingFlags.Public))
        {
            foreach (var field in group.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                field.IsLiteral.Should().BeTrue($"{group.Name}.{field.Name} is a const");
                result[$"{group.Name}.{field.Name}"] = (string)field.GetRawConstantValue()!;
            }
        }

        return result;
    }

    [Fact]
    public void EveryDeclaredCode_IsPinnedByALiteral_AndEveryPinIsDeclared()
    {
        Declared().Should().BeEquivalentTo(Pinned);
    }

    [Fact]
    public void EveryCode_IsLowercaseDomainDotSnakeReason()
    {
        Declared().Values.Should().OnlyContain(code => CodeShape().IsMatch(code) && code.StartsWith("persistence.", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryCode_IsUnique()
    {
        var codes = Declared().Values.ToList();
        codes.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TheRegistryDeclaresGroupsOnly_NoLooseConstants()
    {
        typeof(PersistenceErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Should().BeEmpty();
    }
}
