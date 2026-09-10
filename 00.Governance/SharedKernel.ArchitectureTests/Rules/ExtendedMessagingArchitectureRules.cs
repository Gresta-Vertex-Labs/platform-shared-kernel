using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates (SK0706–SK0707) that close the misuse vectors introduced by
/// the messaging adapter chains: direct injection of
/// <c>MassTransit.IMessageScheduler</c> outside the messaging boundary, and saga state classes
/// that do not extend <c>SagaStateBase</c>.
/// </summary>
/// <remarks>
/// <para>
/// All factory methods return a <see cref="ConditionList"/> — consistent with the established
/// <c>ArchitectureRuleBase</c> API. Call <c>.GetResult()</c> on the returned
/// <see cref="ConditionList"/> to evaluate the rule, or pass it to
/// <see cref="Helpers.ArchitectureRuleBase.AssertRule"/> to throw on violation.
/// </para>
/// <para>
/// <list type="bullet">
///   <item><description>
///     SK0706 (<see cref="NoDirectMassTransitSchedulerInjection"/>) — prevents direct
///     <c>MassTransit.IMessageScheduler</c> injection outside <c>SharedKernel.Messaging.*</c>.
///     All other code must inject <c>SharedKernel.Messaging.Abstractions.IMessageScheduler</c>.
///   </description></item>
///   <item><description>
///     SK0707 (<see cref="SagaStatesMustExtendSagaStateBase"/>) — prevents saga state classes
///     that implement <c>ISaga</c> from omitting <c>SagaStateBase</c> from their inheritance
///     chain.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Exemption list for SK0706</strong> (applied inside
/// <see cref="NoDirectSchedulerInjectionOutsideMessagingPredicate"/>):
/// <list type="bullet">
///   <item><description>
///     Types whose namespace starts with <c>"SharedKernel.Messaging"</c> — covers both
///     <c>SharedKernel.Messaging.Abstractions</c> and <c>SharedKernel.Messaging.MassTransit</c>
///     and all sub-namespaces.
///   </description></item>
/// </list>
/// SK0707 has no exemption list — every <c>ISaga</c> implementor must extend
/// <c>SagaStateBase</c>. Any additional exemption must be documented in
/// <c>00.Governance/CLAUDE.md</c> before it is applied in code.
/// </para>
/// <para>
/// Reference this class with <c>PrivateAssets="all"</c> so it never
/// becomes a transitive production dependency.
/// </para>
/// </remarks>
public static class ExtendedMessagingArchitectureRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in any of the supplied
    /// assemblies injects <c>MassTransit.IMessageScheduler</c> as a constructor parameter,
    /// unless the type's namespace starts with <c>"SharedKernel.Messaging"</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The namespace exemption covers both <c>SharedKernel.Messaging.Abstractions</c> and
    /// <c>SharedKernel.Messaging.MassTransit</c>. All other assemblies must inject
    /// <c>SharedKernel.Messaging.Abstractions.IMessageScheduler</c>, the platform's
    /// transport-independent scheduler abstraction.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>class ScheduleReminderHandler(MassTransit.IMessageScheduler scheduler) { }</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>class ScheduleReminderHandler(IMessageScheduler scheduler) { } // platform abstraction</code>
    /// </para>
    /// </remarks>
    /// <param name="assemblies">
    /// One or more assemblies to evaluate — typically the application, domain, and
    /// infrastructure assemblies under test. Supply via <c>typeof(SomeType).Assembly</c>.
    /// Do <em>not</em> pass <c>SharedKernel.Messaging.Abstractions</c> or
    /// <c>SharedKernel.Messaging.MassTransit</c> — they are exempt by namespace prefix and
    /// passing them adds no signal.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no non-exempt type injects
    /// <c>MassTransit.IMessageScheduler</c> in a constructor.
    /// </returns>
    public static ConditionList NoDirectMassTransitSchedulerInjection(params Assembly[] assemblies)
    {
        var types = Types.InAssemblies(assemblies);

        return types
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoDirectSchedulerInjectionOutsideMessagingPredicate());
    }

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that every type implementing
    /// <c>ISaga</c> in the supplied assembly also extends <c>SagaStateBase</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>SagaStateBase</c> (from <c>SharedKernel.Messaging.MassTransit</c>) provides the
    /// platform-standard <c>CorrelationId</c>, <c>Version</c> (optimistic concurrency counter),
    /// <c>CreatedAt</c>, and <c>ModifiedAt</c> audit fields required for correct saga state
    /// persistence and version-conflict resolution.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>class OrderSagaState : ISagaVersion { } // no SagaStateBase</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>class OrderSagaState : SagaStateBase { }</code>
    /// </para>
    /// </remarks>
    /// <param name="assembly">
    /// The assembly to evaluate — supply via <c>typeof(SomeSagaState).Assembly</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting every <c>ISaga</c> implementor extends
    /// <c>SagaStateBase</c>.
    /// </returns>
    public static ConditionList SagaStatesMustExtendSagaStateBase(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new SagaStateMustExtendSagaStateBasePredicate());
}
