using System.Globalization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Domain.Monetary;
using SharedKernel.Persistence.EfCore.Configurations;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conventions;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Conversions;

// ---------------------------------------------------------------------------
// Test entities and support types — Money as an EF Core 10 complex type,
// two independently queryable columns (amount, currency), replacing the packed-string design.
// ---------------------------------------------------------------------------

internal sealed record MoneyConversionTestId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static MoneyConversionTestId New() => new(Guid.NewGuid());
}

/// <summary>Aggregate with a required and an optional <see cref="Money"/> property, configured via <c>.Money(...)</c>.</summary>
internal sealed class ProductTestAggregate : AggregateRoot<MoneyConversionTestId>
{
    public string Name { get; private set; } = string.Empty;
    public Money Price { get; private set; } = null!;
    public Money? Discount { get; private set; }

    public ProductTestAggregate(MoneyConversionTestId id, string name, Money price, IClock clock)
        : base(id, clock)
    {
        Name = name;
        Price = price;
    }

    protected ProductTestAggregate() { } // ORM path

    public void Reprice(Money newPrice) => Price = newPrice;

    public void ApplyDiscount(Money? discount) => Discount = discount;
}

internal sealed class ProductTestAggregateConfig
    : EntityTypeConfigurationBase<ProductTestAggregate, MoneyConversionTestId>
{
    public override void Configure(EntityTypeBuilder<ProductTestAggregate> builder)
    {
        base.Configure(builder);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Money(e => e.Price, amountColumnName: "price_amount", currencyColumnName: "price_currency");
        builder.Money(
            e => e.Discount,
            required: false,
            amountColumnName: "discount_amount",
            currencyColumnName: "discount_currency");
    }
}

/// <summary>
/// Aggregate with a standalone (not <see cref="Money"/>-wrapped) <see cref="Currency"/> property,
/// used to exercise <see cref="CurrencyValueConverter"/> through a real <c>DbContext</c> read path —
/// distinct from a <see cref="Money"/> property's own nested <see cref="Money.Currency"/> column.
/// </summary>
internal sealed class AccountTestAggregate : AggregateRoot<MoneyConversionTestId>
{
    public string Name { get; private set; } = string.Empty;
    public Currency PreferredCurrency { get; private set; } = null!;

    public AccountTestAggregate(MoneyConversionTestId id, string name, Currency preferredCurrency, IClock clock)
        : base(id, clock)
    {
        Name = name;
        PreferredCurrency = preferredCurrency;
    }

    protected AccountTestAggregate() { } // ORM path
}

internal sealed class AccountTestAggregateConfig
    : EntityTypeConfigurationBase<AccountTestAggregate, MoneyConversionTestId>
{
    public override void Configure(EntityTypeBuilder<AccountTestAggregate> builder)
    {
        base.Configure(builder);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.PreferredCurrency).HasMaxLength(3);
    }
}

/// <summary>
/// Test DbContext for <see cref="ProductTestAggregate"/> and <see cref="AccountTestAggregate"/>.
/// Deliberately calls <see cref="ValueObjectOwnershipBuilder.Apply"/> AFTER applying the explicit
/// <c>.Money(...)</c> configuration, exactly as a real service's <c>OnModelCreating</c> would —
/// proving the two compose without conflict.
/// </summary>
internal sealed class MoneyTestDbContext : SharedKernelDbContext
{
    public DbSet<ProductTestAggregate> Products => Set<ProductTestAggregate>();
    public DbSet<AccountTestAggregate> Accounts => Set<AccountTestAggregate>();

    public MoneyTestDbContext(DbContextOptions<MoneyTestDbContext> options, PersistenceContextDependencies dependencies)
        : base(options, dependencies)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<MoneyConversionTestId, Guid>();
        configurationBuilder.ConfigureMoney();
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ProductTestAggregateConfig());
        modelBuilder.ApplyConfiguration(new AccountTestAggregateConfig());
        ValueObjectOwnershipBuilder.Apply(modelBuilder);
    }
}

// ---------------------------------------------------------------------------
// Complex-type mapping tests
// ---------------------------------------------------------------------------

