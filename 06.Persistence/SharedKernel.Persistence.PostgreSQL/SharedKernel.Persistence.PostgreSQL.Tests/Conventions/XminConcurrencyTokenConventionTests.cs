using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.PostgreSQL.Extensions;
using SharedKernel.Persistence.PostgreSQL.Tests.Integration;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions.Abstractions;

namespace SharedKernel.Persistence.PostgreSQL.Tests.Conventions;

/// <summary>
/// WO-051/P-315 (T-64): <see cref="XminConcurrencyTokenConvention"/> model-metadata test — pure EF
/// Core model-building inspection. Building <see cref="DbContext.Model"/> does not require a live
/// database connection (only executing a query does), so this test deliberately does NOT spin up a
/// PostgreSQL Testcontainer — it reuses the already-Testcontainer-covered
/// <see cref="ConcurrencyTestDbContext"/>/<see cref="ConcurrentPgAggregate"/> fixtures from
/// <see cref="ConcurrencyIntegrationTests"/> purely for their model shape.
/// </summary>
public sealed class XminConcurrencyTokenConventionTests
{
    private static ConcurrencyTestDbContext CreateContextWithoutConnecting()
    {
        var builder = new DbContextOptionsBuilder<ConcurrencyTestDbContext>();
        // A syntactically valid Npgsql connection string that is never actually opened — model
        // building is a local, in-memory compilation step independent of connectivity.
        builder.UsePostgreSQL("Host=localhost;Database=xmin_metadata_test;Username=test;Password=test");
        var options = builder.Options;

        var userContext = Substitute.For<IUserContext>();
        userContext.IsAuthenticated.Returns(false);
        userContext.UserId.Returns(Guid.Empty);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        var serviceOptions = Options.Create(new PersistenceServiceOptions());

        var audit = new AuditInterceptor(userContext, clock, serviceOptions);
        var softDelete = new SoftDeleteInterceptor(userContext, clock, serviceOptions);
        var concurrency = new ConcurrencyInterceptor();

        return new ConcurrencyTestDbContext(options, audit, softDelete, concurrency);
    }

    [Fact]
    public void RowVersionProperty_AfterUsePostgreSQL_IsBoundToXminSystemColumn()
    {
        // Arrange
        using var ctx = CreateContextWithoutConnecting();

        // Act — accessing .Model triggers model building/finalization (including
        // XminConcurrencyTokenConvention) without opening a database connection.
        var entityType = ctx.Model.FindEntityType(typeof(ConcurrentPgAggregate));
        entityType.Should().NotBeNull();

        var property = entityType!.FindProperty(nameof(IHasConcurrency.RowVersion));

        // Assert
        property.Should().NotBeNull("IHasConcurrency.RowVersion must be mapped");
        property!.GetColumnName().Should().Be("xmin",
            "XminConcurrencyTokenConvention must bind RowVersion to the real xmin system column");
        property.GetColumnType().Should().Be("xid",
            "the column type must be PostgreSQL's native xid type");
        property.ValueGenerated.Should().Be(ValueGenerated.OnAddOrUpdate,
            "the database supplies a fresh xmin value on both insert and update");
        property.IsConcurrencyToken.Should().BeTrue(
            "the property must remain marked as the EF Core concurrency token");
    }

    [Fact]
    public void RowVersionColumnName_SurvivesSnakeCaseNamingConvention()
    {
        // SnakeCaseNamingConvention would otherwise rename "RowVersion" to "row_version" — the
        // explicit "xmin" column name set by XminConcurrencyTokenConvention must win.
        using var ctx = CreateContextWithoutConnecting();

        var entityType = ctx.Model.FindEntityType(typeof(ConcurrentPgAggregate));
        var property = entityType!.FindProperty(nameof(IHasConcurrency.RowVersion));

        property!.GetColumnName().Should().NotBe("row_version",
            "the snake_case default must be overridden by the explicit xmin binding");
        property.GetColumnName().Should().Be("xmin");
    }

    [Fact]
    public void OtherProperties_StillReceive_SnakeCaseColumnNames()
    {
        // Confirms XminConcurrencyTokenConvention only touches the concurrency-token property —
        // SnakeCaseNamingConvention still applies normally to every other column.
        using var ctx = CreateContextWithoutConnecting();

        var entityType = ctx.Model.FindEntityType(typeof(ConcurrentPgAggregate));
        var nameProperty = entityType!.FindProperty(nameof(ConcurrentPgAggregate.Name));

        nameProperty.Should().NotBeNull();
        nameProperty!.GetColumnName().Should().Be("name");
    }
}
