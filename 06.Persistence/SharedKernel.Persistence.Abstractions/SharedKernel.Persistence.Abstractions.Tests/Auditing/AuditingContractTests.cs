using SharedKernel.Application.Auditing;
using SharedKernel.Application.Context;
using System.Reflection;
using FluentAssertions;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.Abstractions.Context;

namespace SharedKernel.Persistence.Abstractions.Tests.Auditing;

/// <summary>
/// Structural immutability of
/// <see cref="AuditRecord"/>/<see cref="AuditEntry"/> and <see cref="IAuditTrailWriter"/>'s
/// single-member (no update/delete) contract shape; <see cref="AuditResourceHistorySpecification"/>/
/// <see cref="AuditActorActionsSpecification"/> criteria, page-size cap, and mandatory Id-tiebreaker
/// composition.
/// </summary>
public sealed class AuditingContractTests
{
    // -----------------------------------------------------------------------
    // structural immutability
    // -----------------------------------------------------------------------

    [Fact]
    public void AuditRecord_AllProperties_AreInitOnlyOrHaveNoPublicSetter()
    {
        AssertAllPropertiesAreInitOnly(typeof(AuditRecord));
    }

    [Fact]
    public void AuditEntry_AllProperties_AreInitOnlyOrHaveNoPublicSetter()
    {
        AssertAllPropertiesAreInitOnly(typeof(AuditEntry));
    }

    [Fact]
    public void AuditChainCheckpoint_AllProperties_AreInitOnlyOrHaveNoPublicSetter()
    {
        AssertAllPropertiesAreInitOnly(typeof(AuditChainCheckpoint));
    }

