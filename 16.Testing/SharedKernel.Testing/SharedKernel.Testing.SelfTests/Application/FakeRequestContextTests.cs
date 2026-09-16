using SharedKernel.Testing.Application;

namespace SharedKernel.Testing.SelfTests.Application;

public sealed class FakeRequestContextTests
{
    [Fact]
    public void IsAuthenticated_DefaultsToTrue()
    {
        var context = new FakeRequestContext();

        Assert.True(context.IsAuthenticated);
    }

    [Fact]
    public void UserId_DefaultsToFixedNonEmptyValue()
    {
        var context = new FakeRequestContext();

        Assert.False(string.IsNullOrWhiteSpace(context.UserId));
    }

    [Fact]
    public void TenantId_DefaultsToNull()
    {
        var context = new FakeRequestContext();

        Assert.Null(context.TenantId);
    }

    [Fact]
    public void TenantId_Settable()
    {
        var tenantId = Guid.NewGuid();
        var context = new FakeRequestContext { TenantId = tenantId };

        Assert.Equal(tenantId, context.TenantId);
    }

    [Fact]
    public void IsAuthenticated_Settable()
    {
        var context = new FakeRequestContext { IsAuthenticated = false, UserId = null };

        Assert.False(context.IsAuthenticated);
        Assert.Null(context.UserId);
    }

    [Fact]
    public async Task HasPermissionAsync_UnconfiguredPermission_ReturnsFalse()
    {
        var context = new FakeRequestContext();

        var result = await context.HasPermissionAsync("orders:create", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task HasPermissionAsync_GrantedPermission_ReturnsTrue()
    {
        var context = new FakeRequestContext { Permissions = ["orders:create"] };

        var result = await context.HasPermissionAsync("orders:create", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task HasPermissionAsync_ComparisonIsCaseInsensitive()
    {
        var context = new FakeRequestContext { Permissions = ["Orders:Create"] };

        var result = await context.HasPermissionAsync("orders:create", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public void HasPermission_MirrorsHasPermissionAsync()
    {
        var context = new FakeRequestContext { Permissions = ["orders:create"] };

        Assert.True(context.HasPermission("orders:create"));
        Assert.False(context.HasPermission("orders:delete"));
    }

    [Fact]
    public async Task HasPermissionAsync_NullPermission_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new FakeRequestContext().HasPermissionAsync(null!, CancellationToken.None));
}
