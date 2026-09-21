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
        // Hard rule: exactly one public constructor; second constructor creates DI ambiguity.
        var constructors = typeof(EfUnitOfWork).GetConstructors();
        constructors.Should().HaveCount(1,
            "EfUnitOfWork must have exactly one public constructor. " +
            "A second constructor causes the DI container to silently select the shorter one, " +
            "bypassing the IDomainEventDispatcher parameter.");
    }

    [Fact]
    public void EfUnitOfWork_SingleConstructor_HasNullableDispatcherParameter()
    {
        var ctor = typeof(EfUnitOfWork).GetConstructors().Single();
        var dispatcherParam = ctor.GetParameters()
            .FirstOrDefault(p => p.ParameterType == typeof(IDomainEventDispatcher));

        dispatcherParam.Should().NotBeNull(
            "EfUnitOfWork's single constructor must have an IDomainEventDispatcher? parameter");

        // The parameter must be optional (has a default value of null).
        dispatcherParam!.IsOptional.Should().BeTrue(
            "IDomainEventDispatcher? must be an optional parameter so DI resolves null when not registered");
        dispatcherParam.DefaultValue.Should().BeNull(
            "The default value must be null — resolves to no-op path when dispatcher is not registered");
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
        var uow = new EfUnitOfWork(ctx, dispatcher);

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
        var uow = new EfUnitOfWork(ctx, dispatcher: null);

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
        using var ctx = TestDbContextFactory.CreateTestDbContext();

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        dispatcher.DispatchAsync(
            Arg.Any<IReadOnlyList<IDomainEvent>>(),
            Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton<SharedKernelDbContext>(ctx); // register as base type for EfUnitOfWork
        services.AddSingleton<IDomainEventDispatcher>(dispatcher);
        services.AddTransient<EfUnitOfWork>();

        await using var provider = services.BuildServiceProvider();

        // Act — DI resolves EfUnitOfWork with the dispatcher via the single constructor.
        var uow = provider.GetRequiredService<EfUnitOfWork>();

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
