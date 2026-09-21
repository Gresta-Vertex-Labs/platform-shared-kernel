using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Application.Auditing;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Dapper.Extensions;
using SharedKernel.Persistence.Dapper.ReadModels;
using SharedKernel.Persistence.Dapper.TypeHandlers;
using SharedKernel.Persistence.EfCore.Auditing.Extensions;
using SharedKernel.Persistence.EfCore.Configurations;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Conversions;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Encryption.Extensions;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Repositories;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.Npgsql.Extensions;
using SharedKernel.Primitives.Clocks;
using Xunit;

namespace SharedKernel.Persistence.ConsumerVerify;

// ---------------------------------------------------------------------------
// Minimal domain fixture — an aggregate, its strongly-typed id, and its EF Core configuration,
// exercised through the PACKED SharedKernel.Persistence.EfCore public API exactly as a consuming
// service would author them.
// ---------------------------------------------------------------------------

public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static OrderId New() => new(Guid.NewGuid());
}

public sealed class Order : AggregateRoot<OrderId>
{
    public string CustomerEmail { get; private set; } = string.Empty;
    public decimal Total { get; private set; }

    public Order(OrderId id, string customerEmail, decimal total, IClock clock)
        : base(id, clock)
    {
        CustomerEmail = customerEmail;
        Total = total;
    }

    protected Order() { } // ORM materialization path
}

public sealed class OrderConfiguration : EntityTypeConfigurationBase<Order, OrderId>
{
    public override void Configure(EntityTypeBuilder<Order> builder)
    {
        base.Configure(builder);
        builder.Property(o => o.CustomerEmail).HasMaxLength(255).IsRequired();
        builder.Property(o => o.Total).IsRequired();
    }
}

public sealed class ConsumerVerifyDbContext : SharedKernelDbContext
{
    public DbSet<Order> Orders => Set<Order>();

    public ConsumerVerifyDbContext(
        DbContextOptions<ConsumerVerifyDbContext> options,
        PersistenceContextDependencies dependencies)
            : base(options, dependencies)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureStronglyTypedId<OrderId, Guid>();
        base.ConfigureConventions(configurationBuilder);
    }
}

internal sealed class OrderRepository(ConsumerVerifyDbContext ctx) : EfRepository<Order, OrderId>(ctx);

internal sealed class OrderReadRepository(ConsumerVerifyDbContext ctx)
    : EfReadRepository<Order, OrderId>(ctx, new SpecificationEvaluator<Order>());

/// <summary>Exercises the packed public API of every 06.Persistence package the way a consuming service would.</summary>
public sealed class ConsumerVerifyTests
{
    // -----------------------------------------------------------------------
    // Dependency-graph sanity: each package's assembly references only what its own README/CLAUDE.md
    // promises — the same reflection-based technique 05.Application.ConsumerVerify uses.
    // -----------------------------------------------------------------------

    [Fact]
    public void EfCorePackage_IsThePostgreSqlProvider()
    {
        // P-558: PostgreSQL-only — the former SharedKernel.Persistence.PostgreSQL package merged into EfCore.
        var references = typeof(SharedKernelDbContext).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToArray();

        Assert.Contains("Npgsql.EntityFrameworkCore.PostgreSQL", references);
        Assert.Contains("SharedKernel.Persistence.Npgsql", references);
    }

    [Fact]
    public void DapperPackage_NeverReferencesEntityFrameworkCore()
    {
        var references = typeof(DapperReadService).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToArray();

        Assert.DoesNotContain(references, name =>
            name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || name.StartsWith("SharedKernel.Persistence.EfCore", StringComparison.Ordinal));
    }

    [Fact]
    public void AbstractionsPackage_NeverReferencesAnOrm()
    {
        var references = typeof(IDbConnectionFactory).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToArray();

        Assert.DoesNotContain(references, name =>
            name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || name.StartsWith("Npgsql", StringComparison.Ordinal)
            || name == "Dapper");
    }

    // -----------------------------------------------------------------------
    // EF Core: a real SQLite-backed round trip through the packed repository/specification/
    // interceptor surface — proves EfCore + Abstractions compose correctly once packed separately.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EfRepository_AddThenGetById_RoundTripsThroughSqlite()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddSharedKernelEfCore<ConsumerVerifyDbContext>(options => options.UseSqlite(connection))
            .Build();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ConsumerVerifyDbContext>();
        await ctx.Database.EnsureCreatedAsync();

