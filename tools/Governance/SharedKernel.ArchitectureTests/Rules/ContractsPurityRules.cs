using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Helpers;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that protect the <c>04.Contracts</c> layer from domain type leakage,
/// <c>Result&lt;T&gt;</c> leaking onto the wire, behaviour inside integration events, and non-sealed
/// integration events.
/// </summary>
/// <remarks>
/// <para>
/// All four factory methods accept an <see cref="Assembly"/> parameter and return a
/// <see cref="ConditionList"/> — consistent with the established <c>ArchitectureRuleBase</c> API. Every rule
/// passes against the real <c>SharedKernel.Contracts</c> assembly, which is itself tested, so a failure
/// against your own contracts assembly is a real finding rather than a rule that was only ever tried on
/// contrived fixtures.
/// </para>
/// <para>
/// <strong>Contract types versus integration events.</strong> A contracts assembly legitimately carries
/// behaviour on some types: validating factories (<c>PageRequest.Create</c>), projections
/// (<c>PagedList&lt;T&gt;.Map</c>), codecs (<c>PageCursor.Encode</c>). The rules therefore judge what reaches
/// the wire — public properties and fields — rather than banning methods outright, and the "no behaviour"
/// rule applies only to integration events, which are pure data.
/// </para>
/// <para>
/// Documentation-only (not a NetArchTest rule): microservices must not reference
/// <c>SharedKernel.Domain</c> directly unless they implement domain logic. Cross-service DTO types are in
/// <c>SharedKernel.Contracts</c>; domain types are internal to services that own the domain. No code
/// enforcement is applicable at the mono-repo level.
/// </para>
/// <para>
/// Reference this class with <c>PrivateAssets="all"</c> so it never becomes a transitive production
/// dependency.
/// </para>
/// </remarks>
public static class ContractsPurityRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no integration event type in the supplied
    /// contracts assembly contains a non-trivial method.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An integration event is a published fact: data only. A method on one is logic every consumer would
    /// have to reimplement in its own language, and a reason to change the event that is not a schema change.
    /// </para>
    /// <para>
    /// A method is trivial if it is a constructor, property getter/setter, static operator (<c>op_</c>
    /// prefix), one of <c>ToString</c>/<c>Equals</c>/<c>GetHashCode</c>, or a compiler-generated record member
    /// (<c>Deconstruct</c>, <c>PrintMembers</c>, <c>&lt;Clone&gt;$</c>). Uses
    /// <see cref="NoNonTrivialMethodsPredicate"/> for IL-level inspection.
    /// </para>
    /// <para>
    /// Only types implementing <paramref name="integrationEventInterface"/> are judged. Other contract types
    /// may carry factories and projections; see the class remarks.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>
    /// [IntegrationEvent("orders.order-placed")]
    /// public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn, DateTimeOffset Deadline) : IIntegrationEvent
    /// {
    ///     public bool IsExpired() =&gt; Deadline &lt; DateTimeOffset.UtcNow;
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>
    /// [IntegrationEvent("orders.order-placed")]
    /// public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn, DateTimeOffset Deadline) : IIntegrationEvent;
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="contractsAssembly">The contracts assembly to evaluate.</param>
    /// <param name="integrationEventInterface">
    /// The integration-event marker interface, i.e. <c>typeof(SharedKernel.Contracts.Events.IIntegrationEvent)</c>.
    /// Supplied by the caller rather than hard-bound here so this package declares no dependency on
    /// <c>SharedKernel.Contracts</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting every integration event type has no non-trivial methods.
    /// </returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="integrationEventInterface"/> is not an interface — which would select zero types and
    /// make the rule pass vacuously.
    /// </exception>
    public static ConditionList IntegrationEventsHaveNoNonTrivialMethods(
        Assembly contractsAssembly,
        Type integrationEventInterface)
    {
        RuleAnchor.NotNull(contractsAssembly, nameof(contractsAssembly));
        RuleAnchor.Interface(integrationEventInterface, nameof(integrationEventInterface));

        return Types
            .InAssembly(contractsAssembly)
            .That()
            .ImplementInterface(integrationEventInterface)
            .Should()
            .MeetCustomRule(new NoNonTrivialMethodsPredicate());
    }

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no public type in the supplied
    /// contracts assembly has a dependency on <c>SharedKernel.Domain</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Integration events and DTOs must be independent projections. Exposing
    /// <c>Entity&lt;TId&gt;</c>, <c>AggregateRoot&lt;TId&gt;</c>, <c>ValueObject</c>, or
    /// <c>Specification&lt;T&gt;</c> on a contracts public surface ties the wire format to the
    /// domain model, breaking polyglot consumers. No type is exempt.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>public class OrderSummaryDto { public Order DomainOrder { get; set; } }</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>public record OrderSummaryDto(Guid OrderId, string Status);</code>
    /// </para>
    /// </remarks>
    /// <param name="contractsAssembly">The contracts assembly to evaluate.</param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no public type depends on the domain assembly.
    /// </returns>
    public static ConditionList ContractsAssembliesHaveNoDomainTypeOnPublicSurface(
        Assembly contractsAssembly) =>
        Types
            .InAssembly(contractsAssembly)
            .That()
            .ArePublic()
            .Should()
            .NotHaveDependencyOn("SharedKernel.Domain");

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no public type in the supplied contracts assembly
    /// exposes a <c>Result</c>, <c>Result&lt;T&gt;</c>, <c>ValidationResult</c> or
    /// <c>ValidationResult&lt;T&gt;</c> (from <c>SharedKernel.Primitives</c>) through a public property or field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Result&lt;T&gt;</c> is an intra-service outcome type. Putting one in a serialized payload causes
    /// deserialization failures in any JSON client that does not share the <c>SharedKernel.Primitives</c>
    /// assembly, breaking the polyglot contract model. Map the outcome to a success payload or an RFC 9457
    /// problem response at the boundary instead.
    /// </para>
    /// <para>
    /// Public methods may still return these types: a validating factory such as <c>PageRequest.Create</c> or a
    /// codec such as <c>PageCursor.Decode</c> runs inside the service and never reaches the wire. Referencing
    /// <c>SharedKernel.Primitives</c> for <c>Error</c> is likewise allowed. Uses
    /// <see cref="NoResultTypedPublicMemberPredicate"/>.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>public class CreateOrderResponse { public Result&lt;Guid&gt; OrderId { get; set; } }</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>
    /// public record CreateOrderResponse(Guid OrderId);
    /// public sealed record PageRequest { public static ValidationResult&lt;PageRequest&gt; Create(int? page, int? pageSize) =&gt; ...; }
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="contractsAssembly">The contracts assembly to evaluate.</param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no public type exposes an outcome type through a property or field.
    /// </returns>
    public static ConditionList ContractsAssembliesHaveNoResultTypeOnPublicSurface(
        Assembly contractsAssembly) =>
        Types
            .InAssembly(contractsAssembly)
            .That()
            .ArePublic()
            .Should()
            .MeetCustomRule(new NoResultTypedPublicMemberPredicate());

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that every non-abstract type in the
    /// supplied assembly that implements <c>IIntegrationEvent</c> is sealed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Non-sealed integration events are an inheritance trap. A sub-event changes the wire
    /// format without a new <c>[IntegrationEvent(..., Version = n)]</c>, causing silent schema drift.
    /// <c>sealed</c> ensures the wire contract is closed.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>public class OrderCreatedEvent : IIntegrationEvent { ... }</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>public sealed record OrderCreatedEvent : IIntegrationEvent { ... }</code>
    /// </para>
    /// </remarks>
    /// <param name="contractsAssembly">The contracts assembly to evaluate.</param>
    /// <param name="integrationEventInterface">
    /// The integration-event marker interface, i.e.
    /// <c>typeof(SharedKernel.Contracts.Events.IIntegrationEvent)</c>. Supplied by the caller
    /// rather than hard-bound here so this package declares no dependency on
    /// <c>SharedKernel.Contracts</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting all non-abstract integration-event
    /// implementations are sealed.
    /// </returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="integrationEventInterface"/> is not an interface — which would select
    /// zero types and make the rule pass vacuously.
    /// </exception>
    public static ConditionList IntegrationEventImplementationsMustBeSealed(
        Assembly contractsAssembly,
        Type integrationEventInterface)
    {
        RuleAnchor.NotNull(contractsAssembly, nameof(contractsAssembly));
        RuleAnchor.Interface(integrationEventInterface, nameof(integrationEventInterface));

        return Types
            .InAssembly(contractsAssembly)
            .That()
            .ImplementInterface(integrationEventInterface)
            .And()
            .AreNotAbstract()
            .Should()
            .BeSealed();
    }
}
