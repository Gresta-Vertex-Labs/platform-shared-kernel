using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Auditing;
using SharedKernel.Application.Context;
using SharedKernel.Application.Transactions;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Persistence;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.EfCore.Auditing.Tests.Support;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Integration;

public sealed class LedgerOrder
{
    private LedgerOrder() { }

    public LedgerOrder(Guid id) => Id = id;

    public Guid Id { get; private set; }
}

internal sealed class LedgerOrderConfiguration : IEntityTypeConfiguration<LedgerOrder>
{
    public void Configure(EntityTypeBuilder<LedgerOrder> builder)
    {
        builder.ToTable("ledger_orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
    }
}

public sealed class LedgerTestDbContext(DbContextOptions<LedgerTestDbContext> options, PersistenceContextDependencies dependencies)
    : SharedKernelDbContext(options, dependencies)
{
    public DbSet<LedgerOrder> Orders => Set<LedgerOrder>();
}

/// <summary>
/// The ledger composed through the EF Core persistence builder (<c>WithAuditTrail</c>): a succeeded entry
/// queued on <see cref="IUnitOfWork.OnBeforeCommit"/> commits with the business write and vanishes with it.
/// </summary>
[Collection("AuditPostgres")]
public sealed class UnitOfWorkIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private static async Task<ServiceProvider> BuildAsync(string cs)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{AuditLedgerOptions.SectionName}:CurrentKeyId"] = "k1",
            [$"{AuditLedgerOptions.SectionName}:Keys:k1:Material"] = TestKeys.K1,
            [$"{AuditLedgerOptions.SectionName}:Keys:k1:Order"] = "1",
            [$"{AuditLedgerOptions.SectionName}:Sealer:Enabled"] = "false",
            [$"{AuditLedgerOptions.SectionName}:SelfCheck"] = "Off",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRequestContext>(new TestRequestContext());
        services.AddSingleton<IDbConnectionFactory>(new TestConnectionFactory(cs));
        services.AddSingleton<IHmacSigner, HmacSha256Signer>();

        services.AddSharedKernelPostgres<LedgerTestDbContext>(configuration, "ledger", p => p
            .UseDataSource(TestNpgsqlDataSources.Get(cs))
            .ConfigureDbContext((_, o) => o.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)))
            .UseAuditTrail());

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<LedgerTestDbContext>().Database.EnsureCreatedAsync();
        await LedgerTestDatabase.ExecuteAsync(cs, AuditLedgerSchema.CreateScript);
        return provider;
    }

    private static AuditEntry Succeeded(Guid orderId) => new()
    {
        Action = "OrderCreated", ResourceType = "Order", ResourceId = orderId.ToString(), Outcome = AuditOutcome.Succeeded,
    };

    [Fact]
    public async Task SucceededEntry_OnBeforeCommit_CommitsWithTheBusinessWrite()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture, withLedger: false);
        await using var provider = await BuildAsync(cs);

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<LedgerTestDbContext>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();

            await unitOfWork.ExecuteInTransactionAsync(_ =>
            {
                var id = Guid.NewGuid();
                context.Orders.Add(new LedgerOrder(id));
                unitOfWork.OnBeforeCommit(ct => writer.RecordAsync(Succeeded(id), ct));
                return Task.CompletedTask;
            });
        }

        (await LedgerTestDatabase.ScalarAsync<long>(cs, "SELECT count(*) FROM ledger_orders")).Should().Be(1);
        (await LedgerTestDatabase.ScalarAsync<long>(cs, $"SELECT count(*) FROM {AuditLedgerSchema.RecordsTable}")).Should().Be(1);
    }

    [Fact]
    public async Task SucceededEntry_RollsBackWhenTheTransactionFails()
    {
        var cs = await LedgerTestDatabase.CreateAsync(fixture, withLedger: false);
        await using var provider = await BuildAsync(cs);

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<LedgerTestDbContext>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();

            var result = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var id = Guid.NewGuid();
                context.Orders.Add(new LedgerOrder(id));
                await context.SaveChangesAsync(ct);
                await writer.RecordAsync(Succeeded(id), ct);
                return Result.Failure(Error.Conflict("test.rollback", "roll back"));
            });
            result.IsFailure.Should().BeTrue();

            // After the rollback, the failure is recorded on its own connection.
            await writer.RecordAsync(Succeeded(Guid.NewGuid()) with { Outcome = AuditOutcome.Failed, ErrorCode = "test.rollback" });
        }

        (await LedgerTestDatabase.ScalarAsync<long>(cs, "SELECT count(*) FROM ledger_orders")).Should().Be(0);
        (await LedgerTestDatabase.ScalarAsync<long>(cs, $"SELECT count(*) FROM {AuditLedgerSchema.RecordsTable}")).Should().Be(1);
        (await LedgerTestDatabase.ScalarAsync<int>(cs, $"SELECT outcome FROM {AuditLedgerSchema.RecordsTable}")).Should().Be((int)AuditOutcome.Failed);
    }
}