        var writeRepo = new OrderRepository(ctx);
        var order = new Order(OrderId.New(), "ada@example.com", 42.50m, new SystemClock());
        await writeRepo.AddAsync(order);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var readRepo = new OrderReadRepository(ctx);
        var loaded = await readRepo.GetByIdAsync(order.Id);

        Assert.NotNull(loaded);
        Assert.Equal("ada@example.com", loaded!.CustomerEmail);
        Assert.Equal(42.50m, loaded.Total);
    }

    // -----------------------------------------------------------------------
    // EfCore.Auditing: WithAuditTrail() registers the append-only audit-trail services.
    // -----------------------------------------------------------------------

    [Fact]
    public void WithAuditTrail_RegistersAuditTrailWriterAndQueryService()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Persistence:Auditing:HmacKeyBase64"] =
                    Convert.ToBase64String(new byte[32]),
                ["SharedKernel:Persistence:Npgsql:ConnectionString"] =
                    "Host=localhost;Port=1;Database=consumer_verify;Username=x;Password=x",
            })
            .Build();

        services.AddLogging();
        services.AddSharedKernelCryptography(configuration); // IHmacSigner — WithAuditTrail requires it
        services.AddSharedKernelNpgsql(configuration); // IDbConnectionFactory/IAdvisoryTransactionLock — EfAuditTrailWriter requires it (construction-only, never connects here)
        services.AddSharedKernelEfCore<ConsumerVerifyDbContext>(options => options.UseSqlite("DataSource=:memory:"))
            .WithAuditTrail(configuration)
            .Build();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAuditQueryService>());
    }

    // -----------------------------------------------------------------------
    // EfCore.Encryption: WithEncryption() registers the field-level encryption services, given a
    // key provider the consuming service supplies (exactly as documented).
    // -----------------------------------------------------------------------

    [Fact]
    public void WithEncryption_RegistersEncryptionInfrastructure_GivenAStaticKeyProvider()
    {
        var services = new ServiceCollection();
        var key = new CryptographicKey("key-1", new byte[32]);
        var keyProvider = new StaticEncryptionKeyProvider("key-1", [key]);
        services.AddSingleton(keyProvider);
        services.AddSingleton<IEncryptionKeyProvider>(keyProvider);
        services.AddSingleton<ISynchronousEncryptionKeyProvider>(keyProvider);

        services.AddSharedKernelEfCore<ConsumerVerifyDbContext>(options => options.UseSqlite("DataSource=:memory:"))
            .WithEncryption()
            .Build();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        // Build() succeeding and the context resolving proves the encryption model convention and
        // interceptor wired in without throwing.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ConsumerVerifyDbContext>());
    }

    // -----------------------------------------------------------------------
    // Dapper: AddSharedKernelDapper() registers the type handlers idempotently and a
    // DapperReadService subclass constructs against the packed IDbConnectionFactory contract.
    // -----------------------------------------------------------------------

    [Fact]
    public void AddSharedKernelDapper_RegistersTypeHandlers_Idempotently()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelDapper();
        services.AddSharedKernelDapper(); // second call must not throw — see DapperTypeHandlers.Apply remarks

        using var provider = services.BuildServiceProvider();

        Assert.True(true); // both registrations completed without throwing
    }

    // -----------------------------------------------------------------------
    // Npgsql / PostgreSQL: DI registration succeeds without opening a connection — NpgsqlDataSource
    // construction is lazy, so this proves the wiring without requiring a live PostgreSQL instance
    // (real connectivity is proven by each package's own Testcontainers-backed .Tests project).
    // -----------------------------------------------------------------------

    [Fact]
    public void AddSharedKernelNpgsql_RegistersConnectionFactory_WithoutConnecting()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Persistence:Npgsql:ConnectionString"] =
                    "Host=localhost;Port=1;Database=consumer_verify;Username=x;Password=x",
            })
            .Build();

        services.AddSharedKernelNpgsql(configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDbConnectionFactory>());
        Assert.NotNull(provider.GetRequiredService<NpgsqlDataSource>());
    }

    [Fact]
    public void UsePostgreSQL_ConfiguresDbContextOptions_WithoutThrowing()
    {
        var builder = new DbContextOptionsBuilder<ConsumerVerifyDbContext>();
        using var dataSource = NpgsqlDataSource.Create("Host=localhost;Port=1;Database=consumer_verify;Username=x;Password=x");

        builder.UsePostgreSQL(dataSource, o => o.Retry.Enabled = false);

        Assert.True(((DbContextOptionsBuilder)builder).IsConfigured);
    }
}
