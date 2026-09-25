using FluentAssertions;
using SharedKernel.Execution.Context;

namespace SharedKernel.Execution.Tests.Context;

/// <summary>
/// <see cref="PropagatedRequestContext"/> — the sending caller's identity as rebuilt on the receiving side of a
/// message, workflow activity or other asynchronous hop (was <c>MessageRequestContext</c>, P-561; moved and
/// generalised by P-566).
/// </summary>
public sealed class PropagatedRequestContextTests
{
    [Fact]
    public void Constructor_WithTenantAndUser_ExposesBothAndReportsAuthenticated()
    {
        var tenantId = new TenantId(Guid.NewGuid());

        var context = new PropagatedRequestContext(tenantId, "user-42", ActorKind.User, "web-client", "corr-1");

        context.TenantId.Should().Be(tenantId);
        context.UserId.Should().Be("user-42");
        context.ActorKind.Should().Be(ActorKind.User);
        context.ClientId.Should().Be("web-client");
        context.CorrelationId.Should().Be("corr-1");
        context.IsAuthenticated.Should().BeTrue("a subject id is what the sender asserted");
    }

    [Fact]
    public void Constructor_WithNoUser_ReportsNotAuthenticated()
    {
        var context = new PropagatedRequestContext(new TenantId(Guid.NewGuid()));

        context.IsAuthenticated.Should().BeFalse();
        context.UserId.Should().BeNull();
        context.CorrelationId.Should().BeNull();
    }

    /// <summary>
    /// A hop that carried no actor kind must not be attributed to a human: the default is the
    /// least-privileged member, not the enum's zero value.
    /// </summary>
    [Fact]
    public void Constructor_WithoutActorKind_DefaultsToAnonymousNotUser()
    {
        var context = new PropagatedRequestContext(new TenantId(Guid.NewGuid()), "user-42");

        context.ActorKind.Should().Be(ActorKind.Anonymous);
        ActorKind.User.Should().Be(default(ActorKind),
            "User is the enum's zero value, which is exactly why the default must be stated explicitly");
    }

    [Fact]
    public void Constructor_WithNoTenant_LeavesTenantNull()
    {
        var context = new PropagatedRequestContext(tenantId: null, "user-42", ActorKind.Service);

        context.TenantId.Should().BeNull();
    }

    /// <summary>Permissions never travel with a hop: a header cannot grant anything, whatever it says.</summary>
    [Theory]
    [InlineData("orders.write")]
    [InlineData("")]
    [InlineData("*")]
    public async Task HasPermissionAsync_AnyPermission_AlwaysFalse(string permission)
    {
        var context = new PropagatedRequestContext(new TenantId(Guid.NewGuid()), "user-42", ActorKind.User);

        var granted = await context.HasPermissionAsync(permission, CancellationToken.None);

        granted.Should().BeFalse("a receiver re-resolves permissions from the identity provider; it never trusts the wire");
    }

    /// <summary>
    /// The defaulted members of <see cref="IRequestContext"/> that are not carried stay at their interface defaults.
    /// </summary>
    [Fact]
    public void UncarriedMembers_KeepInterfaceDefaults()
    {
        IRequestContext context = new PropagatedRequestContext(new TenantId(Guid.NewGuid()), "user-42", ActorKind.User);

        context.SessionId.Should().BeNull();
        context.ImpersonatorId.Should().BeNull();
    }
}
