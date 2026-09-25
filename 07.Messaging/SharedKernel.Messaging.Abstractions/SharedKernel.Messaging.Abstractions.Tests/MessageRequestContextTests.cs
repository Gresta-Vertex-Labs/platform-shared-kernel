using FluentAssertions;
using SharedKernel.Execution.Context;
using SharedKernel.Messaging.Abstractions.Context;

namespace SharedKernel.Messaging.Abstractions.Tests;

/// <summary>
/// P-561: <see cref="MessageRequestContext"/> — the publishing caller's identity as rebuilt on the
/// consumer.
/// </summary>
public sealed class MessageRequestContextTests
{
    [Fact]
    public void Constructor_WithTenantAndUser_ExposesBothAndReportsAuthenticated()
    {
        var tenantId = Guid.NewGuid();

        var context = new MessageRequestContext(tenantId, "user-42", ActorKind.User, "web-client");

        context.TenantId.Should().Be(tenantId);
        context.UserId.Should().Be("user-42");
        context.ActorKind.Should().Be(ActorKind.User);
        context.ClientId.Should().Be("web-client");
        context.IsAuthenticated.Should().BeTrue("a subject id is what the publisher asserted");
    }

    [Fact]
    public void Constructor_WithNoUser_ReportsNotAuthenticated()
    {
        var context = new MessageRequestContext(Guid.NewGuid());

        context.IsAuthenticated.Should().BeFalse();
        context.UserId.Should().BeNull();
    }

    /// <summary>
    /// A message that carried no actor kind must not be attributed to a human: the default is the
    /// least-privileged member, not the enum's zero value.
    /// </summary>
    [Fact]
    public void Constructor_WithoutActorKind_DefaultsToAnonymousNotUser()
    {
        var context = new MessageRequestContext(Guid.NewGuid(), "user-42");

        context.ActorKind.Should().Be(ActorKind.Anonymous);
        ActorKind.User.Should().Be(default(ActorKind),
            "User is the enum's zero value, which is exactly why the default must be stated explicitly");
    }

    /// <summary>
    /// A message with no tenant leaves persistence failing closed, which is the same behaviour as
    /// before the identity was propagated at all — never a silent escalation to "all tenants".
    /// </summary>
    [Fact]
    public void Constructor_WithNoTenant_LeavesTenantNull()
    {
        var context = new MessageRequestContext(tenantId: null, "user-42", ActorKind.Service);

        context.TenantId.Should().BeNull();
    }

    /// <summary>
    /// Permissions never travel on a message: a header cannot grant anything, whatever it says.
    /// </summary>
    [Theory]
    [InlineData("orders.write")]
    [InlineData("")]
    [InlineData("*")]
    public async Task HasPermissionAsync_AnyPermission_AlwaysFalse(string permission)
    {
        var context = new MessageRequestContext(Guid.NewGuid(), "user-42", ActorKind.User);

        var granted = await context.HasPermissionAsync(permission, CancellationToken.None);

        granted.Should().BeFalse(
            "a consumer re-resolves permissions from the identity provider; it never trusts the wire");
    }

    /// <summary>
    /// The defaulted members of <see cref="IRequestContext"/> stay at their interface defaults —
    /// they are not carried on the wire, so claiming a value for them would be an invention.
    /// </summary>
    [Fact]
    public void UncarriedMembers_KeepInterfaceDefaults()
    {
        IRequestContext context = new MessageRequestContext(Guid.NewGuid(), "user-42", ActorKind.User);

        context.SessionId.Should().BeNull();
        context.ImpersonatorId.Should().BeNull();
    }

    /// <summary>
    /// The header names are a cross-service wire contract, so their exact values are asserted
    /// rather than referenced — a test that read the constant would pass after a rename that broke
    /// every publisher already deployed.
    /// </summary>
    [Fact]
    public void MessageContextHeaders_CarryTheirPublishedNames()
    {
        MessageContextHeaders.ActorId.Should().Be("x-sk-actor-id");
        MessageContextHeaders.ActorKind.Should().Be("x-sk-actor-kind");
        MessageContextHeaders.ClientId.Should().Be("x-sk-client-id");
    }

    /// <summary>
    /// Every identity header uses the <c>x-sk-</c> prefix, which is what lifts it into a consumer's
    /// structured log scope without per-consumer code.
    /// </summary>
    [Fact]
    public void MessageContextHeaders_AllUseTheCrossCuttingPrefix()
    {
        string[] headers =
        [
            MessageContextHeaders.ActorId,
            MessageContextHeaders.ActorKind,
            MessageContextHeaders.ClientId,
        ];

        headers.Should().OnlyContain(h => h.StartsWith("x-sk-", StringComparison.Ordinal));
    }
}
