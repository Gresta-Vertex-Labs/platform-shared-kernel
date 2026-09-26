using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Workflows.Temporal.Constants;
using Temporalio.Common;
using Temporalio.Workflows;

namespace SharedKernel.Workflows.Temporal.Authoring;

/// <summary>
/// The base every <c>[Workflow]</c>-attributed type in a consuming service extends. Workflow code is
/// replay code — this base takes no constructor, no injected dependencies, and no fields
/// initialised from ambient state, because workflows are instantiated by the Temporal worker, not by
/// the container.
/// </summary>
/// <remarks>
/// <para>
/// Three things every platform workflow needs, and none of which a consuming service should
/// re-derive: the replay-safe <see cref="Logger"/> already bound to <c>Workflow.Logger</c>, the
/// <see cref="TenantScope"/>/<see cref="CorrelationId"/> lifted directly out of the Temporal headers
/// the propagation interceptor set client-side (so a workflow author never touches
/// <c>Workflow.Info.Headers</c> by hand), and an <see cref="ExecuteAsync{TActivity, TArgs, TResult}"/>
/// overload whose <see cref="ActivityDispatchOptions"/> carry platform defaults for
/// <c>StartToCloseTimeout</c> and <c>RetryPolicy</c> — Temporal's raw activity options have no default
/// <c>StartToCloseTimeout</c> and reject the call at runtime if neither it nor
/// <c>ScheduleToCloseTimeout</c> is set.
/// </para>
/// <para>
/// What this base deliberately does <b>not</b> do: it does not wrap <c>[WorkflowRun]</c>, does not
/// intercept the run method, and does not impose a <c>Result&lt;T&gt;</c> return shape on the
/// workflow itself. A workflow's return value is serialised into history and may be read by another
/// SDK language, so it stays a plain contract type — <c>Result&lt;T&gt;</c> is a C#-side outcome
/// type, not a wire contract.
/// </para>
/// <para>
/// <b>Activity dispatch by name, not by expression:</b> <see cref="ExecuteAsync{TActivity, TArgs, TResult}"/>
/// dispatches via Temporal's string-keyed activity overload (verified against the real Temporalio
/// 1.17.0 assembly: <c>Workflow.ExecuteActivityAsync&lt;TResult&gt;(string, IReadOnlyCollection&lt;object?&gt;,
/// ActivityOptions)</c>), using <c>typeof(TActivity).Name</c> as the registered activity name. Every
/// activity type dispatched through this method must therefore be registered under an explicit
/// <c>[Activity(nameof(TheConcreteActivityType))]</c> name matching its own type name — see
/// <see cref="CommandActivity{TCommand}"/>'s remarks for why this convention exists and the exact
/// shape it takes.
/// </para>
/// </remarks>
public abstract class WorkflowBase
{
    /// <summary>Gets the replay-safe logger. Always <c>Workflow.Logger</c>, never an injected <c>ILogger&lt;T&gt;</c>.</summary>
    protected ILogger Logger => Workflow.Logger;

    /// <summary>Gets the current time. Always <c>Workflow.UtcNow</c>, never <see cref="DateTimeOffset.UtcNow"/>.</summary>
    protected DateTimeOffset UtcNow => Workflow.UtcNow;

    /// <summary>Generates a new deterministic id. Always <c>Workflow.NewGuid()</c>, never <see cref="Guid.NewGuid()"/>.</summary>
    protected Guid NewId() => Workflow.NewGuid();

    /// <summary>
    /// Gets the tenant scope lifted from the Temporal headers the client-side propagation
    /// interceptor set on start. <see cref="TenantScope.Global"/> if no tenant header was propagated.
    /// </summary>
    protected TenantScope TenantScope => TenantId.TryParse(ReadHeaderValue(WorkflowWellKnown.TenantHeaderKey), out TenantId tenant)
        ? TenantScope.For(tenant)
        : TenantScope.Global;

    /// <summary>
    /// Gets the correlation id lifted from the Temporal headers the client-side propagation
    /// interceptor set on start. <see cref="string.Empty"/> if none was propagated.
    /// </summary>
    protected string CorrelationId => ReadHeaderValue(WorkflowWellKnown.CorrelationHeaderKey) ?? string.Empty;

    /// <summary>
    /// Executes <typeparamref name="TActivity"/> with the platform default
    /// <c>StartToCloseTimeout</c>/<c>RetryPolicy</c> applied wherever <paramref name="options"/>
    /// does not override them.
    /// </summary>
    /// <typeparam name="TActivity">
    /// The activity type. Must be registered under an explicit <c>[Activity(nameof(TActivity))]</c>
    /// name — see the type-level remarks.
    /// </typeparam>
    /// <typeparam name="TArgs">The activity's argument type.</typeparam>
    /// <typeparam name="TResult">The activity's result type.</typeparam>
    /// <param name="args">The activity argument.</param>
    /// <param name="options">
    /// Activity dispatch options. When <see langword="null"/>, <see cref="ActivityDispatchOptions.Default"/>
    /// is used, applying the platform default <c>StartToCloseTimeout</c>/<c>HeartbeatTimeout</c>/<c>RetryPolicy</c>.
    /// </param>
    protected async Task<TResult> ExecuteAsync<TActivity, TArgs, TResult>(TArgs args, ActivityDispatchOptions? options = null)
        where TActivity : ActivityBase
    {
        ActivityDispatchOptions effective = options ?? ActivityDispatchOptions.Default;
        var activityOptions = new ActivityOptions
        {
            StartToCloseTimeout = effective.StartToCloseTimeout ?? WorkflowWellKnown.DefaultStartToCloseTimeout,
            ScheduleToCloseTimeout = effective.ScheduleToCloseTimeout,
            ScheduleToStartTimeout = effective.ScheduleToStartTimeout,
            HeartbeatTimeout = effective.HeartbeatTimeout ?? WorkflowWellKnown.DefaultHeartbeatTimeout,
            RetryPolicy = effective.RetryPolicy ?? new RetryPolicy { MaximumAttempts = WorkflowWellKnown.DefaultMaximumAttempts },
            CancellationToken = effective.CancellationToken,
        };

        return await Workflow.ExecuteActivityAsync<TResult>(
            typeof(TActivity).Name,
            [args],
            activityOptions);
    }

    private static string? ReadHeaderValue(string headerKey)
    {
        var headers = Workflow.Info.Headers;
        if (headers is null || !headers.TryGetValue(headerKey, out var payload))
        {
            return null;
        }

        return Workflow.PayloadConverter.ToValue(payload, typeof(string)) as string;
    }
}
