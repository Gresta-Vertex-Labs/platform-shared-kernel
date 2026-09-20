using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.Interceptors;

/// <summary>
/// An aggregate loaded from the database must read time from the application clock. Without the
/// interceptor it has no clock, and raising a timestamped event or soft-deleting throws.
/// </summary>
public sealed class DomainClockMaterializationInterceptorTests
{
    private static readonly DateTimeOffset SavedAt = new(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);

    private static (TestDbContext Context, TestId Id) SeedOne(DateTimeOffset now)
    {
        var context = TestDbContextFactory.CreateTestDbContext(clock: TestDbContextFactory.CreateClock(now));
        var id = TestId.New();
        context.AuditableAggregates.Add(new AuditableTestAggregate(id, "seeded", TestDbContextFactory.CreateClock(SavedAt)));
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return (context, id);
    }

    [Fact]
    public async Task TrackedLoad_AttachesTheContextClock()
    {
        var now = new DateTimeOffset(2026, 6, 15, 12, 30, 0, TimeSpan.Zero);
        var (context, id) = SeedOne(now);
        using var _ = context;

        var loaded = await context.AuditableAggregates.SingleAsync(a => a.Id == id);
        loaded.RaiseTestEvent();

        ((IHasClock)loaded).IsClockAttached.Should().BeTrue();
        loaded.DomainEvents.Single().OccurredOn.Should().Be(now);
    }

    [Fact]
    public async Task NoTrackingLoad_AttachesTheContextClock()
    {
        var now = new DateTimeOffset(2026, 6, 15, 12, 30, 0, TimeSpan.Zero);
        var (context, id) = SeedOne(now);
        using var _ = context;

        var loaded = await context.AuditableAggregates.AsNoTracking().SingleAsync(a => a.Id == id);

        ((IHasClock)loaded).IsClockAttached.Should().BeTrue();
    }

    [Fact]
    public async Task LoadedAggregate_SoftDeletesAtTheClockTime_NotYearOne()
    {
        // Regression guard: the aggregate previously had no clock after loading, and before that it
        // recorded 0001-01-01 instead of the real clock time.
        var now = new DateTimeOffset(2026, 6, 15, 12, 30, 0, TimeSpan.Zero);
        var (context, id) = SeedOne(now);
        using var _ = context;

        var loaded = await context.AuditableAggregates.SingleAsync(a => a.Id == id);
        loaded.Close("admin");

        loaded.DeletedOn.Should().Be(now);
    }

    [Fact]
    public async Task Version_IsPersisted_AndNumberingContinuesAfterLoad()
    {
        var (context, id) = SeedOne(SavedAt);
        using var _ = context;

        var first = await context.AuditableAggregates.SingleAsync(a => a.Id == id);
        first.RaiseTestEvent();
        first.RaiseTestEvent();
        first.RaiseTestEvent();
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var reloaded = await context.AuditableAggregates.SingleAsync(a => a.Id == id);
        reloaded.Version.Should().Be(3);

        reloaded.RaiseTestEvent();
        reloaded.Version.Should().Be(4);
    }

    [Fact]
    public void Version_IsMapped_ButIsNotAConcurrencyToken()
    {
        using var context = TestDbContextFactory.CreateTestDbContext();

        var property = context.Model.FindEntityType(typeof(AuditableTestAggregate))!.FindProperty(nameof(IHasVersion.Version));

        property.Should().NotBeNull();
        property!.IsConcurrencyToken.Should().BeFalse();
        property.IsNullable.Should().BeFalse();
    }

    [Fact]
    public void AlreadyAttachedClock_IsLeftUnchanged()
    {
        var original = TestDbContextFactory.CreateClock(SavedAt);
        var aggregate = new AuditableTestAggregate(TestId.New(), "x", original);
        var interceptor = new DomainClockMaterializationInterceptor(TestDbContextFactory.CreateClock(DateTimeOffset.MaxValue));

        interceptor.InitializedInstance(default, aggregate);
        aggregate.RaiseTestEvent();

        aggregate.DomainEvents.Single().OccurredOn.Should().Be(SavedAt);
    }

    [Fact]
    public void NonAggregateEntity_IsReturnedUntouched()
    {
        var interceptor = new DomainClockMaterializationInterceptor(new SystemClock());
        var entity = new object();

        interceptor.InitializedInstance(default, entity).Should().BeSameAs(entity);
    }

    [Fact]
    public void ContextsShareOneInternalServiceProvider()
    {
        // EF Core keys its internal service provider on singleton interceptors such as materialization
        // interceptors. A fresh interceptor instance per context would build a new provider per context.
        using var first = TestDbContextFactory.CreateTestDbContext();
        using var second = TestDbContextFactory.CreateTestDbContext();

        // IModelSource is a singleton of the internal provider, so equal instances mean one shared provider.
        var firstModelSource = first.GetService<Microsoft.EntityFrameworkCore.Infrastructure.IModelSource>();
        var secondModelSource = second.GetService<Microsoft.EntityFrameworkCore.Infrastructure.IModelSource>();

        secondModelSource.Should().BeSameAs(firstModelSource);
    }

    [Fact]
    public void NullClock_Throws() =>
        FluentActions.Invoking(() => new DomainClockMaterializationInterceptor(null!))
            .Should().Throw<ArgumentNullException>();
}
