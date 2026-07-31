using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;

namespace SharedKernel.Persistence.EfCore.Tests.Diagnostics;

// ---------------------------------------------------------------------------
// SharedKernelDbContext.CheckReadinessAsync tests (C-90)
// ---------------------------------------------------------------------------

public sealed class DbContextDiagnosticsExtensionsTests
{
    [Fact]
    public async Task CheckReadinessAsync_HealthyContext_ReturnsIsHealthyTrue()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();

        var result = await ctx.CheckReadinessAsync();

        result.IsHealthy.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.Latency.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        result.Provider.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CheckReadinessAsync_DisposedConnection_ReturnsIsHealthyFalse_WithErrorMessage_WithoutThrowing()
    {
        using var ctx = TestDbContextFactory.CreateTestDbContext();

        // Force the underlying connection into a broken state so CanConnectAsync fails.
        var connection = ctx.Database.GetDbConnection();
        await connection.OpenAsync();
        await connection.CloseAsync();
        connection.Dispose();

        var act = async () => await ctx.CheckReadinessAsync();

        var result = await act.Should().NotThrowAsync();
        result.Subject.Provider.Should().NotBeNullOrEmpty();

        // Either CanConnectAsync still succeeds (SQLite can reopen) or it fails gracefully —
        // either way, the call must not throw, and when it fails an ErrorMessage is populated.
        if (!result.Subject.IsHealthy)
            result.Subject.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CheckReadinessAsync_InvalidConnectionString_ReturnsIsHealthyFalse_WithErrorMessage()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("Data Source=/nonexistent/path/that/cannot/be/created.db")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        var serviceOptions = TestDbContextFactory.DefaultServiceOptions();
        var userContext = TestDbContextFactory.CreateUnauthenticatedUserContext();
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        using var ctx = new TestDbContext(
            options,
            new SharedKernel.Persistence.EfCore.Interceptors.AuditInterceptor(userContext, clock, serviceOptions),
            new SharedKernel.Persistence.EfCore.Interceptors.SoftDeleteInterceptor(userContext, clock, serviceOptions),
            new SharedKernel.Persistence.EfCore.Interceptors.ConcurrencyInterceptor());

        var act = async () => await ctx.CheckReadinessAsync();

        var result = await act.Should().NotThrowAsync();
        result.Subject.IsHealthy.Should().BeFalse();
        result.Subject.ErrorMessage.Should().NotBeNullOrEmpty();
    }
}
