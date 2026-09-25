using FluentAssertions;
using SharedKernel.Presentation.Authorization;
using Xunit;

namespace SharedKernel.Presentation.Core.Tests.Authorization;

public class RequireRoleAttributeTests
{
    [Fact]
    public void Ctor_SingleRole_StoresRole()
    {
        var attribute = new RequireRoleAttribute("Admin");

        attribute.Roles.Should().BeEquivalentTo(["Admin"]);
    }

    [Fact]
    public void Ctor_MultipleRoles_StoresEveryRole_ForOrWithinAttributeSemantics()
    {
        var attribute = new RequireRoleAttribute("Admin", "Manager", "Owner");

        attribute.Roles.Should().BeEquivalentTo(["Admin", "Manager", "Owner"]);
        attribute.Roles.Should().HaveCount(3);
    }

    [Fact]
    public void Ctor_NoRoles_StoresEmptyCollection()
    {
        var attribute = new RequireRoleAttribute();

        attribute.Roles.Should().BeEmpty();
    }

    [Fact]
    public void AttributeUsage_AllowsMultipleAndTargetsMethodOrClass()
    {
        var usage = typeof(RequireRoleAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.AllowMultiple.Should().BeTrue();
        usage.ValidOn.Should().Be(AttributeTargets.Method | AttributeTargets.Class);
    }
}
