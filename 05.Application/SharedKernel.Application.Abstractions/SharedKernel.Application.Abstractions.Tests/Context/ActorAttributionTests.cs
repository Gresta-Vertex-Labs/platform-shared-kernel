using FluentAssertions;
using SharedKernel.Application.Context;

namespace SharedKernel.Application.Abstractions.Tests.Context;

/// <summary>
/// The attribution members persistence reads off <see cref="IRequestContext"/>: an implementation
/// written against the original four members keeps compiling and gets sensible defaults.
/// </summary>
public sealed class ActorAttributionTests
{
    private sealed class MinimalContext(bool isAuthenticated) : IRequestContext
    {
        public bool IsAuthenticated => isAuthenticated;
        public string? UserId => isAuthenticated ? "user-1" : null;
        public Guid? TenantId => null;

        public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken)
            => ValueTask.FromResult(false);
    }

    [Theory]
    [InlineData(true, ActorKind.User)]
    [InlineData(false, ActorKind.Anonymous)]
    public void ActorKind_DefaultsFromIsAuthenticated(bool isAuthenticated, ActorKind expected)
    {
        IRequestContext context = new MinimalContext(isAuthenticated);

        context.ActorKind.Should().Be(expected);
    }

    [Fact]
    public void ClientSessionAndImpersonator_DefaultToNull()
    {
        IRequestContext context = new MinimalContext(isAuthenticated: true);

        context.ClientId.Should().BeNull();
        context.SessionId.Should().BeNull();
        context.ImpersonatorId.Should().BeNull();
    }

    [Fact]
    public void Anonymous_IsAttributedToNobody_NeverToTheSystem_WithNoTenant()
    {
        IRequestContext context = AnonymousRequestContext.Instance;

        context.ActorKind.Should().Be(ActorKind.Anonymous);
        context.TenantId.Should().BeNull("an unresolved tenant must fail closed");
    }

    [Fact]
    public void SystemContext_IsAttributedToTheSystem_UnderItsIdentity()
    {
        IRequestContext context = new SystemRequestContext([], identity: "nightly-job");

        context.ActorKind.Should().Be(ActorKind.System);
        context.UserId.Should().Be("nightly-job");
    }
}
