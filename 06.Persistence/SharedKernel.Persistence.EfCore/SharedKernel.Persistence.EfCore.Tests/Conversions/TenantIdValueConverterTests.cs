using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Persistence.EfCore.Conversions;

namespace SharedKernel.Persistence.EfCore.Tests.Conversions;

/// <summary>
/// <see cref="TenantIdValueConverter"/> and the convention that applies it to every <see cref="TenantId"/> property.
/// </summary>
public sealed class TenantIdValueConverterTests
{
    [Fact]
    public void Converter_RoundTripsTheGuid()
    {
        var converter = new TenantIdValueConverter();
        var tenant = new TenantId(Guid.NewGuid());

        converter.ConvertToProvider(tenant).Should().Be(tenant.Value);
        converter.ConvertFromProvider(tenant.Value).Should().Be(tenant);
    }

    [Fact]
    public void Converter_FromAnEmptyGuid_ReturnsTheUnsetTenant_InsteadOfThrowing()
    {
        var converter = new TenantIdValueConverter();

        ((TenantId)converter.ConvertFromProvider(Guid.Empty)!).IsDefault.Should().BeTrue();
    }

    [Fact]
    public void Convention_MapsIHasTenantTenantId_ToAGuidColumn_AndRoundTripsIt()
    {
        var tenant = new TenantId(Guid.NewGuid());
        using var context = TestDbContextFactory.CreateTenantedDbContext(tenant);

        var property = context.Model.FindEntityType(typeof(TenantedTestAggregate))!.FindProperty(nameof(TenantedTestAggregate.TenantId))!;
        property.GetTypeMapping().Converter!.ProviderClrType.Should().Be(typeof(Guid));

        context.TenantedAggregates.Add(new TenantedTestAggregate(TenantedTestId.New(), "row", tenant, TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow)));
        context.SaveChanges();
        context.ChangeTracker.Clear();

        context.TenantedAggregates.Single().TenantId.Should().Be(tenant);
    }
}
