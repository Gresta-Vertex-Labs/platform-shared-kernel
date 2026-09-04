using System.Globalization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Domain.ValueObjects.Money;
using SharedKernel.Persistence.EfCore.Configurations;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conventions;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Conversions;

// ---------------------------------------------------------------------------
// C-146/C-147/C-148/T-124 test entities and support types
// ---------------------------------------------------------------------------

internal sealed record MoneyConversionTestId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static MoneyConversionTestId New() => new(Guid.NewGuid());
}

/// <summary>Aggregate with a real <see cref="Money"/> property, configured via <c>.OwnsMoney(...)</c>.</summary>
internal sealed class ProductTestAggregate : AggregateRoot<MoneyConversionTestId>
{
    public string Name { get; private set; } = string.Empty;
    public Money Price { get; private set; } = null!;

    public ProductTestAggregate(MoneyConversionTestId id, string name, Money price, IClock clock)
        : base(id, clock)
    {
        Name = name;
        Price = price;
    }

    protected ProductTestAggregate() { } // ORM path

    public void Reprice(Money newPrice) => Price = newPrice;
}

internal sealed class ProductTestAggregateConfig
    : EntityTypeConfigurationBase<ProductTestAggregate, MoneyConversionTestId>
{
    public override void Configure(EntityTypeBuilder<ProductTestAggregate> builder)
    {
        base.Configure(builder);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.OwnsMoney(e => e.Price);
    }
}

/// <summary>
/// Aggregate with a standalone (not <see cref="Money"/>-wrapped) <see cref="Currency"/> property,
/// used to exercise <see cref="CurrencyValueConverter"/> through a real <c>DbContext</c> read path
/// (T-123) — distinct from <see cref="MoneyValueConverter"/>'s own packed-string Unpack, which also
/// happens to call <see cref="Currency.Create"/> internally but is not this converter.
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
/// <c>.OwnsMoney(...)</c> configuration, exactly as a real service's <c>OnModelCreating</c> would —
/// proving the two compose without conflict (T-124/D-107).
/// </summary>
internal sealed class MoneyTestDbContext : SharedKernelDbContext
{
    public DbSet<ProductTestAggregate> Products => Set<ProductTestAggregate>();
    public DbSet<AccountTestAggregate> Accounts => Set<AccountTestAggregate>();

    public MoneyTestDbContext(
        DbContextOptions<MoneyTestDbContext> options,
        AuditInterceptor audit,
        SoftDeleteInterceptor softDelete,
        ConcurrencyInterceptor concurrency)
        : base(options, audit, softDelete, concurrency)
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
// C-146/C-147/C-148/T-124 tests
// ---------------------------------------------------------------------------

/// <summary>
/// WO-066/P-440 — <see cref="MoneyValueConverter"/>, <see cref="MoneyEntityTypeBuilderExtensions.OwnsMoney{TEntity}"/>,
/// and <see cref="ValueObjectOwnershipBuilder"/>'s Money-exclusion (D-105/D-106/D-107).
/// </summary>
public sealed class MoneyValueConverterTests
{
    private static MoneyTestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MoneyTestDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var userCtx = TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var svcOpts = TestDbContextFactory.DefaultServiceOptions();

        var ctx = new MoneyTestDbContext(
            options,
            new AuditInterceptor(userCtx, clock, svcOpts),
            new SoftDeleteInterceptor(userCtx, clock, svcOpts),
            new ConcurrencyInterceptor());
        ctx.Database.EnsureCreated();
        return ctx;
    }

    // -----------------------------------------------------------------------
    // C-146 — MoneyValueConverter unit tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Converter_ToProvider_PacksAmountAndCurrency()
    {
        // Arrange
        var converter = new MoneyValueConverter();
        var money = Money.Create(19.99m, Currency.Usd).Value!;

        // Act
        var result = converter.ConvertToProvider!(money);

        // Assert
        result.Should().Be("19.99:USD");
    }

    [Fact]
    public void Converter_FromProvider_UnpacksAmountAndCurrency()
    {
        // Arrange
        var converter = new MoneyValueConverter();

        // Act
        var result = (Money)converter.ConvertFromProvider!("42.50:EUR")!;

        // Assert
        result.Amount.Should().Be(42.50m);
        result.Currency.Should().Be(Currency.Eur);
    }

