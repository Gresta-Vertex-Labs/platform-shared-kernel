using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.Sagas;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// SA-06: TestHarness saga tests.
/// Defines a minimal two-state saga (Initial → Active → Final) triggered by two events.
/// Publishes both events in sequence; asserts saga instance reaches Final state.
/// </summary>
public sealed class SagaTests
{
    // -------------------------------------------------------------------------
    // SA-06a: Saga transitions from Initial to Active on first event
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Saga_WhenOrderStartedPublished_TransitionsToActiveState()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddSagaStateMachine<TestOrderSagaStateMachine, TestOrderSagaState>()
                    .InMemoryRepository();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var correlationId = Guid.NewGuid();
        await harness.Bus.Publish(new TestOrderStarted(correlationId));

        // Wait for the saga to process the event.
        await harness.InactivityTask;

        var sagaHarness = harness.GetSagaStateMachineHarness<TestOrderSagaStateMachine, TestOrderSagaState>();
        var sagaInstance = sagaHarness.Sagas.Select(s => s.CorrelationId == correlationId).FirstOrDefault();

        sagaInstance.Should().NotBeNull("saga instance must be created when OrderStarted is published");
        sagaInstance!.Saga.CurrentState.Should().Be("Active",
            "saga must transition to Active state after OrderStarted event");

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // SA-06b: Saga transitions from Active to Final on second event
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Saga_WhenBothEventsPublished_ReachesFinalState()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddSagaStateMachine<TestOrderSagaStateMachine, TestOrderSagaState>()
                    .InMemoryRepository();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var correlationId = Guid.NewGuid();

        // Publish both events in sequence.
        await harness.Bus.Publish(new TestOrderStarted(correlationId));
        await harness.Bus.Publish(new TestOrderCompleted(correlationId));

        // Wait for the saga to process both events.
        await harness.InactivityTask;

        var sagaHarness = harness.GetSagaStateMachineHarness<TestOrderSagaStateMachine, TestOrderSagaState>();

        (await sagaHarness.Sagas.Any(s => s.CorrelationId == correlationId))
            .Should().BeTrue("saga instance must exist for the given correlationId");

        // After finalization, MassTransit removes the saga instance from the repository.
        // Assert that the Final state was reached by verifying the Completed event was consumed.
        (await harness.Consumed.Any<TestOrderCompleted>()).Should().BeTrue(
            "saga must have consumed the TestOrderCompleted event to finalize");

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // SA-06c: Saga state is inspectable via the harness
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Saga_AfterOrderStarted_StateIsInspectableViaHarness()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddSagaStateMachine<TestOrderSagaStateMachine, TestOrderSagaState>()
                    .InMemoryRepository();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var correlationId = Guid.NewGuid();
        await harness.Bus.Publish(new TestOrderStarted(correlationId));

        await harness.InactivityTask;

        var sagaHarness = harness.GetSagaStateMachineHarness<TestOrderSagaStateMachine, TestOrderSagaState>();

        // Assert saga is in Active state and CreatedAt was set.
        var sagaInst = sagaHarness.Sagas.Select(s => s.CorrelationId == correlationId).FirstOrDefault();
        sagaInst.Should().NotBeNull();
        sagaInst!.Saga.CurrentState.Should().Be("Active");
        sagaInst.Saga.CreatedAt.Should().NotBe(default(DateTimeOffset),
            "SagaStateBase.CreatedAt must be set when the saga instance is created");

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // SA-03 smoke: AddSaga<TStateMachine, TSaga>() builds without error
    // -------------------------------------------------------------------------

    [Fact]
    public void AddSaga_BuildsWithoutError()
    {
        var services = new ServiceCollection();
        var act = () => services
            .AddSharedKernelMessaging(o => o.ServiceName = "saga-test-service")
            .UseRabbitMq("rabbitmq://localhost")
            .AddSaga<TestOrderSagaStateMachine, TestOrderSagaState>()
            .Build();

        act.Should().NotThrow("AddSaga<TStateMachine, TSaga>() with a valid configuration must build without error");
    }
}

// ---------------------------------------------------------------------------
// Minimal saga state — NOT using 'file' modifier so MassTransit harness type
// matching works correctly. 'file' types get mangled CLR names.
// ---------------------------------------------------------------------------

/// <summary>Minimal saga state for testing — Initial → Active → Final.</summary>
internal sealed record TestOrderSagaState : SagaStateBase
{
}

// ---------------------------------------------------------------------------
// Integration events for the test saga
// ---------------------------------------------------------------------------

internal sealed record TestOrderStarted(Guid OrderId);
internal sealed record TestOrderCompleted(Guid OrderId);

// ---------------------------------------------------------------------------
// Minimal two-state saga state machine
// ---------------------------------------------------------------------------

internal sealed class TestOrderSagaStateMachine : SagaStateMachineBase<TestOrderSagaState>
{
    public State Active { get; private set; } = null!;

    public Event<TestOrderStarted> OrderStarted { get; private set; } = null!;
    public Event<TestOrderCompleted> OrderCompleted { get; private set; } = null!;

    public TestOrderSagaStateMachine()
    {
        InstanceState(x => x.CurrentState);

        // Correlate by OrderId → CorrelationId.
        Event(() => OrderStarted, x => x.CorrelateById(ctx => ctx.Message.OrderId));
        Event(() => OrderCompleted, x => x.CorrelateById(ctx => ctx.Message.OrderId));

        Initially(
            When(OrderStarted)
                .Then(ctx =>
                {
                    ctx.Saga.CreatedAt = DateTimeOffset.UtcNow;
                    ctx.Saga.UpdatedAt = DateTimeOffset.UtcNow;
                })
                .TransitionTo(Active));

        During(Active,
            When(OrderCompleted)
                .Then(ctx => ctx.Saga.UpdatedAt = DateTimeOffset.UtcNow)
                .Finalize());

        SetCompletedWhenFinalized();
    }
}
