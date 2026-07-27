using System.Diagnostics;
using SharedKernel.Workflows.Temporal.Constants;
using SharedKernel.Workflows.Temporal.Dispatch;
using SharedKernel.Workflows.Temporal.Logging;
using Temporalio.Api.Common.V1;
using Temporalio.Client.Interceptors;
using Temporalio.Converters;
using Temporalio.Worker.Interceptors;
using Temporalio.Workflows;

namespace SharedKernel.Workflows.Temporal.Interception;

/// <summary>
/// Carries <see cref="TenantScope"/> and correlation id as Temporal headers across the client → workflow
/// → activity hop, sourced from <see cref="SharedKernel.Primitives.Propagation.WellKnownHeaders"/> —
/// never a retyped literal.
/// </summary>
/// <remarks>
/// The client half writes the headers on every start/signal/query call, sourced from
/// <see cref="DispatchPropagationContext"/> (set immediately before each call by
/// <see cref="Dispatch.WorkflowDispatcher"/>/<see cref="Dispatch.WorkflowHandleAdapter"/>). The worker
/// half reads them back: workflow code reads <c>Workflow.Info.Headers</c> directly (deterministic,
/// requires no ambient state), while activity code has no such direct route — the worker-side
/// <see cref="ActivityInboundInterceptor"/> override republishes them into
/// <see cref="ActivityPropagationContext"/>, which <see cref="Authoring.ActivityBase.TenantScope"/>
/// reads.
/// </remarks>
internal sealed class WorkflowPropagationInterceptor : IClientInterceptor, IWorkerInterceptor
{
    /// <inheritdoc />
    public ClientOutboundInterceptor InterceptClient(ClientOutboundInterceptor nextInterceptor)
        => new PropagatingClientOutboundInterceptor(nextInterceptor);

    /// <inheritdoc />
    public WorkflowInboundInterceptor InterceptWorkflow(WorkflowInboundInterceptor nextInterceptor)
        => new PropagatingWorkflowInboundInterceptor(nextInterceptor);

    /// <inheritdoc />
    public ActivityInboundInterceptor InterceptActivity(ActivityInboundInterceptor nextInterceptor)
        => new PropagatingActivityInboundInterceptor(nextInterceptor);

    private static Payload ToPayload(string value) => DataConverter.Default.PayloadConverter.ToPayload(value);

    private static string? FromPayload(Payload payload) => DataConverter.Default.PayloadConverter.ToValue(payload, typeof(string)) as string;

