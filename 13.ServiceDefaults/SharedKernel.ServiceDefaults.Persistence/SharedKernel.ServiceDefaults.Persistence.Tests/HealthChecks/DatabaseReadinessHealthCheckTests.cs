using System.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;
using SharedKernel.ServiceDefaults.HealthChecks;

namespace SharedKernel.ServiceDefaults.Persistence.Tests.HealthChecks;

public sealed class DatabaseReadinessHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_HealthyDatabase_ReportsHealthy_WithLatencyAndProviderData()
    {
        using var context = CreateSqliteContext();
        var healthCheck = new DatabaseReadinessHealthCheck<TestDbContext>(context);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("Latency");
        result.Data.Should().ContainKey("Provider");
    }

    [Fact]
    public async Task CheckHealthAsync_UnreachableDatabase_ReportsUnhealthy_WithLatencyAndProviderData()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("Data Source=/nonexistent/path/that/cannot/be/created.db")
            .Options;
        using var context = new TestDbContext(
            options,
            new AuditInterceptor(CreateUnauthenticatedUserContext(), CreateClock(), DefaultServiceOptions()),
            new SoftDeleteInterceptor(CreateUnauthenticatedUserContext(), CreateClock(), DefaultServiceOptions()),
            new ConcurrencyInterceptor());

        var healthCheck = new DatabaseReadinessHealthCheck<TestDbContext>(context);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Data.Should().ContainKey("Latency");
        result.Data.Should().ContainKey("Provider");
    }

    private static TestDbContext CreateSqliteContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .Options;

        var context = new TestDbContext(
            options,
            new AuditInterceptor(CreateUnauthenticatedUserContext(), CreateClock(), DefaultServiceOptions()),
            new SoftDeleteInterceptor(CreateUnauthenticatedUserContext(), CreateClock(), DefaultServiceOptions()),
            new ConcurrencyInterceptor());

        context.Database.EnsureCreated();
        return context;
    }

    private static IOptions<PersistenceServiceOptions> DefaultServiceOptions() =>
        Options.Create(new PersistenceServiceOptions());

    private static IUserContext CreateUnauthenticatedUserContext()
    {
        var mock = Substitute.For<IUserContext>();
        mock.SubjectId.Returns((string?)null);
        mock.IsAuthenticated.Returns(false);
        return mock;
    }

    private static IClock CreateClock()
    {
        var mock = Substitute.For<IClock>();
        mock.UtcNow.Returns(DateTimeOffset.UtcNow);
        return mock;
    }

    /// <summary>Minimal <see cref="SharedKernelDbContext"/> subclass with no entities, used only
    /// to exercise <see cref="DatabaseReadinessHealthCheck{TContext}"/> against a real SQLite
    /// connection.</summary>
    internal sealed class TestDbContext(
        DbContextOptions options,
        AuditInterceptor auditInterceptor,
        SoftDeleteInterceptor softDeleteInterceptor,
        ConcurrencyInterceptor concurrencyInterceptor)
        : SharedKernelDbContext(options, auditInterceptor, softDeleteInterceptor, concurrencyInterceptor);
}

public sealed class DapperDatabaseReadinessHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_HealthyConnectionFactory_ReportsHealthy_WithLatencyAndProviderData()
    {
        var command = Substitute.For<IDbCommand>();
        command.ExecuteScalar().Returns(1);

        var connection = Substitute.For<IDbConnection>();
        connection.CreateCommand().Returns(command);

        var factory = Substitute.For<IDbConnectionFactory>();
        factory.CreateConnectionAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(connection));

        var healthCheck = new DapperDatabaseReadinessHealthCheck(factory);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("Latency");
        result.Data.Should().ContainKey("Provider");
    }

    [Fact]
    public async Task CheckHealthAsync_FactoryThrows_ReportsUnhealthy_WithLatencyAndProviderData()
    {
        var factory = Substitute.For<IDbConnectionFactory>();
        factory.CreateConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IDbConnection>(new InvalidOperationException("connection refused")));

        var healthCheck = new DapperDatabaseReadinessHealthCheck(factory);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Data.Should().ContainKey("Latency");
        result.Data.Should().ContainKey("Provider");
    }
}
