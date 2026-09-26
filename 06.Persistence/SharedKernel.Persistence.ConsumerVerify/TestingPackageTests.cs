using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Execution.Transactions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.Testing;
using SharedKernel.Testing.Execution;
using SharedKernel.Primitives.Clocks;
using Xunit;

namespace SharedKernel.Persistence.ConsumerVerify;

public sealed class Note : TenantedAggregateRoot<Guid>
{
    public Note(Guid id, TenantId tenantId, string text) : base(id, tenantId, new SystemClock()) => Text = text;

    private Note() { }

    public string Text { get; private set; } = string.Empty;
}

public sealed class NotesDbContext(DbContextOptions<NotesDbContext> options, PersistenceContextDependencies dependencies)
    : TenantedDbContext(options, dependencies)
{
    public DbSet<Note> Notes => Set<Note>();

    // The assembly's OrderConfiguration belongs to OrdersDbContext.
    protected override bool ShouldApplyConfiguration(Type configurationType) => false;
}

/// <summary>SharedKernel.Persistence.Testing, resolved as a package, used the way a consumer's test project uses it.</summary>
public sealed class TestingPackageTests
{
    [Fact]
    public async Task Fakes_RunAHandlerShapedFlow_WithTheRealTransactionSemantics()
    {
        var services = new ServiceCollection();
        var notes = services.AddFakeRepository<Note, Guid>();
        var unitOfWork = services.AddFakeUnitOfWork();
        var caller = services.AddTestRequestContext(TestRequestContext.ForTenant(new TenantId(Guid.NewGuid())));
        var audit = services.AddFakeAuditTrailWriter();
        await using var provider = services.BuildServiceProvider();
        unitOfWork.TransientFailures = 1;

        var id = Guid.NewGuid();
        await provider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async ct =>
        {
            if (!await provider.GetRequiredService<IReadRepository<Note, Guid>>().ExistsAsync(id, ct))
                await provider.GetRequiredService<IRepository<Note, Guid>>().AddAsync(new Note(id, caller.TenantId!.Value, "n"), ct);
        });

        Assert.Equal(2, unitOfWork.TransactionCount);
        Assert.Equal(1, unitOfWork.CommitCount);
        Assert.Single(notes.Items);
        Assert.Empty(audit.Recorded);
    }

    [Fact]
    public async Task PostgresTestDatabase_GivesTheProductionRoleSplit_ToAddSharedKernelPostgres()
    {
        await using var server = await PostgresTestServer.StartAsync();
        await using var database = await server.CreateDatabaseAsync();
        var tenantA = new TenantId(Guid.NewGuid());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelPostgres<NotesDbContext>(database.BuildConfiguration("notes"), "notes", p => p.UseMultiTenancy(rowLevelSecurity: true));
        var caller = services.AddTestRequestContext(TestRequestContext.ForTenant(tenantA));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
            await database.CreateSchemaAsync(context);
            await database.EnableRowLevelSecurityAsync(context);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<Note, Guid>>();
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>()
                .ExecuteInTransactionAsync(ct => repository.AddAsync(new Note(Guid.NewGuid(), tenantA, "a"), ct));
        }

        caller.TenantId = new TenantId(Guid.NewGuid());
        await using (var scope = provider.CreateAsyncScope())
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<NotesDbContext>().Notes.CountAsync());

        Assert.Equal(0L, await CountAsync(database.RuntimeConnectionString));
        Assert.Equal(1L, await CountAsync(database.CrossTenantConnectionString));

        static async Task<long> CountAsync(string connectionString)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT count(*) FROM notes", connection);
            return (long)(await command.ExecuteScalarAsync())!;
        }
    }
}
