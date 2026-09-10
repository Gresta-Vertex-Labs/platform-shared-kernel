using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest rule factory mechanically enforcing the exact scope of the
/// <c>13.ServiceDefaults</c>→<c>17.Workflows</c> layering grant.
/// </summary>
/// <remarks>
/// <para>
/// The root <c>CLAUDE.md</c> Hard rules grant <c>13.ServiceDefaults</c> a narrow, individually-named
/// <c>ProjectReference</c> to <c>SharedKernel.Workflows.Temporal</c> "solely to resolve
/// <c>IWorkflowServiceProbe</c>/<c>WorkflowServiceHealth</c> for <c>AddWorkflowReadinessCheck</c>. No
/// other <c>17.Workflows</c> type (<c>TemporalOptions</c>, <c>ITemporalClient</c>,
/// <c>WorkflowBase</c>/<c>ActivityBase</c>, <c>IWorkflowDispatcher</c>,
/// <c>ITemporalRawClientAccessor</c>, …) may be reached through this exception." This class is that
/// check's mechanical lock — the older of the platform's two probe-only layering grants, and until
/// the previously unenforced one (the sibling <c>19.Scheduling</c> grant already had
/// <see cref="ServiceDefaultsSchedulingLayeringRules.OnlyReachesSchedulerProbeTypes"/>).
/// </para>
/// <para>
/// <strong>This is its own, separately-earned grant — never reason about it by analogy to the
/// sibling <c>13.ServiceDefaults</c>→<c>19.Scheduling</c> grant, and never fold the
/// two into one parameterized helper.</strong> The root brain is emphatic on this point: each grant
/// is independently justified (each donor domain ships no lower-numbered <c>.Abstractions</c>
/// companion to reference instead) and each must stay a distinct, narrowly-named rule. See
/// <see cref="ServiceDefaultsOnlyReachesWorkflowProbeTypesPredicate"/>'s own remarks for the full
/// rationale, and <see cref="ServiceDefaultsSchedulingLayeringRules"/>'s own remarks for the mirrored
/// warning from the other side of the pairing.
/// </para>
/// </remarks>
public static class ServiceDefaultsWorkflowLayeringRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied
    /// <c>SharedKernel.ServiceDefaults</c> assembly (including any nested/compiler-generated type)
    /// reaches into <c>SharedKernel.Workflows.Temporal</c> through anything other than
    /// <c>SharedKernel.Workflows.Temporal.Health.IWorkflowServiceProbe</c> or
    /// <c>SharedKernel.Workflows.Temporal.Health.WorkflowServiceHealth</c>.
    /// </summary>
    /// <param name="serviceDefaultsAssembly">The <c>SharedKernel.ServiceDefaults</c> assembly.</param>
    /// <returns>
    /// A <see cref="ConditionList"/> ready for assertion via
    /// <c>AssertRule</c>, or by inspecting <c>GetResult().IsSuccessful</c> directly.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Rationale:</strong> <c>SharedKernel.ServiceDefaults</c> takes a real
    /// <c>ProjectReference</c> to <c>SharedKernel.Workflows.Temporal</c> to resolve
    /// <c>AddWorkflowReadinessCheck()</c>'s probe dependency — the ONLY package-level grant this
    /// narrow, and any type reached beyond the two named probe types would silently widen it past
    /// what the root <c>CLAUDE.md</c> Hard rule authorizes (e.g. a future change accidentally
    /// injecting <c>ITemporalClient</c> or branching on <c>TemporalOptions</c> directly inside
    /// <c>13.ServiceDefaults</c>, reaching straight into workflow internals the composition-root
    /// layer has no business touching).
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> a type inside <c>SharedKernel.ServiceDefaults</c>
    /// constructing, injecting, or otherwise referencing <c>ITemporalClient</c>,
    /// <c>TemporalOptions</c>, <c>WorkflowBase</c>/<c>ActivityBase</c>,
    /// <c>IWorkflowDispatcher</c>, <c>ITemporalRawClientAccessor</c>,
    /// <c>CommandActivity&lt;TCommand&gt;</c>, <c>IWorkflowIdFactory</c>, or any other
    /// <c>SharedKernel.Workflows.Temporal.*</c> type.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> exactly the shape <c>WorkflowReadinessHealthCheck</c>
    /// already uses — inject <c>IWorkflowServiceProbe</c>, call <c>.ProbeAsync(...)</c>, read the
    /// returned <c>WorkflowServiceHealth</c>. Nothing else from <c>17.Workflows</c> is ever touched.
    /// </para>
    /// <para>
    /// No exemption is permitted for this rule — the grant itself already IS the exemption; this
    /// rule exists precisely to keep that exemption from silently widening.
    /// </para>
    /// </remarks>
    public static ConditionList OnlyReachesWorkflowProbeTypes(Assembly serviceDefaultsAssembly) =>
        Types
            .InAssembly(serviceDefaultsAssembly)
            .That()
            .HaveNameStartingWith(string.Empty) // select all types
            .Should()
            .MeetCustomRule(new ServiceDefaultsOnlyReachesWorkflowProbeTypesPredicate());
}
