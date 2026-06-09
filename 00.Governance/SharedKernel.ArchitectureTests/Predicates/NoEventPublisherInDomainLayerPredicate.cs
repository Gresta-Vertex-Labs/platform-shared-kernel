using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate (SK0702) that fails any type identified as belonging to the
/// domain layer that injects <c>IEventPublisher</c> as a constructor parameter.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.MessagingArchitectureRules"/> to enforce that domain types do not
/// reach out to publish integration events directly. Domain events are raised internally via
/// <c>AddDomainEvent()</c> and dispatched by <c>IDomainEventDispatcher</c> in the application
/// layer — domain types must never inject <c>IEventPublisher</c>.
/// </para>
/// <para>
/// <strong>Domain-layer membership (either signal triggers the scope check):</strong>
/// <list type="bullet">
///   <item><description>
///     <strong>Namespace signal:</strong> <see cref="TypeDefinition.Namespace"/> contains
///     <c>".Domain."</c> as a substring — dots on both sides prevent matching application types
///     with <c>"Domain"</c> as a bare word fragment (e.g., <c>"IDomainEventHandler"</c> in an
///     application namespace).
///   </description></item>
///   <item><description>
///     <strong>Interface signal:</strong> <see cref="TypeDefinition.Interfaces"/> contains an
///     entry whose <c>InterfaceType.Name</c> is in
///     <c>{"IEntity", "IAggregateRoot", "IValueObject", "IDomainService"}</c> (exact name match).
///   </description></item>
/// </list>
/// </para>
/// <para>
/// If neither signal is true, the type is not in scope and the predicate returns
/// <see langword="true"/> (rule met) immediately.
/// </para>
/// <para>
/// <strong>Failure message:</strong>
/// <c>"{offendingType} injects IEventPublisher in the domain layer. Domain events are dispatched
/// internally by IDomainEventDispatcher. Correct flow: domain event → IDomainEventDispatcher
/// → application handler → IEventPublisher."</c>
/// </para>
/// <para>
/// <strong>Offending pattern:</strong>
/// <code>class OrderAggregate(IEventPublisher publisher) : AggregateRoot&lt;Guid&gt; { }</code>
/// </para>
/// <para>
/// <strong>Compliant pattern:</strong> raise domain events via <c>AddDomainEvent()</c>;
/// let the application layer dispatch them to <c>IEventPublisher</c> via
/// <c>IDomainEventDispatcher</c>.
/// </para>
/// </remarks>
public sealed class NoEventPublisherInDomainLayerPredicate : ICustomRule
{
    private const string EventPublisherInterfaceName = "IEventPublisher";

    private static readonly HashSet<string> DomainInterfaceSignals = new(StringComparer.Ordinal)
    {
        "IEntity",
        "IAggregateRoot",
        "IValueObject",
        "IDomainService",
    };

    /// <summary>
    /// Returns <see langword="true"/> (rule met) for types that are not in the domain layer,
    /// and for domain-layer types that do not inject <c>IEventPublisher</c> in any constructor;
    /// <see langword="false"/> otherwise.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when a domain-layer type injects <c>IEventPublisher</c> in a
    /// constructor; <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        // Determine domain-layer membership via two signals
        if (!IsDomainLayerType(type))
            return true;

        // Domain-layer type — check constructors for IEventPublisher injection
        foreach (var method in type.Methods)
        {
            if (!method.IsConstructor)
                continue;

            foreach (var parameter in method.Parameters)
            {
                if (parameter.ParameterType.Name == EventPublisherInterfaceName)
                    return false;
            }
        }

        return true;
    }

    private static bool IsDomainLayerType(TypeDefinition type)
    {
        // Namespace signal — ".Domain." with surrounding dots to avoid partial-word matches
        if (type.Namespace is not null && type.Namespace.Contains(".Domain.", StringComparison.Ordinal))
            return true;

        // Interface signal — implements a known domain marker interface
        foreach (var iface in type.Interfaces)
        {
            if (DomainInterfaceSignals.Contains(iface.InterfaceType.Name))
                return true;
        }

        return false;
    }
}
