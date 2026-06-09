using MassTransit;

namespace SharedKernel.Messaging.MassTransit.Sagas;

/// <summary>
/// Thin ergonomic base class for MassTransit saga state machines.
/// Extend this class to define a saga with states, events, and transitions.
/// </summary>
/// <typeparam name="TSaga">The saga state type. Must derive from <see cref="SagaStateBase"/>.</typeparam>
/// <remarks>
/// <para>
/// This is a thin ergonomic wrapper around <c>MassTransitStateMachine&lt;TSaga&gt;</c>
/// — it is <strong>not</strong> a complete abstraction. Consuming services that need advanced
/// MassTransit state machine features (composite events, activities, routing slips, or
/// activity factories) should reference MassTransit directly for those specific calls,
/// as this base class does not re-expose the full <c>MassTransitStateMachine&lt;TSaga&gt;</c> API.
/// </para>
/// <para>
/// Consuming services extend <see cref="SagaStateMachineBase{TSaga}"/> and declare their own
/// states, events, and transitions using the protected helper surface and the inherited
/// MassTransit state machine DSL.
/// </para>
/// <para>
/// Register the saga via <c>MessagingBusBuilder.AddSaga&lt;TSaga&gt;()</c>. Use
/// <c>MessagingBusBuilder.WithEntityFrameworkSagaRepository&lt;TDbContext, TSaga&gt;()</c> for
/// production persistence — the consuming service must add the saga state entity to its
/// <c>DbContext</c> and run the required EF migrations.
/// </para>
/// </remarks>
public abstract class SagaStateMachineBase<TSaga> : MassTransitStateMachine<TSaga>
    where TSaga : SagaStateBase
{
}
