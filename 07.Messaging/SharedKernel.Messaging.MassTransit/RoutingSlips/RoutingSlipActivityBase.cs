using MassTransit;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Messaging.MassTransit.RoutingSlips;

/// <summary>
/// Platform-standard base class for MassTransit Courier routing slip activities.
/// Handles routing-slip CorrelationId propagation, structured log scope enrichment,
/// and exception log-then-rethrow semantics for both <c>Execute</c> and <c>Compensate</c>.
/// </summary>
/// <typeparam name="TArguments">The activity's execute argument type.</typeparam>
/// <typeparam name="TLog">The activity's compensation log type.</typeparam>
/// <remarks>
/// <para>
/// <strong>Routing slips vs. sagas:</strong> routing slips are for stateless multi-step
/// coordination — orchestration state lives only in the routing slip as it travels between
/// activities. When workflow state must survive process restarts or requires durable
/// persistent state, use <c>SagaStateMachineBase&lt;TSaga&gt;</c> instead.
/// </para>
/// <para>
/// Override <see cref="ExecuteAsync"/> and <see cref="CompensateAsync"/> with business logic only.
/// Do not override <see cref="Execute"/> or <see cref="Compensate"/> directly — both are sealed
/// entry points that propagate the routing slip's <c>CorrelationId</c> (tracking number) into
/// <see cref="System.Diagnostics.Activity.Current"/>, enrich the structured log scope with
/// <c>routing_slip.tracking_number</c> and <c>routing_slip.activity_name</c>, and rethrow any
/// unhandled exception after structured logging.
/// </para>
/// </remarks>
public abstract class RoutingSlipActivityBase<TArguments, TLog> : IActivity<TArguments, TLog>
    where TArguments : class
    where TLog : class
{
    private static readonly Action<ILogger, Guid, Exception?> LogExecuteError =
        LoggerMessage.Define<Guid>(
            LogLevel.Error,
            new EventId(1, "RoutingSlipExecuteError"),
            "Unhandled exception executing routing slip activity. TrackingNumber={TrackingNumber}.");

    private static readonly Action<ILogger, Guid, Exception?> LogCompensateError =
        LoggerMessage.Define<Guid>(
            LogLevel.Error,
            new EventId(2, "RoutingSlipCompensateError"),
            "Unhandled exception compensating routing slip activity. TrackingNumber={TrackingNumber}.");

    private ExecuteContext<TArguments>? _executeContext;
    private CompensateContext<TLog>? _compensateContext;

    /// <summary>
    /// Gets the logger for this activity. Additional dependencies are constructor-injected by subclasses.
    /// </summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="RoutingSlipActivityBase{TArguments, TLog}"/>
    /// with the provided logger.
    /// </summary>
    /// <param name="logger">The logger for this activity type.</param>
    protected RoutingSlipActivityBase(ILogger logger)
    {
        Logger = logger;
    }

    /// <summary>
    /// MassTransit Courier execute entry point. Propagates the routing slip's CorrelationId
    /// (tracking number) into <see cref="System.Diagnostics.Activity.Current"/>, enriches the
    /// structured log scope, forwards to <see cref="ExecuteAsync"/>, and rethrows any unhandled
    /// exception after structured logging. Do not override — override <see cref="ExecuteAsync"/> instead.
    /// </summary>
    /// <param name="context">The MassTransit Courier execute context.</param>
    /// <returns>The execution result produced by <see cref="ExecuteAsync"/>.</returns>
    public async Task<ExecutionResult> Execute(ExecuteContext<TArguments> context)
    {
        PropagateCorrelationId(context.TrackingNumber);

        var scopeState = new Dictionary<string, object?>
        {
            ["routing_slip.tracking_number"] = context.TrackingNumber,
            ["routing_slip.activity_name"] = context.ActivityName,
        };

        using var scope = Logger.BeginScope(scopeState);

        _executeContext = context;
        try
        {
            return await ExecuteAsync(context.Arguments, context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogExecuteError(Logger, context.TrackingNumber, ex);
            throw; // Never swallow — propagates to MassTransit Courier fault handling.
        }
        finally
        {
            _executeContext = null;
        }
    }

    /// <summary>
    /// MassTransit Courier compensate entry point. Propagates the routing slip's CorrelationId
    /// (tracking number) into <see cref="System.Diagnostics.Activity.Current"/>, enriches the
    /// structured log scope, forwards to <see cref="CompensateAsync"/>, and rethrows any unhandled
    /// exception after structured logging. Do not override — override <see cref="CompensateAsync"/> instead.
    /// </summary>
    /// <param name="context">The MassTransit Courier compensate context.</param>
    /// <returns>The compensation result produced by <see cref="CompensateAsync"/>.</returns>
    public async Task<CompensationResult> Compensate(CompensateContext<TLog> context)
    {
        PropagateCorrelationId(context.TrackingNumber);

        var scopeState = new Dictionary<string, object?>
        {
            ["routing_slip.tracking_number"] = context.TrackingNumber,
            ["routing_slip.activity_name"] = context.ActivityName,
        };

        using var scope = Logger.BeginScope(scopeState);

        _compensateContext = context;
        try
        {
            return await CompensateAsync(context.Log, context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogCompensateError(Logger, context.TrackingNumber, ex);
            throw; // Never swallow — propagates to MassTransit Courier fault handling.
        }
        finally
        {
            _compensateContext = null;
        }
    }

    /// <summary>
    /// Override this method with the activity's execute-step business logic.
    /// </summary>
    /// <param name="arguments">The activity's execute arguments.</param>
    /// <param name="ct">The cancellation token forwarded from the MassTransit Courier execute context.</param>
    /// <returns>
    /// The execution result. Use <see cref="Complete"/> to complete the activity (optionally
    /// supplying a compensation log) or <see cref="Faulted"/> to fault the routing slip.
    /// </returns>
    /// <remarks>
    /// <strong>Do not swallow exceptions.</strong> Unhandled exceptions propagate to MassTransit
    /// Courier and trigger the compensation chain for previously completed activities.
    /// </remarks>
    protected abstract Task<ExecutionResult> ExecuteAsync(TArguments arguments, CancellationToken ct);

    /// <summary>
    /// Override this method with the activity's compensation logic, run when a downstream
    /// activity in the routing slip faults.
    /// </summary>
    /// <param name="log">The compensation log captured during <see cref="ExecuteAsync"/>.</param>
    /// <param name="ct">The cancellation token forwarded from the MassTransit Courier compensate context.</param>
    /// <returns>
    /// The compensation result. Use <see cref="CompensationComplete"/> to indicate the
    /// compensation succeeded.
    /// </returns>
    /// <remarks>
    /// <strong>Do not swallow exceptions.</strong> Unhandled exceptions propagate to MassTransit
    /// Courier and are reported as compensation failures.
    /// </remarks>
    protected abstract Task<CompensationResult> CompensateAsync(TLog log, CancellationToken ct);

    /// <summary>
    /// Completes the current execute step, recording <paramref name="log"/> as the compensation
    /// log entry for this activity.
    /// </summary>
    /// <param name="log">The compensation log entry to persist for this activity.</param>
    /// <returns>An <see cref="ExecutionResult"/> representing successful completion.</returns>
    /// <remarks>Must only be called from within <see cref="ExecuteAsync"/>.</remarks>
    protected ExecutionResult Complete(TLog log)
    {
        if (_executeContext is null)
            throw new InvalidOperationException(
                $"{nameof(Complete)} can only be called from within {nameof(ExecuteAsync)}.");

        return _executeContext.Completed(log);
    }

    /// <summary>
    /// Faults the current execute step with the given exception, triggering compensation of all
    /// previously completed activities in the routing slip.
    /// </summary>
    /// <param name="ex">The exception describing the failure.</param>
    /// <returns>An <see cref="ExecutionResult"/> representing the fault.</returns>
    /// <remarks>Must only be called from within <see cref="ExecuteAsync"/>.</remarks>
    protected ExecutionResult Faulted(Exception ex)
    {
        if (_executeContext is null)
            throw new InvalidOperationException(
                $"{nameof(Faulted)} can only be called from within {nameof(ExecuteAsync)}.");

        return _executeContext.Faulted(ex);
    }

    /// <summary>
    /// Completes the current compensate step successfully.
    /// </summary>
    /// <returns>A <see cref="CompensationResult"/> representing successful compensation.</returns>
    /// <remarks>Must only be called from within <see cref="CompensateAsync"/>.</remarks>
    protected CompensationResult CompensationComplete()
    {
        if (_compensateContext is null)
            throw new InvalidOperationException(
                $"{nameof(CompensationComplete)} can only be called from within {nameof(CompensateAsync)}.");

        return _compensateContext.Compensated();
    }

    /// <summary>
    /// Propagates the routing slip's tracking number into <see cref="System.Diagnostics.Activity.Current"/>
    /// as the CorrelationId, when an active span is present.
    /// </summary>
    private static void PropagateCorrelationId(Guid trackingNumber)
    {
        System.Diagnostics.Activity.Current?.SetTag("CorrelationId", trackingNumber.ToString("D"));
    }
}
