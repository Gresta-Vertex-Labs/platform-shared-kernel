using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.Abstractions.UnitOfWork;
using SharedKernel.Persistence.Dapper.ReadModels;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.Npgsql.Extensions;
using SharedKernel.Persistence.PostgreSQL.Extensions;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.Dapper.Tests.Integration;

// ---------------------------------------------------------------------------
// Minimal EF Core entity + DbContext sharing a database with a plain Dapper-only table.
// ---------------------------------------------------------------------------

public sealed class EfWidget
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class EfWidgetConfig : IEntityTypeConfiguration<EfWidget>
{
    public void Configure(EntityTypeBuilder<EfWidget> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
    }
}

public sealed class CommandServiceTestDbContext : SharedKernelDbContext
{
    public DbSet<EfWidget> Widgets => Set<EfWidget>();

    public CommandServiceTestDbContext(
        DbContextOptions<CommandServiceTestDbContext> options,
        PersistenceContextDependencies dependencies)
            : base(options, dependencies)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new EfWidgetConfig());
    }
}

/// <summary>
/// A <see cref="DapperCommandService"/> subclass enlists in the SAME transaction and
/// connection as <see cref="ITransactionalUnitOfWork"/>'s active explicit transaction: a write
/// through each survives together on commit and disappears together on rollback.
/// </summary>
public sealed class DapperCommandServiceTransactionIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed class LogTableCommandService(
        IDbConnectionFactory connectionFactory,
        IAmbientDbTransaction? ambientTransaction = null)
            : DapperCommandService(connectionFactory, ambientTransaction)
    {
        public bool IsCurrentlyEnlisted => IsEnlistedInAmbientTransaction;

        public Task<int> InsertLogAsync(string message, CancellationToken ct) =>
            ExecuteAsync("INSERT INTO dapper_log (message) VALUES (@message)", new { message }, cancellationToken: ct);
    }

    private async Task<ServiceProvider> BuildProviderAsync()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelNpgsql(_fixture.ConnectionString);

        services
            .AddSharedKernelEfCore<CommandServiceTestDbContext>((sp, options) => options.UsePostgreSQL(sp))
            .WithTransactionalUnitOfWork()
            .Build();

        services.AddScoped<LogTableCommandService>();

        var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<CommandServiceTestDbContext>();
            await ctx.Database.EnsureCreatedAsync();

            await using var connection = await scope.ServiceProvider
                .GetRequiredService<IDbConnectionFactory>()
                    .CreateConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DROP TABLE IF EXISTS dapper_log;
                CREATE TABLE dapper_log (id SERIAL PRIMARY KEY, message TEXT NOT NULL);
                """;
            await command.ExecuteNonQueryAsync();
        }

        return provider;
    }

    [Fact]
    public async Task CommandService_InsideActiveTransaction_IsEnlisted_AndSharesTheSameConnection()
    {
        var provider = await BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var commandService = scope.ServiceProvider.GetRequiredService<LogTableCommandService>();

        await using var transaction = await uow.BeginTransactionAsync();

        commandService.IsCurrentlyEnlisted.Should().BeTrue();

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task CommandService_OutsideAnyTransaction_IsNotEnlisted()
    {
        var provider = await BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();

        var commandService = scope.ServiceProvider.GetRequiredService<LogTableCommandService>();

        commandService.IsCurrentlyEnlisted.Should().BeFalse();
    }

    [Fact]
    public async Task Commit_MakesBothTheEfWriteAndTheDapperWrite_Durable()
    {
        var provider = await BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();

        var ctx = scope.ServiceProvider.GetRequiredService<CommandServiceTestDbContext>();
        var uow = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var commandService = scope.ServiceProvider.GetRequiredService<LogTableCommandService>();

        await using (var transaction = await uow.BeginTransactionAsync())
        {
            ctx.Widgets.Add(new EfWidget { Name = "commit-widget" });
            await uow.SaveChangesAsync();

            await commandService.InsertLogAsync("commit-log", CancellationToken.None);

            await transaction.CommitAsync();
        }

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyCtx = verifyScope.ServiceProvider.GetRequiredService<CommandServiceTestDbContext>();
        (await verifyCtx.Widgets.CountAsync()).Should().Be(1);

        await using var connection = await verifyScope.ServiceProvider
            .GetRequiredService<IDbConnectionFactory>()
                .CreateConnectionAsync();
        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM dapper_log";
        var logCount = (long)(await countCommand.ExecuteScalarAsync())!;
        logCount.Should().Be(1);
    }

    [Fact]
    public async Task Rollback_DiscardsBothTheEfWriteAndTheDapperWrite_Together()
    {
        var provider = await BuildProviderAsync();
        await using var scope = provider.CreateAsyncScope();

        var ctx = scope.ServiceProvider.GetRequiredService<CommandServiceTestDbContext>();
        var uow = scope.ServiceProvider.GetRequiredService<ITransactionalUnitOfWork>();
        var commandService = scope.ServiceProvider.GetRequiredService<LogTableCommandService>();

        await using (var transaction = await uow.BeginTransactionAsync())
        {
            ctx.Widgets.Add(new EfWidget { Name = "rollback-widget" });
            await uow.SaveChangesAsync();

            await commandService.InsertLogAsync("rollback-log", CancellationToken.None);

            await transaction.RollbackAsync();
        }

        await using var verifyScope = provider.CreateAsyncScope();
        var verifyCtx = verifyScope.ServiceProvider.GetRequiredService<CommandServiceTestDbContext>();
        (await verifyCtx.Widgets.CountAsync()).Should().Be(0);

        await using var connection = await verifyScope.ServiceProvider
            .GetRequiredService<IDbConnectionFactory>()
                .CreateConnectionAsync();
        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM dapper_log";
        var logCount = (long)(await countCommand.ExecuteScalarAsync())!;
        logCount.Should().Be(0);
    }
}
