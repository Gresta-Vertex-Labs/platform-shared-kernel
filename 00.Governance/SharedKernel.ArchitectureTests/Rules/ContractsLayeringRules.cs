using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicate enforcing the <c>04.Contracts</c> wire-format
/// construction-path contract introduced by WO-054 P-350:
/// <c>SharedKernel.Contracts.Events.EventEnvelope&lt;TEvent&gt;</c> — the platform's single
/// cross-service event wire format — must be constructed exclusively via
/// <c>EventEnvelope.Wrap{TEvent}</c>, never a raw object initializer.
/// </summary>
/// <remarks>
/// <para>
/// A NEW class, deliberately not a fifth predicate folded onto the existing
/// <see cref="ContractsPurityRules"/> — <see cref="ContractsPurityRules"/> governs
/// <c>04.Contracts</c>'s OWN internal purity (no domain types on its public surface, no
/// non-trivial methods, sealed integration events, no <c>Result</c>-type on its public
/// surface); this rule instead governs how OTHER assemblies must construct one of
/// <c>04.Contracts</c>'s types — the same "protect this domain's boundary from outside misuse"
/// direction <see cref="PresentationLayeringRules"/>, <see cref="CommunicationLayeringRules"/>,
/// and <see cref="MessagingArchitectureRules"/> already established under a
/// <c>{Domain}LayeringRules</c> name.
/// </para>
/// <para>
/// No new SK diagnostic ID is introduced by this class — the single rule is a pure NetArchTest
/// <see cref="ConditionList"/> predicate over Mono.Cecil IL inspection, following the same
/// "boundary-mapping prohibition via architecture test, not Roslyn analyzer" precedent already
/// established for SK-less rules in this domain (<see cref="RedisTopologyRules"/>,
/// <see cref="CompositionRootExclusivityRules"/>, <see cref="CommunicationLayeringRules"/>'s
/// gRPC/Contracts rule, and <see cref="PresentationLayeringRules"/>'s own
/// <c>ProblemDetails</c> construction-path rule — the closest structural analog: a
/// sealed/record wire-format type with exactly one legitimate construction site elsewhere in
/// the platform, detected via <c>newobj</c> IL-opcode presence, caller-controlled package
/// exclusion).
/// </para>
/// <para>
/// Introduced in WO-054 P-350, motivated by this review's own finding that the platform's
/// shipped <c>07.Messaging</c>/<c>SharedKernel.Messaging.MassTransit</c>
/// <c>MassTransitEventPublisher.PublishEnvelope&lt;TEvent&gt;</c> constructed
/// <c>EventEnvelope&lt;TEvent&gt;</c> via a raw object initializer instead of
/// <c>EventEnvelope.Wrap&lt;TEvent&gt;()</c> — <c>04.Contracts</c>'s own XML-doc-mandated
/// factory — silently dropping the <c>TenantId</c> field shipped for cross-service tenant
/// routing (WO-052/P-331). That defect shipped in <c>07.Messaging</c>'s P-340
/// (<c>SK.07.EnvelopeTenancy</c>, ET-04) BEFORE this phase's own implementation session began
/// (confirmed 2026-08-07 by reading <c>MassTransitEventPublisher.cs</c> directly and by
/// <c>07.Messaging/state-map.md</c>'s ET-04 row showing <c>●</c> Complete, dated 2026-08-05) —
/// see <see cref="ContractsLayeringRulesTests"/>'s own remarks and this phase's task-row
/// annotations in <c>00.Governance/state-map.md</c> for the full stale-dependency correction
/// record, mirroring the <c>SK.00.StorageTopology</c>/<c>SK.00.SearchTopology</c>/
/// <c>SK.00.IntelligenceTopology</c>/<c>SK.00.WorkflowTopology</c>/
/// <c>SK.00.EfPropertyUsageGuard</c> precedent for this exact class of
/// dependency-resolved-before-implementation finding.
/// </para>
/// </remarks>
public static class ContractsLayeringRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied assemblies
    /// directly instantiates <c>SharedKernel.Contracts.Events.EventEnvelope&lt;TEvent&gt;</c>
    /// via a <c>newobj</c> IL opcode targeting its zero-parameter (object-initializer)
    /// constructor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rationale:</strong> <c>EventEnvelope&lt;TEvent&gt;</c>'s own XML doc states
    /// "<c>EventEnvelope.Wrap&lt;TEvent&gt;</c> is the only permitted construction path. Do not
    /// construct instances directly," and <c>04.Contracts/CLAUDE.md</c> repeats the same claim
    /// ("The <c>Wrap</c> factory is the only permitted construction path for outbound events").
    /// A documentation-only convention was not sufficient — the platform's own shipped
    /// <c>MassTransitEventPublisher</c> violated it. This rule closes the gap mechanically, here
    /// and for any future consumer of <c>EventEnvelope&lt;TEvent&gt;</c>.
    /// </para>
    /// <para>
    /// <strong>Offending pattern</strong> (the real, motivating defect —
    /// <c>07.Messaging</c>'s <c>MassTransitEventPublisher.PublishEnvelope&lt;TEvent&gt;</c>
    /// before its P-340 fix):
    /// <code>
    /// var envelope = new EventEnvelope&lt;TEvent&gt;
    /// {
    ///     EventId = domainEvent.Id,
    ///     OccurredOn = domainEvent.OccurredOn,
    ///     EventType = typeof(TEvent).Name,
    ///     EventVersion = version,
    ///     SourceService = sourceService,
    ///     Payload = domainEvent,
    /// };
    /// </code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>
    /// var envelope = EventEnvelope.Wrap(domainEvent, sourceService, correlationId, causationId, tenantId);
    /// </code>
    /// </para>
    /// <para>
    /// The caller supplies every assembly to be checked EXCEPT <c>SharedKernel.Contracts</c>
    /// itself — there is no internal namespace exemption inside the predicate (see
    /// <see cref="NoDirectEventEnvelopeConstructionPredicate"/>'s own remarks for why none is
    /// needed: a legitimate caller's own compiled IL never contains a matching <c>newobj</c> at
    /// all — it emits only a <c>call</c>/<c>callvirt</c> to <c>EventEnvelope.Wrap</c> or, for a
    /// <c>with</c> expression, to the compiler-synthesized <c>&lt;Clone&gt;$</c> method — the
    /// <c>newobj</c> instruction that actually constructs the record lives inside
    /// <c>SharedKernel.Contracts.dll</c> itself, a different assembly entirely from any caller's).
    /// </para>
    /// </remarks>
    /// <param name="assemblies">
    /// The assemblies under test — must never include <c>SharedKernel.Contracts</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> ready for assertion via
    /// <c>result.IsSuccessful.Should().BeTrue()</c> or <c>AssertRule</c> on
    /// <see cref="Helpers.ArchitectureRuleBase"/>.
    /// </returns>
    public static ConditionList NoDirectEventEnvelopeConstructionOutsideContracts(
        params Assembly[] assemblies) =>
        Types
            .InAssemblies(assemblies)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoDirectEventEnvelopeConstructionPredicate());
}
