namespace SharedKernel.Messaging.Abstractions.RoutingSlips;

/// <summary>
/// Transport-agnostic builder for MassTransit Courier routing slips — stateless multi-step
/// coordination across multiple services.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Routing slips vs. sagas:</strong> routing slips are for stateless multi-step
/// coordination where the orchestration state lives only in the routing slip itself as it
/// travels between activities. When workflow state must survive process restarts or requires
/// durable persistent state, use <c>SagaStateMachineBase&lt;TSaga&gt;</c> instead.
/// </para>
/// <para>
/// The concrete implementation (<c>MassTransitRoutingSlipBuilder</c>) lives in
/// <c>SharedKernel.Messaging.MassTransit</c>. Resolve <see cref="IRoutingSlipBuilder"/> from DI;
/// never construct MassTransit's <c>RoutingSlipBuilder</c> directly in application code.
/// </para>
/// </remarks>
public interface IRoutingSlipBuilder
{
    /// <summary>
    /// Adds an activity step to the routing slip.
    /// </summary>
    /// <param name="activityName">A human-readable label identifying the activity step.</param>
    /// <param name="executeAddress">The MassTransit endpoint URI for the activity's execute endpoint.</param>
    /// <param name="arguments">An object whose properties match the activity's argument type (<c>TArguments</c>).</param>
    /// <returns>This builder for fluent chaining.</returns>
    IRoutingSlipBuilder AddActivity(string activityName, Uri executeAddress, object arguments);

    /// <summary>
    /// Constructs the routing slip from the previously added activities.
    /// </summary>
    /// <returns>
    /// The opaque routing slip object, typed as <see cref="object"/> to avoid a MassTransit
    /// reference in <c>SharedKernel.Messaging.Abstractions</c>.
    /// </returns>
    /// <remarks>
    /// The returned object must be passed directly to
    /// <see cref="MessageBus.IMessageBus.ExecuteRoutingSlipAsync"/> — do not cast or inspect it
    /// in application code.
    /// </remarks>
    object Build();
}
