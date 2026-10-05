using SharedKernel.Execution.Context;
using Xunit;

namespace SharedKernel.Security.Abstractions.Tests;

public sealed class FixedUserContextTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<IUserContext> Contexts => new() { AnonymousUserContext.Instance, SystemUserContext.Instance };

    [Fact]
    public void AnonymousUserContext_KindAndAuthentication_AreAnonymousAndFalse()
    {
        Assert.Equal(ActorKind.Anonymous, AnonymousUserContext.Instance.ActorKind);
        Assert.False(AnonymousUserContext.Instance.IsAuthenticated);
    }

    [Fact]
    public void SystemUserContext_KindAndAuthentication_AreSystemAndTrue()
    {
        Assert.Equal(ActorKind.System, SystemUserContext.Instance.ActorKind);
        Assert.True(SystemUserContext.Instance.IsAuthenticated);
    }

    [Fact]
    public void Instance_RepeatedAccess_ReturnsSameObject()
    {
        Assert.Same(AnonymousUserContext.Instance, AnonymousUserContext.Instance);
        Assert.Same(SystemUserContext.Instance, SystemUserContext.Instance);
    }

    [Fact]
    public void AnonymousActorKind_IsAnonymous()
    {
        Assert.Equal(ActorKind.Anonymous, AnonymousUserContext.Instance.ActorKind);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Identifiers_AreAllNull(IUserContext context)
    {
        Assert.Null(context.SubjectId);
        Assert.Null(context.ClientId);
        Assert.Null(context.TenantId);
        Assert.Null(context.SessionId);
        Assert.Null(context.Name);
        Assert.Null(context.Email);
        Assert.Null(context.AuthContextClassReference);
        Assert.Null(context.AuthTime);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Collections_AreEmpty(IUserContext context)
    {
        Assert.Empty(context.Roles);
        Assert.Empty(context.Permissions);
        Assert.Empty(context.AuthenticationMethods);
        Assert.Empty(context.FindClaims(SecurityClaimTypes.Subject));
        Assert.Empty(context.FindClaims(SecurityClaimTypes.Roles));
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Checks_AllReturnFalse(IUserContext context)
    {
        Assert.False(context.IsSenderConstrained);
        Assert.Null(context.FindClaim(SecurityClaimTypes.Subject));
        Assert.Null(context.FindClaim(SecurityClaimTypes.TenantId));
        Assert.False(context.HasRole("admin"));
        Assert.False(context.HasPermission("orders:write"));
        Assert.False(context.WasAuthenticatedWith("pwd"));
        Assert.False(context.IsAuthenticationFresherThan(TimeSpan.MaxValue, Now));
        Assert.Null(context.GetAuthenticationMethodTime("otp"));
    }

    [Fact]
    public void GetAuthenticationMethodTime_OnTheConcreteTypes_ReturnsNull()
    {
        Assert.Null(AnonymousUserContext.Instance.GetAuthenticationMethodTime("otp"));
        Assert.Null(SystemUserContext.Instance.GetAuthenticationMethodTime("otp"));
    }
}
