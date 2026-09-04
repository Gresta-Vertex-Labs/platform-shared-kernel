using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest rule factory mechanically enforcing the exact scope of the
/// <c>13.ServiceDefaults</c>→<c>19.Scheduling</c> layering grant (WO-073/P-466).
/// </summary>
/// <remarks>
/// <para>
/// The root <c>CLAUDE.md</c> Hard rules grant <c>13.ServiceDefaults</c> a narrow, individually-
/// named <c>ProjectReference</c> to <c>SharedKernel.Scheduling</c> "solely to resolve
/// <c>ISchedulerServiceProbe</c>/<c>SchedulerServiceHealth</c> for
/// <c>AddSchedulerReadinessCheck</c>. No other <c>19.Scheduling</c> type may be reached through
/// this exception." This class is that check's mechanical lock.
/// </para>
/// <para>
/// <strong>This is its own, separately-earned grant — never reason about it by analogy to the
/// sibling <c>13.ServiceDefaults</c>→<c>17.Workflows</c> grant (P-291/WO-047), and never fold the
/// two into one parameterized helper.</strong> The root brain is emphatic on this point: each
/// grant is independently justified (each donor domain ships no lower-numbered
/// <c>.Abstractions</c> companion to reference instead) and each must stay a distinct, narrowly-
/// named rule. See <see cref="ServiceDefaultsOnlyReachesSchedulerProbeTypesPredicate"/>'s own
/// remarks for the full rationale. As of this class's introduction, no equivalent mechanical lock
/// yet exists for the Workflows grant in this file — do not treat this class's existence as
/// license to retrofit one by generalizing this class; a Workflows-scoped lock, if ever added,
/// must be its own independent predicate and rule, designed on its own terms.
/// </para>
/// </remarks>
public static class ServiceDefaultsSchedulingLayeringRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied
    /// <c>SharedKernel.ServiceDefaults</c> assembly (including any nested/compiler-generated type)
    /// reaches into <c>SharedKernel.Scheduling</c> through anything other than
    /// <c>SharedKernel.Scheduling.Probes.ISchedulerServiceProbe</c> or
    /// <c>SharedKernel.Scheduling.Probes.SchedulerServiceHealth</c>.
    /// </summary>
    /// <param name="serviceDefaultsAssembly">The <c>SharedKernel.ServiceDefaults</c> assembly.</param>
    /// <returns>
    /// A <see cref="ConditionList"/> ready for assertion via
    /// <c>result.IsSuccessful.Should().BeTrue()</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Rationale:</strong> <c>SharedKernel.ServiceDefaults</c> takes a real
    /// <c>ProjectReference</c> to <c>SharedKernel.Scheduling</c> to resolve
    /// <c>AddSchedulerReadinessCheck()</c>'s probe dependency — the ONLY package-level grant this
    /// narrow, and any type reached beyond the two named probe types would silently widen it past
    /// what the root <c>CLAUDE.md</c> Hard rule authorizes (e.g. a future change accidentally
    /// injecting <c>IScheduledJobRegistry</c> or branching on <c>MisfirePolicy</c> directly inside
    /// <c>13.ServiceDefaults</c>, reaching straight into scheduling internals the composition-root
    /// layer has no business touching).
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> a type inside <c>SharedKernel.ServiceDefaults</c>
    /// constructing, injecting, or otherwise referencing <c>IScheduledJobRegistry</c>,
    /// <c>ScheduledCommandJob&lt;TCommand&gt;</c>, <c>SchedulingOptions</c>,
    /// <c>MisfirePolicy</c>/<c>OverlapPolicy</c>, <c>ScheduledJobDefinition</c>,
    /// <c>TenantScope</c>, or any other <c>SharedKernel.Scheduling.*</c> type.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> exactly the shape
    /// <c>SchedulerReadinessHealthCheck</c> already uses — inject
    /// <c>ISchedulerServiceProbe</c>, call <c>.ProbeAsync(...)</c>, read the returned
    /// <c>SchedulerServiceHealth</c>. Nothing else from <c>19.Scheduling</c> is ever touched.
    /// </para>
    /// <para>
    /// No exemption is permitted for this rule — the grant itself already IS the exemption; this
    /// rule exists precisely to keep that exemption from silently widening.
    /// </para>
    /// </remarks>
    public static ConditionList OnlyReachesSchedulerProbeTypes(Assembly serviceDefaultsAssembly) =>
        Types
            .InAssembly(serviceDefaultsAssembly)
            .That()
            .HaveNameStartingWith(string.Empty) // select all types
            .Should()
            .MeetCustomRule(new ServiceDefaultsOnlyReachesSchedulerProbeTypesPredicate());
}
