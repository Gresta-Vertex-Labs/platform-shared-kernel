using FluentAssertions;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.Behaviors.DualApproval;

namespace SharedKernel.Application.Behaviors.Tests.DualApproval;

/// <summary>
/// Verifies the exact contract shapes of <see cref="IRequiresDualApproval"/>,
/// <see cref="IDualApprovalStore"/>, and <see cref="IAuthorizationContextIdentity"/> (WO-058, T-69).
/// </summary>
public sealed class DualApprovalContractShapeTests
{
    [Fact]
    public void IRequiresDualApproval_DeclaresApprovalKeyStringProperty()
    {
        var property = typeof(IRequiresDualApproval).GetProperty(nameof(IRequiresDualApproval.ApprovalKey));

        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(string));
        property.CanRead.Should().BeTrue();
        property.CanWrite.Should().BeFalse("ApprovalKey is a self-supplied, read-only marker — mirroring IIdempotentRequest.IdempotencyKey");
    }

    [Fact]
    public void IDualApprovalStore_DeclaresTryGetApprovalAsync_ReturningNullableString()
    {
        var method = typeof(IDualApprovalStore).GetMethod(nameof(IDualApprovalStore.TryGetApprovalAsync));

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<string?>));

        var parameters = method.GetParameters();
        parameters.Should().HaveCount(2);
        parameters[0].ParameterType.Should().Be(typeof(string));
        parameters[0].Name.Should().Be("approvalKey");
        parameters[1].ParameterType.Should().Be(typeof(CancellationToken));
    }

    [Fact]
    public void IDualApprovalStore_DeclaresRecordApprovalAsync_TakingApprovalKeyAndApproverIdentity()
    {
        var method = typeof(IDualApprovalStore).GetMethod(nameof(IDualApprovalStore.RecordApprovalAsync));

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task));

        var parameters = method.GetParameters();
        parameters.Should().HaveCount(3);
        parameters[0].ParameterType.Should().Be(typeof(string));
        parameters[0].Name.Should().Be("approvalKey");
        parameters[1].ParameterType.Should().Be(typeof(string));
        parameters[1].Name.Should().Be("approverIdentity");
        parameters[2].ParameterType.Should().Be(typeof(CancellationToken));
    }

    [Fact]
    public void IDualApprovalStore_HasExactlyTwoMembers()
    {
        // Pure local seam, no additional surface — mirrors IIdempotencyKeyStore's bridge shape exactly.
        typeof(IDualApprovalStore).GetMembers(
                System.Reflection.BindingFlags.DeclaredOnly
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance)
            .Should().HaveCount(2);
    }

    [Fact]
    public void IAuthorizationContextIdentity_DeclaresGetCurrentIdentityAsync_ReturningString()
    {
        var method = typeof(IAuthorizationContextIdentity).GetMethod(
            nameof(IAuthorizationContextIdentity.GetCurrentIdentityAsync));

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<string>));

        var parameters = method.GetParameters();
        parameters.Should().HaveCount(1);
        parameters[0].ParameterType.Should().Be(typeof(CancellationToken));
    }

    [Fact]
    public void IAuthorizationContextIdentity_IsASeparateInterface_NeverAModificationOfIAuthorizationContext()
    {
        // WO-058: added as a NEW, SEPARATE interface — never a breaking change to the already-
        // published IAuthorizationContext contract (mirrors IIdempotencyResponseStore's precedent).
        typeof(IAuthorizationContextIdentity).Should().NotBeAssignableTo<IAuthorizationContext>();
        typeof(IAuthorizationContext).Should().NotBeAssignableTo<IAuthorizationContextIdentity>();

        // IAuthorizationContext's own shipped members are unchanged by this addition.
        typeof(IAuthorizationContext).GetMembers(
                System.Reflection.BindingFlags.DeclaredOnly
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance)
            .Should().HaveCount(3, "IsAuthorizedAsync/AllOf/AnyOf — unchanged by the WO-058 addition");
    }
}