    private static void AssertAllPropertiesAreInitOnly(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var setMethod = property.GetSetMethod(nonPublic: false);
            if (setMethod is null)
                continue; // no public setter at all — trivially immutable from the outside.

            // An `init`-only accessor is compiled with a System.Runtime.CompilerServices.IsExternalInit
            // required custom modifier on its return parameter — this is the only way C# 9+ tells an
            // init setter apart from a plain mutable set at the metadata level.
            var isInitOnly = setMethod.ReturnParameter
                .GetRequiredCustomModifiers()
                    .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");

            isInitOnly.Should().BeTrue(
                $"{type.Name}.{property.Name} must be init-only (or expose no public setter) — " +
                "structural immutability is the entire point of the audit trail's tamper-evidence " +
                "guarantee; a mutable property would let a caller silently rewrite history in memory " +
                "before/instead of ever going through IAuditTrailWriter");
        }
    }

    [Fact]
    public void IAuditTrailWriter_HasExactlyOneMethod_RecordAsync()
    {
        var methods = typeof(IAuditTrailWriter).GetMethods();

        methods.Should().ContainSingle(
            m => m.Name == nameof(IAuditTrailWriter.RecordAsync),
            "IAuditTrailWriter must expose exactly one member — there is structurally no way to " +
            "update or delete an existing AuditRecord through this contract");
    }

    [Fact]
    public void IAuditTrailWriter_HasNo_UpdateDeleteOrRemoveMember()
    {
        var methodNames = typeof(IAuditTrailWriter).GetMethods().Select(m => m.Name).ToList();

        methodNames.Should().NotContain(name =>
            name.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Remove", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Modify", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IAuditQueryService_HasNo_UpdateDeleteOrRemoveMember()
    {
        var methodNames = typeof(IAuditQueryService).GetMethods().Select(m => m.Name).ToList();

        methodNames.Should().NotContain(name =>
            name.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Remove", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Modify", StringComparison.OrdinalIgnoreCase));
    }

    // -----------------------------------------------------------------------
    // AuditResourceHistorySpecification / AuditActorActionsSpecification composition
    // -----------------------------------------------------------------------

    [Fact]
    public void AuditResourceHistorySpecification_Criteria_FiltersOnResourceTypeAndResourceId()
    {
        var tenantId = Guid.NewGuid();
        var spec = new AuditResourceHistorySpecification(
            "Order", "order-1", afterSequence: null, afterId: null, descending: false, take: 10);

        spec.Criteria.Should().NotBeNull();
        var predicate = spec.Criteria!.Compile();

        predicate(MakeRecord(tenantId, "Order", "order-1")).Should().BeTrue();
        predicate(MakeRecord(tenantId, "Order", "order-2")).Should().BeFalse("different ResourceId");
        predicate(MakeRecord(tenantId, "Invoice", "order-1")).Should().BeFalse("different ResourceType");

        // the specification itself is tenant-agnostic BY DESIGN — tenant scoping is applied
        // by EfAuditQueryService against its own resolved IRequestContext, never accepted here.
        predicate(MakeRecord(Guid.NewGuid(), "Order", "order-1")).Should().BeTrue(
            "the specification alone does not filter by tenant — that is EfAuditQueryService's job");
    }

    [Fact]
    public void AuditResourceHistorySpecification_HasMandatoryIdTiebreaker_KeyedBySequence()
    {
        var spec = new AuditResourceHistorySpecification(
            "Order", "order-1", afterSequence: null, afterId: null, descending: false, take: 10);

        spec.AfterKey.Should().BeNull();
        spec.Descending.Should().BeFalse();
        spec.PageSize.Should().Be(10);
        FluentActions.Invoking(() => new AuditResourceHistorySpecification(
                "Order", "order-1", afterSequence: 5, afterId: null, descending: false, take: 10))
            .Should().Throw<ArgumentException>("a cursor supplies both values or neither");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(AuditQueryLimits.MaxPageSize + 1)]
    public void AuditResourceHistorySpecification_TakeOutsideAllowedRange_Throws(int take)
    {
        var act = () => new AuditResourceHistorySpecification(
            "Order", "order-1", afterSequence: null, afterId: null, descending: false, take);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AuditResourceHistorySpecification_TakeAtMaxPageSize_DoesNotThrow()
    {
        var spec = new AuditResourceHistorySpecification(
            "Order", "order-1", afterSequence: null, afterId: null, descending: false,
            take: AuditQueryLimits.MaxPageSize);

        spec.PageSize.Should().Be(AuditQueryLimits.MaxPageSize);
    }

    [Fact]
    public void AuditActorActionsSpecification_Criteria_FiltersOnActor()
    {
        var tenantId = Guid.NewGuid();
        var spec = new AuditActorActionsSpecification(
            "actor-1", afterKey: null, afterId: null, descending: false, take: 10);

        spec.Criteria.Should().NotBeNull();
        var predicate = spec.Criteria!.Compile();

        predicate(MakeRecord(tenantId, "Order", "order-1", "actor-1")).Should().BeTrue();
        predicate(MakeRecord(tenantId, "Order", "order-1", "actor-2")).Should().BeFalse("different ActorId");
    }

    [Fact]
    public void AuditActorActionsSpecification_HasMandatoryIdTiebreaker_EvenWhenDescending()
    {
        var spec = new AuditActorActionsSpecification(
            "actor-1", afterKey: null, afterId: null, descending: true, take: 10);

        spec.Descending.Should().BeTrue();
        spec.AfterKey.Should().BeNull();
        spec.AfterId.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(AuditQueryLimits.MaxPageSize + 1)]
    public void AuditActorActionsSpecification_TakeOutsideAllowedRange_Throws(int take)
    {
        var act = () => new AuditActorActionsSpecification(
            "actor-1", afterKey: null, afterId: null, descending: false, take);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BothSpecifications_Skip_IsAlwaysZero_KeysetPaginationNeverUsesOffset()
    {
        var resourceSpec = new AuditResourceHistorySpecification(
            "Order", "order-1", afterSequence: null, afterId: null, descending: false, take: 10);
        var actorSpec = new AuditActorActionsSpecification(
            "actor-1", afterKey: null, afterId: null, descending: false, take: 10);

        // P-558: the cursor paging lives at the call site (ToKeysetPage); the specifications carry the
        // filter and the cursor, never Skip/Take or an ordering.
        resourceSpec.Skip.Should().BeNull();
        resourceSpec.OrderBy.Should().BeNull();
        actorSpec.Take.Should().BeNull();
        resourceSpec.PageSize.Should().Be(10);
        actorSpec.PageSize.Should().Be(10);
    }

    private static AuditRecord MakeRecord(
        Guid? tenantId, string resourceType, string resourceId, string actorId = "actor-1") =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorId = actorId,
            ActorKind = ActorKind.User,
            Action = "Test",
            ResourceType = resourceType,
            ResourceId = resourceId,
            Sequence = 1,
            OccurredOn = DateTimeOffset.UtcNow,
            Outcome = AuditOutcome.Succeeded,
            HashAlgorithm = "HMAC-SHA256",
            SchemaVersion = 1,
            KeyId = "test",
            RecordHash = "hash",
        };
}
