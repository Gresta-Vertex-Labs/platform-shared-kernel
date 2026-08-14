using SharedKernel.Security.Abstractions.Abstractions;
using Xunit;

namespace SharedKernel.Security.Abstractions.Tests;

public sealed class SystemUserContextTests
{
    private readonly SystemUserContext _sut = SystemUserContext.Instance;

    [Fact]
    public void UserId_IsGuidEmpty()
    {
        Assert.Equal(Guid.Empty, _sut.UserId);
    }

    [Fact]
    public void IsAuthenticated_IsTrue()
    {
        // System contexts are trusted, non-HTTP execution authorities — legitimately authenticated
        // despite carrying no human subject (corrected invariant, WO-057/P-367).
        Assert.True(_sut.IsAuthenticated);
    }

    [Fact]
    public void IdentityKind_IsSystem()
    {
        Assert.Equal(IdentityKind.System, _sut.IdentityKind);
    }

    [Fact]
    public void IdentityKind_IsDistinguishableFrom_AnonymousUserContext()
    {
        Assert.NotEqual(AnonymousUserContext.Instance.IdentityKind, _sut.IdentityKind);
    }

    [Fact]
    public void Email_IsNull()
    {
        Assert.Null(_sut.Email);
    }

    [Fact]
    public void Username_IsNull()
    {
        Assert.Null(_sut.Username);
    }

    [Fact]
    public void Roles_IsEmpty()
    {
        Assert.Empty(_sut.Roles);
    }

    [Fact]
    public void Permissions_IsEmpty()
    {
        Assert.Empty(_sut.Permissions);
    }

    [Fact]
    public void Claims_IsEmpty()
    {
        Assert.Empty(_sut.Claims);
    }

    [Fact]
    public void HasRole_AlwaysReturnsFalse()
    {
        Assert.False(_sut.HasRole("admin"));
    }

    [Fact]
    public void HasPermission_AlwaysReturnsFalse()
    {
        Assert.False(_sut.HasPermission("orders:read"));
    }

    [Fact]
    public void Instance_IsSingleton()
    {
        Assert.Same(SystemUserContext.Instance, SystemUserContext.Instance);
    }

    [Fact]
    public void ImplementsIUserContext()
    {
        Assert.IsAssignableFrom<IUserContext>(_sut);
    }

    [Fact]
    public void AuthenticationMethods_IsEmpty()
    {
        Assert.Empty(_sut.AuthenticationMethods);
    }

    [Fact]
    public void AuthContextClassReference_IsNull()
    {
        Assert.Null(_sut.AuthContextClassReference);
    }

    [Fact]
    public void AuthTime_IsNull()
    {
        Assert.Null(_sut.AuthTime);
    }

    [Fact]
    public void IsSenderConstrained_IsFalse()
    {
        Assert.False(_sut.IsSenderConstrained);
    }

    [Fact]
    public void WasAuthenticatedWith_AlwaysReturnsFalse()
    {
        Assert.False(_sut.WasAuthenticatedWith("mfa"));
    }

    [Fact]
    public void IsAuthenticationFresherThan_AlwaysReturnsFalse()
    {
        Assert.False(_sut.IsAuthenticationFresherThan(TimeSpan.FromDays(365), DateTimeOffset.UtcNow));
    }
}
