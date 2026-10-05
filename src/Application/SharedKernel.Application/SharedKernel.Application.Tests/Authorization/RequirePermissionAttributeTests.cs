using System.Reflection;
using FluentAssertions;
using SharedKernel.Application.Authorization;

namespace SharedKernel.Application.Tests.Authorization;

/// <summary>The declaration contract of <see cref="RequirePermissionAttribute"/>.</summary>
public sealed class RequirePermissionAttributeTests
{
    [Fact]
    public void Usage_ClassesAndStructs_RepeatableAndInherited()
    {
        var usage = typeof(RequirePermissionAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        usage.ValidOn.Should().Be(AttributeTargets.Class | AttributeTargets.Struct);
        usage.AllowMultiple.Should().BeTrue("several attributes all apply");
        usage.Inherited.Should().BeTrue("a request inherits the permissions of its base type");
    }

    [Fact]
    public void Permissions_KeepsTheValuesInOrder()
        => new RequirePermissionAttribute("orders.read", "orders.admin").Permissions
            .Should().Equal("orders.read", "orders.admin");

    [Fact]
    public void Constructor_NoValue_Throws()
    {
        var act = () => new RequirePermissionAttribute();

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Constructor_BlankValue_Throws(string? permission)
    {
        var act = () => new RequirePermissionAttribute("orders.read", permission!);

        act.Should().Throw<ArgumentException>();
    }
}
