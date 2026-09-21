using FluentAssertions;
using SharedKernel.Persistence.EfCore.Encryption.Crypto;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Unit;

public sealed class AssociatedDataBuilderTests
{
    [Fact]
    public void Build_SamePurposePkTenant_IsDeterministic()
    {
        var pk = Guid.Parse("11111111-1111-1111-1111-111111111111").ToByteArray();
        var tenant = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var a = AssociatedDataBuilder.Build("customer.email", pk, tenant);
        var b = AssociatedDataBuilder.Build("customer.email", pk, tenant);

        a.Should().Equal(b);
    }

    [Fact]
    public void Build_DifferentPurpose_ProducesDifferentAad()
    {
        var pk = Guid.NewGuid().ToByteArray();

        var a = AssociatedDataBuilder.Build("customer.email", pk, null);
        var b = AssociatedDataBuilder.Build("customer.ssn", pk, null);

        a.Should().NotEqual(b);
    }

    [Fact]
    public void Build_DifferentPrimaryKey_ProducesDifferentAad()
    {
        var a = AssociatedDataBuilder.Build("customer.email", Guid.NewGuid().ToByteArray(), null);
        var b = AssociatedDataBuilder.Build("customer.email", Guid.NewGuid().ToByteArray(), null);

        a.Should().NotEqual(b);
    }

    [Fact]
    public void Build_DifferentTenant_ProducesDifferentAad()
    {
        var pk = Guid.NewGuid().ToByteArray();

        var a = AssociatedDataBuilder.Build("customer.ssn", pk, Guid.NewGuid());
        var b = AssociatedDataBuilder.Build("customer.ssn", pk, Guid.NewGuid());

        a.Should().NotEqual(b);
    }

    [Fact]
    public void Build_TenantedVsNonTenanted_ProducesDifferentAad()
    {
        var pk = Guid.NewGuid().ToByteArray();

        var withTenant = AssociatedDataBuilder.Build("customer.ssn", pk, Guid.NewGuid());
        var withoutTenant = AssociatedDataBuilder.Build("customer.ssn", pk, null);

        withTenant.Should().NotEqual(withoutTenant);
    }

    [Fact]
    public void Build_NeverDependsOnAnyPhysicalNameInput_ByConstruction()
    {
        // AssociatedDataBuilder.Build's signature takes only purpose, primary-key bytes and an optional tenant id —
        // there is no table/column/schema parameter at all, so a rename can never change the AAD. This test exists
        // to make that guarantee visible and break loudly if the signature ever grows such a parameter.
        var method = typeof(AssociatedDataBuilder).GetMethod("Build");
        method.Should().NotBeNull();
        method!.GetParameters().Select(p => p.Name).Should().BeEquivalentTo("purpose", "primaryKey", "tenantId");
    }
}
