using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Persistence.PostgreSQL.Extensions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions.Abstractions;
using Testcontainers.PostgreSql;

namespace SharedKernel.Persistence.PostgreSQL.Tests.Integration;

// ---------------------------------------------------------------------------
// WO-051/P-320 — proves the required two-call pairing (UsePostgreSQL(..., maxRetryCount) +
// the retry-safety guard on EfTransactionalUnitOfWork.BeginTransactionAsync) against real
// PostgreSQL: when Npgsql retry-on-failure is genuinely enabled, BeginTransactionAsync must throw
// an actionable InvalidOperationException directing the caller to ExecuteInTransactionAsync, which
// must itself still complete normally.
// ---------------------------------------------------------------------------

[Collection("PostgreSQL")]
public sealed class TransientFaultRetryIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private static ConcurrencyTestDbContext CreateRetryEnabledContext(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<ConcurrencyTestDbContext>();
        builder.UsePostgreSQL(connectionString, maxRetryCount: 3);
        var options = builder.Options;

        var userContext = Substitute.For<IUserContext>();
        userContext.IsAuthenticated.Returns(true);
        userContext.UserId.Returns(Guid.NewGuid());

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        var serviceOptions = Options.Create(new PersistenceServiceOptions());

        var audit = new AuditInterceptor(userContext, clock, serviceOptions);
        var softDelete = new SoftDeleteInterceptor(userContext, clock, serviceOptions);
        var concurrency = new ConcurrencyInterceptor();

        return new ConcurrencyTestDbContext(options, audit, softDelete, concurrency);
    }

    [Fact]
    public async Task BeginTransactionAsync_WithRetryEnabled_ThrowsActionableInvalidOperationException()
    {
        // Arrange
        await using var ctx = CreateRetryEnabledContext(ConnectionString);
        await ctx.Database.EnsureCreatedAsync();
        var uow = new EfTransactionalUnitOfWork(ctx);

        // Act
        Func<Task> act = async () => await uow.BeginTransactionAsync();

        // Assert
        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain(nameof(EfTransactionalUnitOfWork.ExecuteInTransactionAsync));
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_WithRetryEnabled_CompletesNormally()
    {
        // Arrange
        await using var ctx = CreateRetryEnabledContext(ConnectionString);
        await ctx.Database.EnsureCreatedAsync();
        var uow = new EfTransactionalUnitOfWork(ctx);

        var id = ConcurrentPgId.New();

        // Act — the retry-safe path must still work end to end with genuine Npgsql retry enabled.
        await uow.ExecuteInTransactionAsync(async token =>
        {
            await ctx.Aggregates.AddAsync(
                new ConcurrentPgAggregate(id, "RetrySafe", new SystemClock()), token);
            await uow.SaveChangesAsync(token);
        });

        // Assert
        ctx.ChangeTracker.Clear();
        var found = await ctx.Aggregates.FindAsync(id);
        found.Should().NotBeNull();
        found!.Name.Should().Be("RetrySafe");
    }
}
