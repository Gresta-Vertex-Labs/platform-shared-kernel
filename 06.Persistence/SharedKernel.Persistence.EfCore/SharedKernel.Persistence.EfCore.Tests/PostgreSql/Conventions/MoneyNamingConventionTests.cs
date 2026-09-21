using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Monetary;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Persistence.EfCore.Configurations;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Tests.PostgreSql.Conventions;

/// <summary>
/// Proves snake_case naming (<c>EFCore.NamingConventions</c>, applied by <c>UsePostgreSQL()</c>)
/// reaches into an EF Core 10 complex type's own scalar properties (e.g. a <see cref="Money"/>
/// property's <c>Amount</c>/<c>Currency</c> sub-columns), not just an entity's own top-level
/// properties. Model-building only — no live connection is opened, so no Testcontainer is required:
/// EF Core finalizes the model, and therefore runs every <c>IModelFinalizingConvention</c> including
/// the snake_case rewriting, the first time <c>DbContext.Model</c> is
/// read, with no actual database round trip.
/// </summary>
public sealed class MoneyNamingConventionTests
{
    public sealed record NamingTestId(Guid Value) : StronglyTypedId<Guid>(Value)
    {
        public static NamingTestId New() => new(Guid.NewGuid());
    }

    public sealed class NamingTestAggregate : AggregateRoot<NamingTestId>
    {
        public Money Total { get; private set; } = null!;

        public NamingTestAggregate(NamingTestId id, Money total, IClock clock) : base(id, clock)
        {
            Total = total;
        }

        private NamingTestAggregate() { } // ORM path
    }

    public sealed class NamingTestAggregateConfig : EntityTypeConfigurationBase<NamingTestAggregate, NamingTestId>
    {
        public override void Configure(EntityTypeBuilder<NamingTestAggregate> builder)
        {
            base.Configure(builder);

            // Deliberately no amountColumnName/currencyColumnName override — proves the naming
            // convention, not an explicit column name, produced the final snake_case names.
            builder.Money(e => e.Total);
        }
    }

    public sealed class NamingTestDbContext : SharedKernelDbContext
    {
        public DbSet<NamingTestAggregate> Aggregates => Set<NamingTestAggregate>();

        public NamingTestDbContext(
            DbContextOptions<NamingTestDbContext> options,
            PersistenceContextDependencies dependencies)
                : base(options, dependencies)
        {
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.ConfigureStronglyTypedId<NamingTestId, Guid>();
            configurationBuilder.ConfigureMoney();
            base.ConfigureConventions(configurationBuilder);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new NamingTestAggregateConfig());
        }
    }

    [Fact]
    public void Money_ComplexTypeSubColumns_AreSnakeCased_ByDefault()
    {
        // Arrange — a syntactically valid but unreachable connection string; model building never
        // opens a connection.
        var optionsBuilder = new DbContextOptionsBuilder<NamingTestDbContext>();
        optionsBuilder.UsePostgreSQL(TestNpgsqlDataSources.Get("Host=localhost;Port=1;Database=unreachable;Username=x;Password=x"));
        var options = optionsBuilder.Options;

        var actorContext = new FakeAuditActorContext("actor");
        var clock = new SystemClock();
        using var ctx = new NamingTestDbContext(
            options,
            new PersistenceContextDependencies(
                new AuditInterceptor(actorContext, clock),
            new SoftDeleteInterceptor(clock),
            new ConcurrencyInterceptor()));

        // Act — reading Model triggers finalization; no database round trip occurs.
        var entityType = ctx.Model.FindEntityType(typeof(NamingTestAggregate))!;
        var totalComplexProperty = entityType.FindComplexProperty(nameof(NamingTestAggregate.Total))!;
        var complexType = totalComplexProperty.ComplexType;

        // Assert — EF Core's own default naming for a complex property's sub-columns is
        // "{Property}_{Member}" ("Total_Amount"/"Total_Currency"); snake_case naming must
        // reach in and normalise that too, not just top-level entity columns.
        var amountColumn = complexType.FindProperty(nameof(Money.Amount))!.GetColumnName();
        var currencyColumn = complexType.FindProperty(nameof(Money.Currency))!.GetColumnName();

        amountColumn.Should().Be("total_amount");
        currencyColumn.Should().Be("total_currency");
        amountColumn.Should().Be(amountColumn.ToLowerInvariant(), "the naming convention must have lower-cased every part of the column name");
        currencyColumn.Should().Be(currencyColumn.ToLowerInvariant());
    }
}