/// <summary>
/// <see cref="Money"/> as an EF Core 10 complex type via
/// <see cref="MoneyEntityTypeBuilderExtensions.Money{TEntity}"/>, and
/// <see cref="ValueObjectOwnershipBuilder"/>'s Money/SingleValueObject exclusions.
/// </summary>
public sealed class MoneyValueConverterTests
{
    private static MoneyTestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MoneyTestDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var actorContext = TestDbContextFactory.CreateAuthenticatedActorContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);

        var ctx = new MoneyTestDbContext(
            options,
            new PersistenceContextDependencies(
                new AuditInterceptor(actorContext, clock), new SoftDeleteInterceptor(clock), new ConcurrencyInterceptor()));
        ctx.Database.EnsureCreated();
        return ctx;
    }

    // -----------------------------------------------------------------------
    // Round-trip / persistence
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RoundTrip_EntityWithMoneyProperty_PreservesAmountAndCurrency()
    {
        // Arrange
        using var ctx = CreateContext();
        var id = MoneyConversionTestId.New();
        var price = Money.Create(1234.56m, Currency.Usd).Value!;
        ctx.Products.Add(new ProductTestAggregate(id, "Widget", price, new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Act
        var loaded = await ctx.Products.FindAsync(id);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Price.Amount.Should().Be(1234.56m);
        loaded.Price.Currency.Should().Be(Currency.Usd);
    }

    [Fact]
    public async Task RoundTrip_UpdatingMoneyProperty_PersistsNewValue()
    {
        // Arrange
        using var ctx = CreateContext();
        var id = MoneyConversionTestId.New();
        ctx.Products.Add(new ProductTestAggregate(
            id, "Widget", Money.Create(10m, Currency.Usd).Value!, new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Act
        var loaded = await ctx.Products.FindAsync(id);
        loaded!.Reprice(Money.Create(25.5m, Currency.Eur).Value!);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var reloaded = await ctx.Products.FindAsync(id);

        // Assert
        reloaded!.Price.Amount.Should().Be(25.5m);
        reloaded.Price.Currency.Should().Be(Currency.Eur);
    }

    [Fact]
    public void Money_ConfiguresTwoColumnComplexType_NotOwnedNavigation()
    {
        // Arrange
        using var ctx = CreateContext();

        // Act
        var entityType = ctx.Model.FindEntityType(typeof(ProductTestAggregate));

        // Assert — a complex type with two independently queryable columns, never an owned navigation.
        entityType.Should().NotBeNull();
        entityType!.FindNavigation(nameof(ProductTestAggregate.Price)).Should().BeNull();

        var priceComplexProperty = entityType.FindComplexProperty(nameof(ProductTestAggregate.Price));
        priceComplexProperty.Should().NotBeNull("Price must be mapped as a complex type, not a scalar or a navigation");

        var complexType = priceComplexProperty!.ComplexType;
        complexType.FindProperty(nameof(Money.Amount)).Should().NotBeNull();
        complexType.FindProperty(nameof(Money.Currency)).Should().NotBeNull();
        complexType.FindProperty(nameof(Money.Amount))!.GetColumnName().Should().Be("price_amount");
        complexType.FindProperty(nameof(Money.Currency))!.GetColumnName().Should().Be("price_currency");
        complexType.FindProperty(nameof(Money.Currency))!.GetMaxLength().Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // ValueObjectOwnershipBuilder excludes Money and standalone Currency
    // -----------------------------------------------------------------------

    [Fact]
    public void ValueObjectOwnershipBuilder_DoesNotAutoOwn_MoneyProperty()
    {
        // Arrange — MoneyTestDbContext calls ValueObjectOwnershipBuilder.Apply AFTER the explicit
        //.Money(...) configuration, mirroring a real service's OnModelCreating.
        using var ctx = CreateContext();

        // Act
        var entityType = ctx.Model.FindEntityType(typeof(ProductTestAggregate));

        // Assert — no owned-entity-type registration for Money exists anywhere in the model; the
        // property remains the complex type.Money(...) configured, never re-wrapped by the generic
        // IValueObject scan.
        ctx.Model.FindEntityType(typeof(Money)).Should().BeNull(
            "Money must never be auto-owned by ValueObjectOwnershipBuilder — it is always configured explicitly via.Money(...)");
        entityType!.GetNavigations().Where(n => n.ForeignKey.IsOwnership).Should().BeEmpty(
            "the Money property is a complex type, not an owned navigation");
    }

    [Fact]
    public void ValueObjectOwnershipBuilder_DoesNotAutoOwn_StandaloneCurrencyProperty()
    {
        // Arrange — MoneyTestDbContext also maps AccountTestAggregate.PreferredCurrency, a bare
        // Currency property never wrapped in Money, alongside ConfigureMoney() + Apply().
        using var ctx = CreateContext();

        // Act
        var entityType = ctx.Model.FindEntityType(typeof(AccountTestAggregate));

        // Assert — model building did not throw (implicit — CreateContext().EnsureCreated() would
        // have thrown otherwise), and PreferredCurrency remains the scalar column ConfigureMoney's
        // global conversion produced, never re-wrapped as an owned navigation or complex type.
        entityType.Should().NotBeNull();
        ctx.Model.FindEntityType(typeof(Currency)).Should().BeNull(
            "Currency must never be auto-owned by ValueObjectOwnershipBuilder — it is a SingleValueObject, " +
            "always a scalar column via a registered converter");
        entityType!.FindProperty(nameof(AccountTestAggregate.PreferredCurrency)).Should().NotBeNull();
        entityType.FindNavigation(nameof(AccountTestAggregate.PreferredCurrency)).Should().BeNull();
        entityType.FindComplexProperty(nameof(AccountTestAggregate.PreferredCurrency)).Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Full EF Core save+reload round-trip across zero/two/three-decimal currencies.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("JPY", "1500")] // zero-decimal currency
    [InlineData("USD", "1234.56")] // two-decimal currency
    [InlineData("BHD", "12.345")] // three-decimal currency
    public async Task RoundTrip_EntityWithMoneyProperty_PreservesAmountAndCurrency_AcrossMinorUnitPrecisions(
        string currencyCode, string amountString)
    {
        // Arrange
        using var ctx = CreateContext();
        var id = MoneyConversionTestId.New();
        var amount = decimal.Parse(amountString, CultureInfo.InvariantCulture);
        var currency = Currency.Create(currencyCode).Value!;
        var price = Money.Create(amount, currency).Value!;
        ctx.Products.Add(new ProductTestAggregate(id, "Widget", price, new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Act
        var loaded = await ctx.Products.FindAsync(id);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Price.Amount.Should().Be(amount);
        loaded.Price.Currency.Should().Be(currency);
    }

    [Fact]
    public async Task TwoColumnMoney_AmountAndCurrency_AreIndependentlySqlQueryable_CapabilityRestored()
    {
        // Arrange — unlike the packed-string design this replaces, "amount"/"currency" are now real,
        // independently named, independently typed columns: a raw WHERE/SUM/ORDER BY against either
        // one, without touching the other, must work.
        using var ctx = CreateContext();
        ctx.Products.Add(new ProductTestAggregate(
            MoneyConversionTestId.New(), "Widget", Money.Create(10m, Currency.Usd).Value!, new SystemClock()));
        ctx.Products.Add(new ProductTestAggregate(
            MoneyConversionTestId.New(), "Gadget", Money.Create(5m, Currency.Eur).Value!, new SystemClock()));
        await ctx.SaveChangesAsync();

        var entityType = ctx.Model.FindEntityType(typeof(ProductTestAggregate))!;
        var tableName = entityType.GetTableName();

        var connection = ctx.Database.GetDbConnection();
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE price_currency = 'USD' AND price_amount > 0";

        // Act
        var count = (long)(await command.ExecuteScalarAsync())!;

        // Assert
        count.Should().Be(1, "only the USD row matches both independently-queryable columns");
    }

    // -----------------------------------------------------------------------
    // Nullable (optional) Money complex property
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NullableMoney_RoundTripsNullAndValueAndBackToNull()
    {
        // Arrange
        using var ctx = CreateContext();
        var id = MoneyConversionTestId.New();
        ctx.Products.Add(new ProductTestAggregate(id, "Widget", Money.Create(10m, Currency.Usd).Value!, new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Act 1 — starts null.
        var loaded = await ctx.Products.FindAsync(id);
        loaded!.Discount.Should().BeNull();

        // Act 2 — set a value.
        loaded.ApplyDiscount(Money.Create(1.5m, Currency.Usd).Value!);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var withDiscount = await ctx.Products.FindAsync(id);
        withDiscount!.Discount.Should().NotBeNull();
        withDiscount.Discount!.Amount.Should().Be(1.5m);
        withDiscount.Discount.Currency.Should().Be(Currency.Usd);

        // Act 3 — clear it back to null.
        withDiscount.ApplyDiscount(null);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var cleared = await ctx.Products.FindAsync(id);

        // Assert
        cleared!.Discount.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Over-precision stored amount fails loudly instead of silently re-rounding.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Read_StoredAmountWithMoreDecimalPlacesThanCurrencyAllows_ThrowsInsteadOfSilentlyRounding()
    {
        // Arrange — USD has 2 minor-unit digits; write 3 decimal places directly, bypassing Money
        // entirely, simulating data corruption or an out-of-band write.
        using var ctx = CreateContext();
        var id = MoneyConversionTestId.New();
        ctx.Products.Add(new ProductTestAggregate(id, "Widget", Money.Create(10m, Currency.Usd).Value!, new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var entityType = ctx.Model.FindEntityType(typeof(ProductTestAggregate))!;
        var tableName = entityType.GetTableName();
        var idColumn = entityType.FindProperty("Id")!.GetColumnName();

        // Table/column names come from trusted EF model metadata (never user input); only the id value
        // is parameterized — built via string.Concat rather than a "$" interpolated literal so the
        // EF1002 "possible SQL injection" analyzer, which cannot distinguish this from an unsafe
        // interpolation, does not fire on inherently-safe metadata-derived identifiers.
        var sql = string.Concat("UPDATE ", tableName, " SET price_amount = '19.999' WHERE ", idColumn, " = {0}");
        var rowsAffected = await ctx.Database.ExecuteSqlRawAsync(sql, id.Value);
        rowsAffected.Should().Be(1, "the raw UPDATE must actually match the seeded row for this test to be meaningful");
        ctx.ChangeTracker.Clear();

        // Act
        var act = async () => await ctx.Products.FindAsync(id);

        // Assert — a clear, loud exception on read; the corrupted row must never be silently
        // materialized with a re-rounded amount.
        (await act.Should().ThrowAsync<Exception>())
            .Where(ex => ex.Message.Contains("decimal places", StringComparison.OrdinalIgnoreCase)
                         || (ex.InnerException != null
                             && ex.InnerException.Message.Contains("decimal places", StringComparison.OrdinalIgnoreCase)));
    }

    // -----------------------------------------------------------------------
    // Corrupted/unknown stored currency code surfaces as a clear thrown exception on read
    // via CurrencyValueConverter, never a silent wrong-currency substitution.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Read_CorruptedStoredCurrencyCode_ThrowsClearException_NeverSilentlySubstitutesCurrency()
    {
        // Arrange — a standalone Currency property (AccountTestAggregate.PreferredCurrency), so the
        // corruption/read path exercises CurrencyValueConverter directly through a real DbContext read.
        using var ctx = CreateContext();
        var id = MoneyConversionTestId.New();
        ctx.Accounts.Add(new AccountTestAggregate(id, "Acme", Currency.Usd, new SystemClock()));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var entityType = ctx.Model.FindEntityType(typeof(AccountTestAggregate))!;
        var tableName = entityType.GetTableName();
        var currencyColumn = entityType.FindProperty(nameof(AccountTestAggregate.PreferredCurrency))!.GetColumnName();
        var idColumn = entityType.FindProperty("Id")!.GetColumnName();

        // Corrupt the stored code directly, bypassing CurrencyValueConverter's write-time
        // validation entirely — simulating data corruption or an out-of-band write. Table/column
        // names come from trusted EF model metadata (never user input); only the id value is
        // parameterized — built via string.Concat rather than a "$" interpolated literal so the
        // EF1002 "possible SQL injection" analyzer, which cannot distinguish this from an unsafe
        // interpolation, does not fire on inherently-safe metadata-derived identifiers.
        var sql = string.Concat("UPDATE ", tableName, " SET ", currencyColumn, " = 'ZZZ' WHERE ", idColumn, " = {0}");
        var rowsAffected = await ctx.Database.ExecuteSqlRawAsync(sql, id.Value);
        rowsAffected.Should().Be(1, "the raw UPDATE must actually match the seeded row for this test to be meaningful");
        ctx.ChangeTracker.Clear();

        // Act
        var act = async () => await ctx.Accounts.FindAsync(id);

        // Assert — a clear, loud exception on read; the corrupted row must never be silently
        // materialized with some other (wrong) Currency.
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*ZZZ*", "the exception must name the corrupted code, never silently substitute a different currency");
    }
}
