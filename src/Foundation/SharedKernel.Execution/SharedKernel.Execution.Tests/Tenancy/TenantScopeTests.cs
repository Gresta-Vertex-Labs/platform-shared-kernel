using FluentAssertions;
using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Execution.Tests.Tenancy;

public sealed class TenantScopeTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"));

    [Fact]
    public void Global_IsTheDefaultValue()
    {
        TenantScope.Global.Should().Be(default(TenantScope));
        TenantScope.Global.IsGlobal.Should().BeTrue();
        TenantScope.Global.Tenant.Should().BeNull();
    }

    [Fact]
    public void For_CarriesTheTenant()
    {
        var scope = TenantScope.For(Tenant);

        scope.IsGlobal.Should().BeFalse();
        scope.Tenant.Should().Be(Tenant);
        scope.Should().Be(TenantScope.For(Tenant));
    }

    [Fact]
    public void For_RejectsDefaultTenantId()
    {
        var act = () => TenantScope.For(default);

        act.Should().Throw<ArgumentException>().WithParameterName("tenant");
    }

    [Fact]
    public void FromNullable_MapsNullToGlobal()
    {
        TenantScope.FromNullable(null).Should().Be(TenantScope.Global);
        TenantScope.FromNullable(Tenant).Should().Be(TenantScope.For(Tenant));
    }

    [Fact]
    public void ToString_NamesTheTenantOrGlobal()
    {
        TenantScope.Global.ToString().Should().Be("global");
        TenantScope.For(Tenant).ToString().Should().Be(Tenant.ToString());
    }
}