    private static void WriteHeaders(IDictionary<string, Payload> headers)
    {
        TenantScope tenantScope = DispatchPropagationContext.CurrentTenantScope;
        if (tenantScope != TenantScope.None)
        {
            headers[WorkflowWellKnown.TenantHeaderKey] = ToPayload(tenantScope.Value);
        }

        string? correlationId = Activity.Current?.Id;
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            headers[WorkflowWellKnown.CorrelationHeaderKey] = ToPayload(correlationId);
        }
    }

    private sealed class PropagatingClientOutboundInterceptor(ClientOutboundInterceptor next) : ClientOutboundInterceptor(next)
    {
        public override Task<Temporalio.Client.WorkflowHandle<TWorkflow, TResult>> StartWorkflowAsync<TWorkflow, TResult>(StartWorkflowInput input)
        {
            var headers = input.Headers is { } existing ? new Dictionary<string, Payload>(existing) : [];
            WriteHeaders(headers);
            return base.StartWorkflowAsync<TWorkflow, TResult>(input with { Headers = headers });
        }

        public override Task SignalWorkflowAsync(SignalWorkflowInput input)
        {
            var headers = input.Headers is { } existing ? new Dictionary<string, Payload>(existing) : [];
            WriteHeaders(headers);
            return base.SignalWorkflowAsync(input with { Headers = headers });
        }

        public override Task<TQueryResult> QueryWorkflowAsync<TQueryResult>(QueryWorkflowInput input)
        {
            var headers = input.Headers is { } existing ? new Dictionary<string, Payload>(existing) : [];
            WriteHeaders(headers);
            return base.QueryWorkflowAsync<TQueryResult>(input with { Headers = headers });
        }
    }

    private sealed class PropagatingWorkflowInboundInterceptor(WorkflowInboundInterceptor next) : WorkflowInboundInterceptor(next)
    {
        /// <summary>
        /// Wraps the workflow's OWN outbound interceptor (the one used when the workflow schedules an
        /// activity or starts a child workflow) so tenant/correlation headers keep propagating past this
        /// workflow, rather than stopping at it. Verified against the real 1.17.0 assembly at Tests phase
        /// (T-15): <c>Workflow.Info.Headers</c> (the headers this workflow itself was started with) are
        /// NOT automatically forwarded to activities/child workflows this workflow schedules —
        /// <c>ScheduleActivityInput</c>/<c>StartChildWorkflowInput</c> each carry their own, independent
        /// <c>Headers</c> dictionary that starts EMPTY unless something populates it. Without this
        /// override, an activity or child workflow invoked from within a workflow would observe
        /// <see cref="Dispatch.TenantScope.None"/> even though the workflow itself was correctly
        /// tenant-scoped — silently defeating the propagation guarantee at the first hop past the
        /// workflow boundary.
        /// </summary>
        public override void Init(WorkflowOutboundInterceptor outbound)
            => base.Init(new PropagatingWorkflowOutboundInterceptor(outbound));

        public override Task<object?> ExecuteWorkflowAsync(ExecuteWorkflowInput input)
        {
            var headers = Workflow.Info.Headers;
            if (headers is null || !headers.ContainsKey(WorkflowWellKnown.TenantHeaderKey))
            {
                WorkflowLog.TenantHeaderMissingOnWorkflow(Workflow.Logger, Workflow.Info.WorkflowId);
            }

            return base.ExecuteWorkflowAsync(input);
        }
    }

    /// <summary>
    /// Copies the current workflow's own inbound headers (<c>Workflow.Info.Headers</c>) onto every
    /// activity it schedules and every child workflow it starts — see the remarks on
    /// <see cref="PropagatingWorkflowInboundInterceptor.Init"/> for why this copy is necessary rather
    /// than automatic.
    /// </summary>
    private sealed class PropagatingWorkflowOutboundInterceptor(WorkflowOutboundInterceptor next) : WorkflowOutboundInterceptor(next)
    {
        public override Task<TResult> ScheduleActivityAsync<TResult>(ScheduleActivityInput input)
            => base.ScheduleActivityAsync<TResult>(input with { Headers = MergeWithWorkflowHeaders(input.Headers) });

        public override Task<Temporalio.Workflows.ChildWorkflowHandle<TWorkflow, TResult>> StartChildWorkflowAsync<TWorkflow, TResult>(StartChildWorkflowInput input)
            => base.StartChildWorkflowAsync<TWorkflow, TResult>(input with { Headers = MergeWithWorkflowHeaders(input.Headers) });

        private static IDictionary<string, Payload> MergeWithWorkflowHeaders(IDictionary<string, Payload>? existing)
        {
            var headers = existing is { } e ? new Dictionary<string, Payload>(e) : new Dictionary<string, Payload>();
            if (Workflow.Info.Headers is { } workflowHeaders)
            {
                foreach (KeyValuePair<string, Payload> pair in workflowHeaders)
                {
                    // An explicitly-set header on this specific call always wins over the inherited one.
                    headers.TryAdd(pair.Key, pair.Value);
                }
            }

            return headers;
        }
    }

    private sealed class PropagatingActivityInboundInterceptor(ActivityInboundInterceptor next) : ActivityInboundInterceptor(next)
    {
        public override Task<object?> ExecuteActivityAsync(ExecuteActivityInput input)
        {
            TenantScope tenantScope = TenantScope.None;
            string correlationId = string.Empty;

            if (input.Headers is { } headers)
            {
                if (headers.TryGetValue(WorkflowWellKnown.TenantHeaderKey, out var tenantPayload)
                    && FromPayload(tenantPayload) is { Length: > 0 } tenantValue)
                {
                    tenantScope = TenantScope.Of(tenantValue);
                }

                if (headers.TryGetValue(WorkflowWellKnown.CorrelationHeaderKey, out var correlationPayload)
                    && FromPayload(correlationPayload) is { } correlationValue)
                {
                    correlationId = correlationValue;
                }
            }

            ActivityPropagationContext.Set(tenantScope, correlationId);
            return base.ExecuteActivityAsync(input);
        }
    }
}
