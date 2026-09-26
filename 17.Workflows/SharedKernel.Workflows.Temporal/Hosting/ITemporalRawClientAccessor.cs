using SharedKernel.Execution.Tenancy;
using Temporalio.Client;

namespace SharedKernel.Workflows.Temporal.Hosting;

/// <summary>
/// The genuine last-resort escape hatch onto a raw <see cref="ITemporalClient"/>, for real Temporal
/// capabilities this package deliberately does not model (Visibility API queries, schedules,
/// namespace administration, Nexus operations).
/// </summary>
/// <remarks>
/// <para>
/// Registered only when the composition root calls
/// <see cref="ITemporalWorkflowsBuilder.AllowRawClientAccess"/> — an explicit, greppable, reviewable
/// act that logs a startup <c>Warning</c> (EventId 17012).
/// </para>
/// <para>
/// <b>THIS HATCH BYPASSES TENANT SCOPING AND WORKFLOW-ID COMPOSITION.</b> <c>TenantScope</c> is
/// applied inside <c>IWorkflowIdFactory</c> and the propagation interceptor; a raw client call
/// receives neither. A multi-tenant service using this accessor <b>must</b> compose the tenant
/// segment and set the tenant header itself.
/// </para>
/// </remarks>
public interface ITemporalRawClientAccessor
{
    /// <summary>Gets the raw Temporal client.</summary>
    ITemporalClient Client { get; }
}
