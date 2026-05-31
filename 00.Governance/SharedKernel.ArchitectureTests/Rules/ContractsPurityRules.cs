using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;
using SharedKernel.Contracts.Events;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that protect the <c>04.Contracts</c> layer from
/// accumulating domain logic, domain type leakage, <c>Result&lt;T&gt;</c> misuse, and
/// non-sealed integration events.
/// </summary>
/// <remarks>
/// <para>
/// All four factory methods accept an <see cref="Assembly"/> parameter and return a
/// <see cref="ConditionList"/> — consistent with the established <c>ArchitectureRuleBase</c> API.
/// </para>
/// <para>
/// Rule 5 (documentation-only — not a NetArchTest rule): microservices must not reference
/// <c>SharedKernel.Domain</c> directly unless they implement domain logic. Cross-service DTO
/// types are in <c>SharedKernel.Contracts</c>; domain types are internal to services that own
/// the domain. No code enforcement is applicable at the mono-repo level.
/// </para>
/// <para>
/// Reference this class with <c>PrivateAssets="all"</c> so it never becomes a transitive
/// production dependency.
/// </para>
/// </remarks>
public static class ContractsPurityRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied contracts
    /// assembly contains a non-trivial method.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A method is trivial if it is a constructor, property getter/setter, static operator
    /// (<c>op_</c> prefix), or one of <c>ToString</c>/<c>Equals</c>/<c>GetHashCode</c>.
    /// Any other method signals domain logic leakage into the contracts layer.
    /// </para>
    /// <para>
    /// Uses <see cref="NoNonTrivialMethodsPredicate"/> for IL-level inspection.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>public class OrderDto { public bool IsExpired() => Deadline &lt; DateTime.UtcNow; }</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>public record OrderDto(Guid Id, DateTimeOffset Deadline);</code>
    /// </para>
    /// </remarks>
    /// <param name="contractsAssembly">The contracts assembly to evaluate.</param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting all types are pure DTOs with no non-trivial methods.
    /// </returns>
    public static ConditionList ContractsAssembliesHaveNoNonTrivialMethods(Assembly contractsAssembly) =>
        Types
            .InAssembly(contractsAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoNonTrivialMethodsPredicate());

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no public type in the supplied
    /// contracts assembly has a dependency on <c>SharedKernel.Domain</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Integration events and DTOs must be independent projections. Exposing
    /// <c>Entity&lt;TId&gt;</c>, <c>AggregateRoot&lt;TId&gt;</c>, <c>ValueObject</c>, or
    /// <c>Specification&lt;T&gt;</c> on a contracts public surface ties the wire format to the
    /// domain model, breaking polyglot consumers.
    /// </para>
    /// <para>
    /// <strong>Exemption:</strong> <c>EventEnvelope&lt;TEvent&gt;</c> is excluded from this scan
    /// because its generic constraint <c>where TEvent : IDomainEvent</c> causes NetArchTest's
    /// dependency scanner to detect a dependency on <c>SharedKernel.Domain</c>. This is a
    /// constraint-only reference, not a public property or return type exposure.
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
            .And()
            .DoNotHaveName("EventEnvelope`1")
            .Should()
            .NotHaveDependencyOn("SharedKernel.Domain");

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no public type in the supplied
    /// contracts assembly has a dependency on <c>SharedKernel.Primitives</c> (where
    /// <c>Result&lt;T&gt;</c> and <c>Result</c> live).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Result&lt;T&gt;</c> is an intra-service discriminated union. <c>Envelope&lt;T&gt;</c>
    /// is the cross-service HTTP wrapper. Exposing <c>Result&lt;T&gt;</c> in a serialized
    /// response payload causes deserialization failures in any JSON client that does not share
    /// the <c>SharedKernel.Primitives</c> assembly, breaking the polyglot contract model.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>public class CreateOrderResponse { public Result&lt;Guid&gt; OrderId { get; set; } }</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>public record CreateOrderResponse(Guid OrderId);</code>
    /// </para>
    /// </remarks>
    /// <param name="contractsAssembly">The contracts assembly to evaluate.</param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no public type depends on SharedKernel.Primitives.
    /// </returns>
    public static ConditionList ContractsAssembliesHaveNoResultTypeOnPublicSurface(
        Assembly contractsAssembly) =>
        Types
            .InAssembly(contractsAssembly)
            .That()
            .ArePublic()
            .Should()
            .NotHaveDependencyOn("SharedKernel.Primitives");

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that every non-abstract type in the
    /// supplied assembly that implements <see cref="IIntegrationEvent"/> is sealed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Non-sealed integration events are an inheritance trap. A sub-event changes the wire
    /// format without incrementing <c>[DomainEventVersion]</c>, causing silent schema drift.
    /// <c>sealed</c> or <c>record</c> (records are sealed in IL) ensures the wire contract
    /// is closed.
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
    /// <returns>
    /// A <see cref="ConditionList"/> asserting all non-abstract <c>IIntegrationEvent</c>
    /// implementations are sealed.
    /// </returns>
    public static ConditionList IntegrationEventImplementationsMustBeSealed(
        Assembly contractsAssembly) =>
        Types
            .InAssembly(contractsAssembly)
            .That()
            .ImplementInterface(typeof(IIntegrationEvent))
            .And()
            .AreNotAbstract()
            .Should()
            .BeSealed();
}
