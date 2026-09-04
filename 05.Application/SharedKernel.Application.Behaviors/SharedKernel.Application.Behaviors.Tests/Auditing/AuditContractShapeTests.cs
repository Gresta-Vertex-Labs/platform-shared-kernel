using FluentAssertions;
using SharedKernel.Application.Behaviors.Auditing;

namespace SharedKernel.Application.Behaviors.Tests.Auditing;

/// <summary>
/// Verifies the exact contract shapes of <see cref="IAuditableRequest{TResponse}"/>,
/// <see cref="IAuditTrailWriter"/>, and <see cref="AuditEntry"/> (WO-071, T-74).
/// </summary>
public sealed class AuditContractShapeTests
{
    [Fact]
    public void IAuditableRequest_DeclaresActionResourceTypeResourceIdAsReadOnlyStrings()
    {
        var type = typeof(IAuditableRequest<>);

        var action = type.GetProperty(nameof(IAuditableRequest<object>.Action));
        var resourceType = type.GetProperty(nameof(IAuditableRequest<object>.ResourceType));
        var resourceId = type.GetProperty(nameof(IAuditableRequest<object>.ResourceId));

        action.Should().NotBeNull();
        action!.PropertyType.Should().Be(typeof(string));
        action.CanWrite.Should().BeFalse();

        resourceType.Should().NotBeNull();
        resourceType!.PropertyType.Should().Be(typeof(string));
        resourceType.CanWrite.Should().BeFalse();

        resourceId.Should().NotBeNull();
        resourceId!.PropertyType.Should().Be(typeof(string));
        resourceId.CanWrite.Should().BeFalse();
    }

    [Fact]
    public void IAuditableRequest_DeclaresBeforeSnapshotAsNullableReadOnlyString()
    {
        var property = typeof(IAuditableRequest<>).GetProperty(
            nameof(IAuditableRequest<object>.BeforeSnapshot));

        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(string));
        property.CanWrite.Should().BeFalse();
    }

    [Fact]
    public void IAuditableRequest_DeclaresGetAfterSnapshot_TakingTResponseReturningNullableString()
    {
        var method = typeof(IAuditableRequest<>).GetMethod(
            nameof(IAuditableRequest<object>.GetAfterSnapshot));

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(string));

        var parameters = method.GetParameters();
        parameters.Should().HaveCount(1);
        parameters[0].ParameterType.Should().Be(typeof(IAuditableRequest<>).GetGenericArguments()[0]);
    }

    [Fact]
    public void IAuditableRequest_HasExactlyFourPropertiesAndOneMethod()
    {
        // Action/ResourceType/ResourceId/BeforeSnapshot properties + GetAfterSnapshot method —
        // mirrors ILoggableRequest<TResponse>'s exact self-supplied-field-surface shape, no more.
        // (GetMembers() also surfaces each property's get_ accessor as a separate MethodInfo, so
        // the raw member count is 4 properties * 2 (PropertyInfo + get_ accessor) + 1 method = 9.)
        var members = typeof(IAuditableRequest<>).GetMembers(
            System.Reflection.BindingFlags.DeclaredOnly
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.Instance);

        members.OfType<System.Reflection.PropertyInfo>().Should().HaveCount(4);
        members.OfType<System.Reflection.MethodInfo>().Should().HaveCount(5); // 4 get_ accessors + GetAfterSnapshot
    }

    [Fact]
    public void IAuditTrailWriter_DeclaresRecordAsync_TakingAuditEntryAndOptionalCancellationToken()
    {
        var method = typeof(IAuditTrailWriter).GetMethod(nameof(IAuditTrailWriter.RecordAsync));

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task));

        var parameters = method.GetParameters();
        parameters.Should().HaveCount(2);
        parameters[0].ParameterType.Should().Be(typeof(AuditEntry));
        parameters[0].Name.Should().Be("entry");
        parameters[1].ParameterType.Should().Be(typeof(CancellationToken));
        parameters[1].HasDefaultValue.Should().BeTrue();
    }

    [Fact]
    public void IAuditTrailWriter_HasExactlyOneMember()
    {
        // Pure local seam, no additional surface — mirrors IUnitOfWork's bridge shape exactly.
        typeof(IAuditTrailWriter).GetMembers(
                System.Reflection.BindingFlags.DeclaredOnly
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance)
            .Should().HaveCount(1);
    }

    [Fact]
    public void AuditEntry_IsASealedRecordWithSixOpaqueMembers()
    {
        typeof(AuditEntry).IsSealed.Should().BeTrue();

        var entry = new AuditEntry("action", "ResourceType", "id", "before", "after", "approval-1");

        entry.Action.Should().Be("action");
        entry.ResourceType.Should().Be("ResourceType");
        entry.ResourceId.Should().Be("id");
        entry.BeforeSnapshot.Should().Be("before");
        entry.AfterSnapshot.Should().Be("after");
        entry.ApprovalId.Should().Be("approval-1");
    }

    [Fact]
    public void AuditEntry_BeforeSnapshotAfterSnapshotAndApprovalId_AreNullable()
    {
        var entry = new AuditEntry("action", "ResourceType", "id", null, null, null);

        entry.BeforeSnapshot.Should().BeNull();
        entry.AfterSnapshot.Should().BeNull();
        entry.ApprovalId.Should().BeNull();
    }

    [Fact]
    public void AuditEntry_DoesNotDeclareIdActorIdTenantIdOccurredOnCorrelationIdOrHashFields()
    {
        // Deliberately smaller than 06.Persistence.Abstractions's richer AuditEntry — every one of
        // those fields is resolved by the REAL writer implementation, never by this local seam.
        var propertyNames = typeof(AuditEntry)
            .GetProperties(System.Reflection.BindingFlags.DeclaredOnly
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        propertyNames.Should().Equal(
            "Action", "ResourceType", "ResourceId", "BeforeSnapshot", "AfterSnapshot", "ApprovalId");
    }
}
