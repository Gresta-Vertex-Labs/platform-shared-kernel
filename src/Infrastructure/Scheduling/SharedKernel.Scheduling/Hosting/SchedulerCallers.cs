using SharedKernel.Execution.Context;
using SharedKernel.Scheduling.Registry;

namespace SharedKernel.Scheduling.Hosting;

/// <summary>
/// Builds the system callers the scheduler runs under, so the hosted loop and every occurrence
/// construct them in one place. Neither caller holds a permission: a job whose command is gated by
/// <c>[RequirePermission]</c> opens its own permissioned scope.
/// </summary>
internal static class SchedulerCallers
{
    /// <summary>The identity of the loop-level caller (start, stop, misfire and overlap decisions).</summary>
    public const string LoopIdentity = "scheduler";

    /// <summary>The caller of the hosted loop itself: global tenant scope, one correlation id per service instance.</summary>
    public static SystemRequestContext ForLoop(string correlationId) =>
        new([], identity: LoopIdentity, tenantId: null, correlationId: correlationId);

    /// <summary>
    /// The caller of one occurrence: the job's name as identity, the job's tenant scope and a new
    /// correlation id. The command handler, every log and the span of the occurrence see the same value.
    /// </summary>
    public static SystemRequestContext ForOccurrence(IScheduledJobDefinition definition) =>
        new(
            [],
            identity: definition.JobName,
            tenantId: definition.Options.TenantScope.Tenant,
            correlationId: CorrelationIds.New());
}
