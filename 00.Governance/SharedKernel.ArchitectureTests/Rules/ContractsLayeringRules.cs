using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicate enforcing the <c>04.Contracts</c> wire-format
/// construction-path contract:
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
/// <strong>The defect this rule exists to catch.</strong> A message publisher built its
/// <c>EventEnvelope&lt;TEvent&gt;</c> with a raw object initializer instead of the
/// <c>EventEnvelope.Wrap&lt;TEvent&gt;()</c> factory. Every field it did set was correct, so the
/// code read as unremarkable and shipped — but the initializer simply never mentioned
/// <c>TenantId</c>, so every published event went out with no tenant on it, and consumers had to
/// deserialize the payload to work out which tenant an event belonged to. Nothing threw and
/// nothing failed; the field was just quietly absent. That is the failure mode a factory-only
/// construction path prevents, and the reason this rule enforces it mechanically rather than by
/// convention.
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
    /// before it was fixed):
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
    /// <c>AssertRule</c> on
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
