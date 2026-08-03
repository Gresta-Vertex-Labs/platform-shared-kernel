using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Persistence.EfCore.Tests.Interceptors;

/// <summary>
/// WO-053/P-333 (C-129): <see cref="ConcurrencyInterceptor"/>'s <c>ConcurrencyConflictDetected</c>
/// Warning (EventId <c>6000</c>).
/// </summary>
public sealed class ConcurrencyInterceptorLoggingTests
{
    [Fact]
    public async Task SaveChanges_ConcurrencyConflict_LogsWarning_WithConflictingEntityTypeName()
    {
        // Arrange
        var inMemoryLogger = new InMemoryLogger<ConcurrencyInterceptor>();
        var interceptor = new ConcurrencyInterceptor(inMemoryLogger);

        var userContext = TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var audit = new SharedKernel.Persistence.EfCore.Interceptors.AuditInterceptor(
            userContext, clock, TestDbContextFactory.DefaultServiceOptions());
        var softDelete = new SharedKernel.Persistence.EfCore.Interceptors.SoftDeleteInterceptor(
            userContext, clock, TestDbContextFactory.DefaultServiceOptions());

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        using var ctx = new TestDbContext(options, audit, softDelete, interceptor);
        ctx.Database.EnsureCreated();

        var id = TestId.New();
        var aggregate = new ConcurrentTestAggregate(id, "Original", new SystemClock());
        ctx.ConcurrentAggregates.Add(aggregate);
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var tracked = await ctx.ConcurrentAggregates.FirstAsync(e => e.Id == id);
        tracked.Rename("Modified");

        // Force a mismatch — the UPDATE's WHERE clause will never match, triggering
        // DbUpdateConcurrencyException (same technique as ConcurrencyInterceptorTests).
        ctx.Entry(tracked).Property(nameof(IHasConcurrency.RowVersion)).OriginalValue =
            new byte[] { 1, 2, 3, 4 };

        // Act
        Func<Task> act = () => ctx.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<ConflictException>();

        var record = inMemoryLogger.Records.ShouldHaveLogged(new EventId(6000), LogLevel.Warning);
        record.TryGetProperty("EntityType", out var entityType).Should().BeTrue();
        entityType.Should().Be(nameof(ConcurrentTestAggregate));

        // Exactly once — TryTranslate logs immediately before returning the translated
        // ConflictException, never duplicated across the single SaveChangesAsync call.
        inMemoryLogger.Records.ShouldHaveLoggedCount(new EventId(6000), 1);
    }

    [Fact]
    public async Task SaveChanges_NonConcurrencyException_DoesNotLogConcurrencyWarning()
    {
        // Arrange
        var inMemoryLogger = new InMemoryLogger<ConcurrencyInterceptor>();
        var interceptor = new ConcurrencyInterceptor(inMemoryLogger);
        var userContext = TestDbContextFactory.CreateAuthenticatedUserContext(Guid.NewGuid());
        var clock = TestDbContextFactory.CreateClock(DateTimeOffset.UtcNow);
        var audit = new SharedKernel.Persistence.EfCore.Interceptors.AuditInterceptor(
            userContext, clock, TestDbContextFactory.DefaultServiceOptions());
        var softDelete = new SharedKernel.Persistence.EfCore.Interceptors.SoftDeleteInterceptor(
            userContext, clock, TestDbContextFactory.DefaultServiceOptions());

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        using var ctx = new TestDbContext(options, audit, softDelete, interceptor);
        ctx.Database.EnsureCreated();

        var id = TestId.New();
        var aggregate1 = new HardDeleteAggregate(id, "Test1", new SystemClock());
        ctx.HardDeleteAggregates.Add(aggregate1);
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var duplicate = new HardDeleteAggregate(id, "Test2", new SystemClock());
        ctx.Entry(duplicate).State = EntityState.Added;

        // Act
        Func<Task> act = () => ctx.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
        inMemoryLogger.Records.ShouldNotHaveLogged(new EventId(6000));
    }
}
