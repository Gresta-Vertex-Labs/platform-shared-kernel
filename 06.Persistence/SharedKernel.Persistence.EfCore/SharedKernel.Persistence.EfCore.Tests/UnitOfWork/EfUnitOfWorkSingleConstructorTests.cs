using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.Domain;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;
using SharedKernel.Application.Transactions;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Persistence.EfCore.UnitOfWork;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Tests.UnitOfWork;

// ---------------------------------------------------------------------------
// T-25 — EfUnitOfWork single-constructor DI tests
// ---------------------------------------------------------------------------

public sealed class EfUnitOfWorkSingleConstructorTests
{
    [Fact]
    public void EfUnitOfWork_Has_Exactly_OnePublicConstructor()
    {
        // Hard rule: exactly one public constructor; a second one creates DI ambiguity.
        typeof(EfUnitOfWork<TestDbContext>).GetConstructors().Should().HaveCount(1);
    }

    [Fact]
    public void EfUnitOfWork_TakesNoDispatcher_TheContextDispatchesItsOwnEvents()
    {
        // A24: dispatch moved into SharedKernelDbContext.SaveChangesAsync, so every save path dispatches.
        var ctor = typeof(EfUnitOfWork<TestDbContext>).GetConstructors().Single();
        ctor.GetParameters().Should().NotContain(p => p.ParameterType == typeof(IDomainEventDispatcher));
    }

    [Fact]
    public async Task EfUnitOfWork_WithDispatcher_Via_DI_DispatchesEvents()
    {
        // Arrange
        using var ctx = TestDbContextFactory.CreateTestDbContext();

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        dispatcher.DispatchAsync(
            Arg.Any<IReadOnlyList<IDomainEvent>>(),
            Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

        // Create EfUnitOfWork via the single constructor — dispatcher provided.
        var uow = EfUnitOfWork.For(ctx, dispatcher);

        var id = TestId.New();
        var aggregate = new AuditableTestAggregate(id, "DispatchTest", new SystemClock());
        aggregate.RaiseTestEvent();
        await ctx.AuditableAggregates.AddAsync(aggregate);

        // Act
        await uow.SaveChangesAsync();

        // Assert — dispatcher must have been called with the raised event.
        await dispatcher.Received(1).DispatchAsync(
            Arg.Is<IReadOnlyList<IDomainEvent>>(events => events.Count > 0),
            Arg.Any<CancellationToken>());

        // Domain events must be cleared post-dispatch.
        aggregate.DomainEvents.Should().BeEmpty("events cleared after dispatch");
    }

    [Fact]
    public async Task EfUnitOfWork_WithoutDispatcher_Via_DI_SavesSuccessfully()
    {
        // Arrange — no dispatcher registered; pass null via the nullable optional parameter.
        using var ctx = TestDbContextFactory.CreateTestDbContext();
        var uow = EfUnitOfWork.For(ctx, dispatcher: null);

        var id = TestId.New();
        var aggregate = new AuditableTestAggregate(id, "NoDispatchTest", new SystemClock());
        aggregate.RaiseTestEvent();
        await ctx.AuditableAggregates.AddAsync(aggregate);

        // Act — must succeed without throwing even though no dispatcher is present.
        var result = await uow.SaveChangesAsync();

        // Assert
        result.Should().BeGreaterThan(0, "rows written to the database");
        aggregate.DomainEvents.Should().BeEmpty("events cleared even without dispatcher");
    }

    [Fact]
    public async Task EfUnitOfWork_DI_Resolves_WithDispatcher_Via_ServiceCollection()
    {
        // Arrange
        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        dispatcher.DispatchAsync(
            Arg.Any<IReadOnlyList<IDomainEvent>>(),
            Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventDispatcher>(dispatcher);
        services.AddSharedKernelEfCore<TestDbContext>(o => o.UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared")).Build();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        // Act — the registered dispatcher is attached to the context the scope hands out.
        var uow = scope.ServiceProvider.GetRequiredService<SharedKernel.Persistence.EfCore.UnitOfWork.IUnitOfWork<TestDbContext>>();

        var id = TestId.New();
        var aggregate = new AuditableTestAggregate(id, "DI_Dispatch", new SystemClock());
        aggregate.RaiseTestEvent();
        await ctx.AuditableAggregates.AddAsync(aggregate);
        await uow.SaveChangesAsync();

        // Assert
        await dispatcher.Received(1).DispatchAsync(
            Arg.Any<IReadOnlyList<IDomainEvent>>(),
            Arg.Any<CancellationToken>());
    }
}
