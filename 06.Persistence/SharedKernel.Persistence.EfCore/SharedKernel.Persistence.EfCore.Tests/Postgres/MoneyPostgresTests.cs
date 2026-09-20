using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel.Domain.Monetary;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Tests.Postgres;

/// <summary>
/// <see cref="PgOrderAggregate.Total"/>/<see cref="PgOrderAggregate.DiscountTotal"/>
/// (EF Core 10 complex-type <see cref="Money"/>, two independently queryable columns) against REAL
/// PostgreSQL: SUM/GROUP BY and ORDER BY/WHERE over the nested amount column, round-trip exactness
/// across minor-unit precisions, an over-precision stored amount failing loudly instead of silently
/// re-rounding, a nullable complex property round-tripping null/value/null, and the value object
/// surviving untouched inside a soft-deleted aggregate — mirroring
/// <see cref="SoftDeleteCascadePostgresTests"/>'s existing proof for the same-table owned
/// <see cref="PgOrderAggregate.Address"/> VO, but for a complex-type <see cref="Money"/> property.
/// </summary>
[Collection("EfCorePostgres")]
public sealed class MoneyPostgresTests
{
    private const string DatabaseName = "sk_p557_money";

    private readonly PostgreSqlContainerFixture _fixture;

    public MoneyPostgresTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    private string ConnectionString =>
        new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = DatabaseName }.ConnectionString;

    private PgTestDbContext CreateContext(Guid tenantId) =>
        PgTestDbContextFactory.Create(
            ConnectionString,
            new FakeAuditActorContext("actor"),
            new FakeAuditActorContext("actor", tenantId));

    private static PgOrderAggregate NewOrder(Guid tenantId, string name, string codeSuffix) =>
        new(
            PgOrderId.New(), tenantId, name, $"money-{codeSuffix}-{Guid.NewGuid():N}", "St", "City",
            new SharedKernel.Primitives.Clocks.SystemClock());

    [Fact]
    public async Task RoundTrip_ExactAcrossMinorUnitPrecisions_RealPostgresNumericColumn()
    {
        var tenantId = Guid.NewGuid();

        await using (var setup = CreateContext(tenantId))
            await setup.Database.EnsureCreatedAsync();

        var jpyId = PgOrderId.New();
        var usdId = PgOrderId.New();
        var bhdId = PgOrderId.New();

        await using (var ctx = CreateContext(tenantId))
        {
            var jpy = new PgOrderAggregate(jpyId, tenantId, "JPY", $"money-jpy-{Guid.NewGuid():N}", "St", "City", new SharedKernel.Primitives.Clocks.SystemClock());
            jpy.SetTotal(Money.Create(1500m, Currency.Jpy).Value!);

            var usd = new PgOrderAggregate(usdId, tenantId, "USD", $"money-usd-{Guid.NewGuid():N}", "St", "City", new SharedKernel.Primitives.Clocks.SystemClock());
            usd.SetTotal(Money.Create(1234.56m, Currency.Usd).Value!);

            var bhd = new PgOrderAggregate(bhdId, tenantId, "BHD", $"money-bhd-{Guid.NewGuid():N}", "St", "City", new SharedKernel.Primitives.Clocks.SystemClock());
            bhd.SetTotal(Money.Create(12.345m, Currency.Create("BHD").Value!).Value!);

            ctx.Orders.AddRange(jpy, usd, bhd);
            await ctx.SaveChangesAsync();
        }

        await using var verifyCtx = CreateContext(tenantId);
        (await verifyCtx.Orders.FirstAsync(o => o.Id == jpyId)).Total.Amount.Should().Be(1500m);
        (await verifyCtx.Orders.FirstAsync(o => o.Id == usdId)).Total.Amount.Should().Be(1234.56m);
        (await verifyCtx.Orders.FirstAsync(o => o.Id == bhdId)).Total.Amount.Should().Be(12.345m);
    }

    [Fact]
    public async Task Query_SumGroupedByCurrency_OrderByAmount_WhereAmountGreaterThan_TranslateToRealSql()
    {
        var tenantId = Guid.NewGuid();

        await using (var setup = CreateContext(tenantId))
            await setup.Database.EnsureCreatedAsync();

        await using (var ctx = CreateContext(tenantId))
        {
            var usd10 = NewOrder(tenantId, "USD10", "a");
            usd10.SetTotal(Money.Create(10m, Currency.Usd).Value!);

            var usd30 = NewOrder(tenantId, "USD30", "b");
            usd30.SetTotal(Money.Create(30m, Currency.Usd).Value!);

            var usd5 = NewOrder(tenantId, "USD5", "c");
            usd5.SetTotal(Money.Create(5m, Currency.Usd).Value!);

            var eur20 = NewOrder(tenantId, "EUR20", "d");
            eur20.SetTotal(Money.Create(20m, Currency.Eur).Value!);

            ctx.Orders.AddRange(usd10, usd30, usd5, eur20);
            await ctx.SaveChangesAsync();
        }

        await using var verifyCtx = CreateContext(tenantId);

        // SUM(amount) GROUP BY currency — a real server-side aggregate over the nested complex column.
        var sums = await verifyCtx.Orders
            .Where(o => o.Name.StartsWith("USD") || o.Name.StartsWith("EUR"))
            .GroupBy(o => o.Total.Currency)
            .Select(g => new { Currency = g.Key, Sum = g.Sum(o => o.Total.Amount) })
            .ToListAsync();

        sums.Should().ContainSingle(x => x.Currency == Currency.Usd && x.Sum == 45m);
        sums.Should().ContainSingle(x => x.Currency == Currency.Eur && x.Sum == 20m);

        // ORDER BY amount.
        var ordered = await verifyCtx.Orders
            .Where(o => o.Total.Currency == Currency.Usd)
            .OrderBy(o => o.Total.Amount)
            .Select(o => o.Total.Amount)
            .ToListAsync();
        ordered.Should().Equal(5m, 10m, 30m);

        // WHERE amount > x.
        var above = await verifyCtx.Orders
            .Where(o => o.Total.Currency == Currency.Usd && o.Total.Amount > 10m)
                .CountAsync();
        above.Should().Be(1, "only the 30 USD order exceeds 10");
    }

    [Fact]
    public async Task NullableMoney_RoundTripsNullAndValueAndBackToNull_RealPostgres()
    {
        var tenantId = Guid.NewGuid();
        PgOrderId orderId;

        await using (var setup = CreateContext(tenantId))
            await setup.Database.EnsureCreatedAsync();

        await using (var ctx = CreateContext(tenantId))
        {
            var order = NewOrder(tenantId, "Discountable", "nullable");
            ctx.Orders.Add(order);
            await ctx.SaveChangesAsync();
            orderId = order.Id;
        }

        await using (var ctx = CreateContext(tenantId))
        {
            var loaded = await ctx.Orders.FirstAsync(o => o.Id == orderId);
            loaded.DiscountTotal.Should().BeNull();

            loaded.SetDiscountTotal(Money.Create(2.50m, Currency.Usd).Value!);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = CreateContext(tenantId))
        {
            var withDiscount = await ctx.Orders.FirstAsync(o => o.Id == orderId);
            withDiscount.DiscountTotal.Should().NotBeNull();
            withDiscount.DiscountTotal!.Amount.Should().Be(2.50m);
            withDiscount.DiscountTotal.Currency.Should().Be(Currency.Usd);

            withDiscount.SetDiscountTotal(null);
            await ctx.SaveChangesAsync();
        }

        await using var finalCtx = CreateContext(tenantId);
        var cleared = await finalCtx.Orders.FirstAsync(o => o.Id == orderId);
        cleared.DiscountTotal.Should().BeNull();
    }

    [Fact]
    public async Task Read_StoredAmountWithMoreDecimalPlacesThanCurrencyAllows_ThrowsInsteadOfSilentlyRounding()
    {
        var tenantId = Guid.NewGuid();
        PgOrderId orderId;

        await using (var setup = CreateContext(tenantId))
            await setup.Database.EnsureCreatedAsync();

        await using (var ctx = CreateContext(tenantId))
        {
            var order = NewOrder(tenantId, "Corrupt", "precision");
            order.SetTotal(Money.Create(10m, Currency.Usd).Value!);
            ctx.Orders.Add(order);
            await ctx.SaveChangesAsync();
            orderId = order.Id;
        }

        // Corrupt the stored amount directly, bypassing Money entirely — USD allows 2 minor-unit
        // digits; write 3.
        await using (var corruptCtx = CreateContext(tenantId))
        {
            var rows = await corruptCtx.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE orders SET total_amount = 19.999 WHERE id = {orderId.Value}");
            rows.Should().Be(1, "the corrupting UPDATE must match the seeded row for this test to be meaningful");
        }

        await using var readCtx = CreateContext(tenantId);
        var act = async () => await readCtx.Orders.FirstAsync(o => o.Id == orderId);

        var thrown = await act.Should().ThrowAsync<Exception>();
        thrown.Which.Message.Contains("decimal places", StringComparison.OrdinalIgnoreCase)
            .Should().BeTrue("the failure must name the reason (too many decimal places), never silently re-round");
    }

    [Fact]
    public async Task SoftDelete_MoneyComplexTypeColumns_SurviveUntouched()
    {
        // A complex type has no separate table/identity, so unlike an owned-collection child it was
        // never at risk of EF's cascade-delete fixup marking it Deleted alongside the soft-deleted
        // root — but this proves it concretely rather than by inference, mirroring
        // SoftDeleteCascadePostgresTests' equivalent proof for the same-table owned Address VO.
        var tenantId = Guid.NewGuid();
        PgOrderId orderId;

        await using (var setup = CreateContext(tenantId))
            await setup.Database.EnsureCreatedAsync();

        await using (var ctx = CreateContext(tenantId))
        {
            var order = NewOrder(tenantId, "ToSoftDelete", "softdelete");
            order.SetTotal(Money.Create(99.99m, Currency.Usd).Value!);
            order.SetDiscountTotal(Money.Create(1.11m, Currency.Usd).Value!);
            ctx.Orders.Add(order);
            await ctx.SaveChangesAsync();
            orderId = order.Id;
        }

        await using (var deleteCtx = CreateContext(tenantId))
        {
            var order = await deleteCtx.Orders.FirstAsync(o => o.Id == orderId);
            deleteCtx.Orders.Remove(order);
            await deleteCtx.SaveChangesAsync();
        }

        await using var verifyCtx = CreateContext(tenantId);
        var rescued = await verifyCtx.Orders
            .IgnoreQueryFilters([SharedKernel.Persistence.EfCore.Diagnostics.PersistenceFilterNames.SoftDelete])
                .FirstOrDefaultAsync(o => o.Id == orderId);

        rescued.Should().NotBeNull();
        rescued!.IsDeleted.Should().BeTrue();
        rescued.Total.Amount.Should().Be(99.99m, "the Money complex type's columns must survive the soft-delete completely untouched");
        rescued.Total.Currency.Should().Be(Currency.Usd);
        rescued.DiscountTotal.Should().NotBeNull();
        rescued.DiscountTotal!.Amount.Should().Be(1.11m);
    }

    [Fact]
    public async Task CurrencyColumn_IsFixedLengthThreeCharacters_RealPostgresSchema()
    {
        await using var ctx = CreateContext(Guid.NewGuid());
        await ctx.Database.EnsureCreatedAsync();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT data_type, character_maximum_length FROM information_schema.columns " +
            "WHERE table_name = 'orders' AND column_name = 'total_currency'";
        await using var reader = await command.ExecuteReaderAsync();

        (await reader.ReadAsync()).Should().BeTrue("the total_currency column must exist");
        var dataType = reader.GetString(0);
        var maxLength = reader.GetInt32(1);

        dataType.Should().Be("character", "IsFixedLength() must produce char(3), not varchar(3)");
        maxLength.Should().Be(3);
    }
}
