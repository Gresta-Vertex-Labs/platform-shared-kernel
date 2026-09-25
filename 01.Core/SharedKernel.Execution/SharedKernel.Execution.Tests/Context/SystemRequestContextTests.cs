using FluentAssertions;
using SharedKernel.Execution.Context;

namespace SharedKernel.Execution.Tests.Context;

public sealed class SystemRequestContextTests
{
    [Fact]
    public void IsAuthenticated_AlwaysTrue()
    {
        var context = new SystemRequestContext([]);

        context.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void UserId_DefaultsToSystem()
    {
        var context = new SystemRequestContext([]);

        context.UserId.Should().Be("system");
    }

    [Fact]
    public void UserId_UsesCallerSuppliedIdentity()
    {
        var context = new SystemRequestContext([], identity: "temporal-worker");

        context.UserId.Should().Be("temporal-worker");
    }

    [Fact]
    public void TenantId_DefaultsToNull()
    {
        var context = new SystemRequestContext([]);

        context.TenantId.Should().BeNull();
    }

    [Fact]
    public void TenantId_UsesCallerSuppliedValue()
    {
        var tenantId = Guid.NewGuid();
        var context = new SystemRequestContext([], tenantId: tenantId);

        context.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task HasPermissionAsync_EmptyPermissionSet_NeverGrantsAnyPermission()
    {
        var context = new SystemRequestContext([]);

        var result = await context.HasPermissionAsync("anything.at.all", CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_DeclaredPermission_ReturnsTrue()
    {
        var context = new SystemRequestContext(["jobs.run"]);

        var result = await context.HasPermissionAsync("jobs.run", CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_UndeclaredPermission_ReturnsFalse()
    {
        var context = new SystemRequestContext(["jobs.run"]);

        var result = await context.HasPermissionAsync("jobs.delete", CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public void Constructor_NullPermissions_Throws()
    {
        var act = () => new SystemRequestContext(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_NullOrWhitespaceIdentity_Throws(string? identity)
    {
        var act = () => new SystemRequestContext([], identity: identity!);

        act.Should().Throw<ArgumentException>();
    }
}
