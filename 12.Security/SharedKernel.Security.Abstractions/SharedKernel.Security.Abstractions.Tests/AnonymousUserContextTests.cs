using SharedKernel.Security.Abstractions.Abstractions;
using Xunit;

namespace SharedKernel.Security.Abstractions.Tests;

public sealed class AnonymousUserContextTests
{
    private readonly AnonymousUserContext _sut = new();

    [Fact]
    public void UserId_IsGuidEmpty()
    {
        Assert.Equal(Guid.Empty, _sut.UserId);
    }

    [Fact]
    public void IsAuthenticated_IsFalse()
    {
        Assert.False(_sut.IsAuthenticated);
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
    public void Claims_IsEmpty()
    {
        Assert.Empty(_sut.Claims);
    }

    [Fact]
    public void Permissions_IsEmpty()
    {
        Assert.Empty(_sut.Permissions);
    }

    [Fact]
    public void IdentityKind_IsAnonymous()
    {
        Assert.Equal(IdentityKind.Anonymous, _sut.IdentityKind);
    }

    [Fact]
    public void HasRole_AlwaysReturnsFalse_ForAnyRole()
    {
        Assert.False(_sut.HasRole("admin"));
        Assert.False(_sut.HasRole("ADMIN"));
        Assert.False(_sut.HasRole("user"));
        Assert.False(_sut.HasRole(string.Empty));
    }

    [Fact]
    public void HasPermission_AlwaysReturnsFalse_ForAnyPermission()
    {
        Assert.False(_sut.HasPermission("orders:read"));
        Assert.False(_sut.HasPermission("ORDERS:READ"));
        Assert.False(_sut.HasPermission(string.Empty));
    }

    [Fact]
    public void Instance_IsSameType()
    {
        Assert.IsType<AnonymousUserContext>(AnonymousUserContext.Instance);
    }

    [Fact]
    public void ImplementsIUserContext()
    {
        Assert.IsAssignableFrom<IUserContext>(_sut);
    }
}
