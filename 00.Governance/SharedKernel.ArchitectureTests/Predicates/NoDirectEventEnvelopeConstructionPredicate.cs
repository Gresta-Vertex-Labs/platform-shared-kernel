using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate (no SK diagnostic ID) that fails any type whose method bodies
/// contain a <c>newobj</c> IL instruction directly constructing
/// <c>SharedKernel.Contracts.Events.EventEnvelope&lt;TEvent&gt;</c> via its compiler-generated
/// public parameterless (object-initializer) constructor.
/// </summary>
/// <remarks>
/// <para>
/// Used by
/// <see cref="Rules.ContractsLayeringRules.NoDirectEventEnvelopeConstructionOutsideContracts"/>
/// to enforce that <c>EventEnvelope&lt;TEvent&gt;</c> — the platform's single cross-service event
/// wire format — is constructed exclusively via
/// <c>SharedKernel.Contracts.Events.EventEnvelope.Wrap{TEvent}</c>, never a raw
/// object-initializer.
/// </para>
/// <para>
/// <strong>Detection — a three-condition match, all of which must hold on the <c>newobj</c>
/// operand's <see cref="MethodReference"/>:</strong>
/// </para>
/// <list type="number">
/// <item><description>
/// <c>MethodReference.DeclaringType</c>.<c>MemberReference.Namespace</c> equals
/// <c>"SharedKernel.Contracts.Events"</c> (exact match).
/// </description></item>
/// <item><description>
/// <c>MethodReference.DeclaringType</c>.<see cref="MemberReference.Name"/> equals
/// <c>"EventEnvelope`1"</c> — Mono.Cecil's undecorated simple-name shape for a generic type
/// (the backtick-arity suffix, never the substituted closed type argument). Confirmed
/// empirically against real compiled IL: for a
/// <c>GenericInstanceType</c> operand such as <c>EventEnvelope&lt;FixtureEvent&gt;</c>,
/// <c>DeclaringType.Name</c> resolves to <c>"EventEnvelope`1"</c>, exactly as this platform's
/// prior generic-<c>Newobj</c> predicates (e.g.
/// <see cref="NoDirectEncryptedValueConverterInstantiationPredicate"/>) have already
/// established for their own target types.
/// </description></item>
/// <item><description>
/// <see cref="MethodReference.Parameters"/>.Count equals <c>0</c> — this platform's first
/// constructor-arity discriminator on a generic-<c>Newobj</c> predicate. A <c>sealed record</c>
/// declared with only <c>required ... {get; init;}</c> properties (no positional parameter
/// list) synthesizes a public PARAMETERLESS instance constructor — the one used by
/// <c>new EventEnvelope&lt;TEvent&gt; { ... }</c> object-initializer syntax, this predicate's
/// fire condition — plus a separate, PRIVATE, ONE-parameter copy constructor used internally by
/// the compiler-synthesized <c>&lt;Clone&gt;$</c> method that backs <c>with</c> expressions.
/// </description></item>
/// </list>
/// <para>
/// <strong>Empirically confirmed <c>with</c>-expression behavior (2026-08-07):</strong> a
/// <c>with</c> expression (<c>envelope with { CorrelationId = "new-id" }</c>) does NOT emit a
/// <c>newobj</c> instruction at all in the CALLER's own compiled IL — it emits a
/// <c>callvirt</c> to the compiler-synthesized <c>&lt;Clone&gt;$</c> method (declared on
/// <c>EventEnvelope&lt;TEvent&gt;</c> itself, inside <c>SharedKernel.Contracts.dll</c>),
/// followed by property setter calls for the modified members. The one-parameter copy
/// constructor is invoked only from within <c>&lt;Clone&gt;$</c>'s own method body — IL that
/// lives inside <c>SharedKernel.Contracts.dll</c>, an assembly this rule's caller never passes
/// to the factory method (see the caller-controlled-exclusion note on
/// <see cref="Rules.ContractsLayeringRules"/>). Because the copy constructor is <c>private</c>,
/// external caller code could never legally emit a direct <c>newobj</c> against it in the first
/// place (a hand-written attempt would fail to compile with CS0122) — so the
/// <c>Parameters.Count == 0</c> condition is a defensive, structurally-correct discriminator
/// even though, in practice, a <c>with</c> expression never reaches the IL-shape this predicate
/// walks at all. Both mechanisms independently guarantee a compliant caller (whether via
/// <c>Wrap&lt;TEvent&gt;()</c> or a <c>with</c> expression on an already-<c>Wrap</c>-constructed
/// instance) never trips this rule.
/// </para>
/// <para>
/// <strong>No exemption mechanism.</strong> Unlike most namespace-scoped <see cref="ICustomRule"/>
/// predicates in this assembly, this predicate carries no internal namespace exemption — exclusion
/// of <c>SharedKernel.Contracts</c> (the one legitimate construction site, inside
/// <c>EventEnvelope.Wrap{TEvent}</c>'s own method body) is achieved entirely by the caller never
/// passing that assembly to
/// <see cref="Rules.ContractsLayeringRules.NoDirectEventEnvelopeConstructionOutsideContracts"/>.
/// </para>
/// <para>
/// <strong>Offending pattern:</strong>
/// <code>
/// var envelope = new EventEnvelope&lt;OrderPlacedEvent&gt;
/// {
///     EventId = domainEvent.Id,
///     OccurredOn = domainEvent.OccurredOn,
///     EventType = typeof(OrderPlacedEvent).Name,
///     EventVersion = 1,
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
/// </remarks>
public sealed class NoDirectEventEnvelopeConstructionPredicate : ICustomRule
{
    private const string EventEnvelopeNamespace = "SharedKernel.Contracts.Events";
    private const string EventEnvelopeSimpleName = "EventEnvelope`1";

    /// <summary>
    /// Returns <see langword="true"/> (rule met) for types whose method bodies contain no direct
    /// construction of <c>EventEnvelope&lt;TEvent&gt;</c> via its parameterless constructor;
    /// <see langword="false"/> when such a construction is found.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when a <c>newobj</c> instruction targeting
    /// <c>SharedKernel.Contracts.Events.EventEnvelope&lt;TEvent&gt;</c>'s zero-parameter
    /// constructor is found in any method body of <paramref name="type"/>;
    /// <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            if (method.Body is null)
                continue;

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Newobj)
                    continue;

                if (instruction.Operand is not MethodReference methodRef)
                    continue;

                var declaringType = methodRef.DeclaringType;

                if (declaringType.Namespace == EventEnvelopeNamespace &&
                    declaringType.Name == EventEnvelopeSimpleName &&
                    methodRef.Parameters.Count == 0)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
