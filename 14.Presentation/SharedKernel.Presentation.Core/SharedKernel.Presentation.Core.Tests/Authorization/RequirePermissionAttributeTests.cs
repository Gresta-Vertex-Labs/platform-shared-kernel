using FluentAssertions;
using SharedKernel.Presentation.Authorization;
using Xunit;

namespace SharedKernel.Presentation.Core.Tests.Authorization;

public class RequirePermissionAttributeTests
{
    [Fact]
    public void Ctor_SinglePermission_StoresPermission()
    {
        var attribute = new RequirePermissionAttribute("orders:read");

        attribute.Permissions.Should().BeEquivalentTo(["orders:read"]);
    }

    [Fact]
    public void Ctor_MultiplePermissions_StoresEveryPermission_ForOrWithinAttributeSemantics()
    {
        var attribute = new RequirePermissionAttribute("orders:read", "orders:write", "orders:cancel");

        attribute.Permissions.Should().BeEquivalentTo(["orders:read", "orders:write", "orders:cancel"]);
        attribute.Permissions.Should().HaveCount(3);
    }

    [Fact]
    public void Ctor_NoPermissions_StoresEmptyCollection()
    {
        var attribute = new RequirePermissionAttribute();

        attribute.Permissions.Should().BeEmpty();
    }

    [Fact]
    public void AttributeUsage_AllowsMultipleAndTargetsMethodOrClass()
    {
        var usage = typeof(RequirePermissionAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.AllowMultiple.Should().BeTrue();
        usage.ValidOn.Should().Be(AttributeTargets.Method | AttributeTargets.Class);
    }
}