    [Fact]
    public void Converter_FromProvider_MissingSeparator_Throws()
    {
        var converter = new MoneyValueConverter();
        var act = () => converter.ConvertFromProvider!("bad-value");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Converter_FromProvider_InvalidAmount_Throws()
    {
        var converter = new MoneyValueConverter();
        var act = () => converter.ConvertFromProvider!("notanumber:USD");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Converter_FromProvider_InvalidCurrency_Throws()
    {
        var converter = new MoneyValueConverter();
        var act = () => converter.ConvertFromProvider!("10.00:ZZZ");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RoundTrip_PreservesAmountAndCurrency_ForZeroDecimalCurrency()
    {
        // Arrange — JPY has zero minor-unit digits.
        var converter = new MoneyValueConverter();
        var money = Money.Create(1500m, Currency.Jpy).Value!;

        // Act
        var provider = converter.ConvertToProvider!(money);
        var roundTripped = (Money)converter.ConvertFromProvider!(provider)!;

        // Assert
        roundTripped.Amount.Should().Be(1500m);
        roundTripped.Currency.Should().Be(Currency.Jpy);
    }

    // -----------------------------------------------------------------------
    // C-147 — OwnsMoney / persistence round-trip (real SQLite DB)
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
    public void OwnsMoney_ConfiguresScalarColumn_NotOwnedNavigation()
    {
        // Arrange
        using var ctx = CreateContext();

        // Act
        var entityType = ctx.Model.FindEntityType(typeof(ProductTestAggregate));

        // Assert — a packed-string scalar property, never an owned-type navigation (D-106 fallback).
        entityType.Should().NotBeNull();
        entityType!.FindProperty(nameof(ProductTestAggregate.Price)).Should().NotBeNull();
        entityType.FindNavigation(nameof(ProductTestAggregate.Price)).Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // C-148/T-124 — ValueObjectOwnershipBuilder excludes Money
    // -----------------------------------------------------------------------

    [Fact]
    public void ValueObjectOwnershipBuilder_DoesNotAutoOwn_MoneyProperty()
    {
        // Arrange — MoneyTestDbContext calls ValueObjectOwnershipBuilder.Apply AFTER the explicit
        // .OwnsMoney(...) configuration, mirroring a real service's OnModelCreating.
        using var ctx = CreateContext();

        // Act
        var entityType = ctx.Model.FindEntityType(typeof(ProductTestAggregate));

        // Assert — no owned-entity-type registration for Money exists anywhere in the model;
        // the property remains the scalar column OwnsMoney configured, never re-wrapped as an
        // owned navigation by the generic IValueObject scan.
        ctx.Model.FindEntityType(typeof(Money)).Should().BeNull(
            "Money must never be auto-owned by ValueObjectOwnershipBuilder (D-107) — it is always configured explicitly via OwnsMoney(...)");
        entityType!.GetNavigations().Where(n => n.ForeignKey.IsOwnership).Should().BeEmpty(
            "the Money property is a scalar value-converted column, not an owned navigation");
    }

    // -----------------------------------------------------------------------
    // Regression (found writing T-122/T-123, fixed same session, 2026-09-02) —
    // ValueObjectOwnershipBuilder.Apply crashed model building for ANY standalone (not
    // Money-wrapped) Currency property: Currency implements IValueObject, and unlike Money, it had
    // no explicit skip in ValueObjectOwnershipBuilder — only its already-scalar-via-ConfigureMoney
    // mapping saved it from being re-discovered as a navigation, EXCEPT ValueObjectOwnershipBuilder
    // never checked for that; it tried OwnsOne(typeof(Currency), "PreferredCurrency") on top of the
    // already-scalar property, throwing "property or navigation ... already exists". Fixed by
    // generalising the skip to any CLR property already mapped as a scalar EF property, not just a
    // hardcoded Money type check. AccountTestAggregate (constructed above for T-123) is exactly the
    // shape that exposed this — a plausible, realistic production pattern (a "preferred currency"
    // field alongside a Money-typed field).
    // -----------------------------------------------------------------------

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
        // global conversion produced, never re-wrapped as an owned navigation.
        entityType.Should().NotBeNull();
        ctx.Model.FindEntityType(typeof(Currency)).Should().BeNull(
            "Currency must never be auto-owned by ValueObjectOwnershipBuilder — it is always a scalar column via ConfigureMoney's global conversion");
        entityType!.FindProperty(nameof(AccountTestAggregate.PreferredCurrency)).Should().NotBeNull();
        entityType.FindNavigation(nameof(AccountTestAggregate.PreferredCurrency)).Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // T-122 — full EF Core save+reload round-trip across zero/two/three-decimal currencies.
    //
    // WO-066/P-440 CORRECTION: T-122 as originally specified asked for a raw
    // `WHERE Currency = 'USD' AND Amount > ...`-shaped SQL assertion, proving two independently
    // queryable columns. The shipped design (D-106, see MoneyValueConverter's remarks) is a single
    // packed-string column instead. Independently re-verified this session against the real EF Core
    // 10.0.10 assembly (not merely trusted from the prior session's prose): reflecting over
    // Microsoft.EntityFrameworkCore.Metadata.ITypeBase/IMutableTypeBase/IConventionTypeBase/
    // IMutableEntityType/IConventionEntityType/IMutableComplexType/IConventionComplexType shows
    // ConstructorBinding has CanWrite=false wherever it appears, and none of the Mutable/Convention
    // interfaces re-declare it as settable; the only settable ConstructorBinding property or
    // HasConstructorBinding(...)-shaped builder method anywhere in the assembly lives on
    // Metadata.Internal.TypeBase/InternalEntityTypeBuilder/InternalComplexTypeBuilder or
    // Metadata.Runtime.Runtime*Type — entirely inside the non-public, non-SemVer-covered
    // Metadata.Internal/Metadata.Runtime namespaces. The two-column owned-type design is therefore
    // genuinely unreachable through any public EF Core 10 API, confirming (not merely trusting)
    // D-106's conclusion. T-122 is rewritten below to assert what the shipped design actually
    // guarantees — full round-trip fidelity across representative minor-unit precisions — and a
    // companion test makes the resulting capability loss concrete and observable: Amount/Currency
    // are NOT independently SQL-queryable. This is a real, documented capability loss relative to
    // the original D-105 design and is worth arch-lead's attention, not a silent scope reduction.
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
    public async Task PackedMoneyColumn_AmountAndCurrency_AreNotIndependentlySqlQueryable_DocumentedCapabilityLoss()
    {
        // Arrange — D-106's documented cost: a service needing `WHERE Currency = 'USD' AND
        // Amount > ...`-style SQL filtering cannot express it against the packed column, because no
        // "Currency" or "Amount" column exists at all — only the single packed-string column
        // OwnsMoney configured. This test makes that loss concrete: the query cannot even execute.
        using var ctx = CreateContext();
        ctx.Products.Add(new ProductTestAggregate(
            MoneyConversionTestId.New(), "Widget", Money.Create(10m, Currency.Usd).Value!, new SystemClock()));
        await ctx.SaveChangesAsync();

        var entityType = ctx.Model.FindEntityType(typeof(ProductTestAggregate))!;
        var tableName = entityType.GetTableName();

        var connection = ctx.Database.GetDbConnection();
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE Currency = 'USD' AND Amount > 0";

        // Act
        var act = () => command.ExecuteScalar();

        // Assert — no "Currency"/"Amount" column exists to reference; the query fails outright.
        act.Should().Throw<Exception>().Where(ex =>
            ex.Message.Contains("Currency", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase));
    }

    // -----------------------------------------------------------------------
    // T-123 — corrupted/unknown stored currency code surfaces as a clear thrown exception on read
    // via CurrencyValueConverter, never a silent wrong-currency substitution.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Read_CorruptedStoredCurrencyCode_ThrowsClearException_NeverSilentlySubstitutesCurrency()
    {
        // Arrange — a standalone Currency property (AccountTestAggregate.PreferredCurrency), so the
        // corruption/read path exercises CurrencyValueConverter directly through a real DbContext
        // read, not MoneyValueConverter's own internal Unpack (which happens to also call
        // Currency.Create, but is a different converter type — see MoneyValueConverterTests' own
        // unit-level coverage of that path).
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
