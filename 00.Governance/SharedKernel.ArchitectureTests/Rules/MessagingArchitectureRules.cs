using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates (SK0701–SK0702) that enforce messaging architecture
/// boundaries: MassTransit transport types must not be injected outside the
/// <c>07.Messaging</c> boundary, and <c>IEventPublisher</c> must not be injected in
/// domain-layer types.
/// </summary>
/// <remarks>
/// <para>
/// All factory methods return a <see cref="ConditionList"/> — consistent with the established
/// <c>ArchitectureRuleBase</c> API. Call <c>.GetResult()</c> on the returned
/// <see cref="ConditionList"/> to evaluate the rule, or pass it to
/// <see cref="Helpers.ArchitectureRuleBase.AssertRule"/> to throw on violation.
/// </para>
/// <para>
/// The two rules form a complementary guard:
/// <list type="bullet">
///   <item><description>
///     SK0701 (<see cref="NoDirectBusInjectionOutsideMessaging"/>) — prevents direct
///     MassTransit transport-type injection outside the messaging boundary. All code outside
///     <c>SharedKernel.Messaging.*</c> must inject <c>IMessageBus</c> or
///     <c>IEventPublisher</c> from <c>SharedKernel.Messaging.Abstractions</c>.
///   </description></item>
///   <item><description>
///     SK0702 (<see cref="NoEventPublisherInDomainLayer"/>) — prevents domain types from
///     injecting <c>IEventPublisher</c>. Domain events are dispatched by
///     <c>IDomainEventDispatcher</c> in the application layer; domain types raise events
///     internally via <c>AddDomainEvent()</c> only.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Exemption list for SK0701</strong> (applied inside
/// <see cref="NoDirectBusInjectionOutsideMessagingPredicate"/>):
/// <list type="bullet">
///   <item><description>
///     Types whose namespace starts with <c>"SharedKernel.Messaging"</c> — covers both
///     <c>SharedKernel.Messaging.Abstractions</c> and <c>SharedKernel.Messaging.MassTransit</c>
///     and all sub-namespaces.
///   </description></item>
/// </list>
/// Any additional exemption must be documented in <c>00.Governance/CLAUDE.md</c> before it is
/// applied in code.
/// </para>
/// <para>
/// Introduced in WO-020 P-123. Reference this class with <c>PrivateAssets="all"</c> so it
/// never becomes a transitive production dependency.
/// </para>
/// </remarks>
public static class MessagingArchitectureRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in any of the supplied
    /// assemblies injects a MassTransit transport interface (<c>IBus</c>,
    /// <c>IPublishEndpoint</c>, or <c>ISendEndpointProvider</c>) as a constructor parameter,
    /// unless the type's namespace starts with <c>"SharedKernel.Messaging"</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The namespace exemption covers both <c>SharedKernel.Messaging.Abstractions</c> (which
    /// may wrap transport types) and <c>SharedKernel.Messaging.MassTransit</c> (which owns the
    /// transport wiring). All other assemblies must inject <c>IMessageBus</c> or
    /// <c>IEventPublisher</c> from the abstractions package.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>class PlaceOrderHandler(IBus bus) { }</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>class PlaceOrderHandler(IMessageBus messageBus) { }</code>
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
    /// A <see cref="ConditionList"/> asserting no non-exempt type injects a MassTransit
    /// transport type in a constructor.
    /// </returns>
    public static ConditionList NoDirectBusInjectionOutsideMessaging(params Assembly[] assemblies)
    {
        var types = Types.InAssemblies(assemblies);

        return types
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoDirectBusInjectionOutsideMessagingPredicate());
    }

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied domain
    /// assembly injects <c>IEventPublisher</c> as a constructor parameter when that type is
    /// identified as a domain-layer type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Domain-layer membership signals (either triggers the scope check):</strong>
    /// <list type="bullet">
    ///   <item><description>
    ///     <strong>Namespace signal:</strong> the type's namespace contains <c>".Domain."</c>
    ///     as a substring (dots required — prevents matching application types with "Domain"
    ///     as a bare word fragment).
    ///   </description></item>
    ///   <item><description>
    ///     <strong>Interface signal:</strong> the type implements any of
    ///     <c>IEntity</c>, <c>IAggregateRoot</c>, <c>IValueObject</c>, or
    ///     <c>IDomainService</c> (exact name match).
    ///   </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Correct event publishing flow:</strong> domain type raises event via
    /// <c>AddDomainEvent()</c> → <c>IDomainEventDispatcher</c> dispatches in the application
    /// layer → application handler calls <c>IEventPublisher</c>.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>class OrderAggregate(IEventPublisher publisher) : AggregateRoot&lt;Guid&gt; { }</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>class OrderAggregate : AggregateRoot&lt;Guid&gt; { /* raises via AddDomainEvent() */ }</code>
    /// </para>
    /// </remarks>
    /// <param name="domainAssembly">
    /// The domain assembly to evaluate — supply via <c>typeof(SomeDomainEntity).Assembly</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no domain-layer type injects
    /// <c>IEventPublisher</c> in a constructor.
    /// </returns>
    public static ConditionList NoEventPublisherInDomainLayer(Assembly domainAssembly) =>
        Types
            .InAssembly(domainAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoEventPublisherInDomainLayerPredicate());
}
