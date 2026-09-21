using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Interceptors;

public sealed class ConcurrencyInterceptorTests
{
    // Replaces the previous non-functional
    // `interceptor.Should().NotBeNull()` placeholder assertion with a real, provider-neutral proof.
    // SQLite has no auto-generated concurrency token equivalent to PostgreSQL's `xmin` (that
    // provider-specific proof lives in SharedKernel.Persistence.EfCore.Tests against a real
    // Testcontainer), so this test deterministically forces a conflict by tampering with the
    // tracked entity's OriginalValues for the IHasConcurrency.RowVersion property so the UPDATE's
    // WHERE clause never matches the actual row — proving ConcurrencyInterceptor's rethrow
    // behavior without depending on any provider-specific auto-update mechanism.
    [Fact]
    public async Task SaveChanges_ConcurrencyConflict_MismatchedRowVersionOriginalValue_ThrowsConflictException()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();

        var id = TestId.New();
        var aggregate = new ConcurrentTestAggregate(id, "Original", new SystemClock());
        ctx.ConcurrentAggregates.Add(aggregate);
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var tracked = await ctx.ConcurrentAggregates.FirstAsync(e => e.Id == id);
        tracked.Rename("Modified");

        // Deterministically force a mismatch: the UPDATE's WHERE clause will compare this
        // (fabricated) original value against the row's real current RowVersion, which can never
        // match — zero affected rows, triggering DbUpdateConcurrencyException.
        ctx.Entry(tracked).Property(nameof(IHasConcurrency.RowVersion)).OriginalValue =
            new byte[] { 1, 2, 3, 4 };

        // Act
        Func<Task> act = () => ctx.SaveChangesAsync();

        // Assert
        var exception = await act.Should().ThrowAsync<ConflictException>();
        exception.Which.Error.Type.Should().Be(SharedKernel.Primitives.Errors.ErrorType.Conflict);
    }

    [Fact]
    public async Task SaveChanges_NonConcurrencyDbException_PropagatesUnchanged()
    {
        // Arrange — duplicate PK causes DbUpdateException (not DbUpdateConcurrencyException)
        using var ctx = TestDbContextFactory.CreateTestDbContext();

        var id = TestId.New();
        var aggregate1 = new HardDeleteAggregate(id, "Test1", new SystemClock());
        ctx.HardDeleteAggregates.Add(aggregate1);
        await ctx.SaveChangesAsync();

        // Try to insert same PK again
        ctx.ChangeTracker.Clear();
        var duplicate = new HardDeleteAggregate(id, "Test2", new SystemClock());
        ctx.Entry(duplicate).State = EntityState.Added;

        Func<Task> act = () => ctx.SaveChangesAsync();

        // Assert — DbUpdateException propagates unchanged; NOT wrapped as ConflictException
        var exception = await act.Should().ThrowAsync<DbUpdateException>();
        exception.Which.Should().NotBeOfType<ConflictException>();
    }

    // The real PostgreSQL xmin concurrency-conflict proof (two DbContext
    // instances, genuine xmin auto-update, DbUpdateConcurrencyException surfacing from an actual
    // provider mismatch) lives in SharedKernel.Persistence.EfCore.Tests against a real
    // Testcontainer — xmin is an Npgsql-only mechanism, so it cannot be proven here.
}
