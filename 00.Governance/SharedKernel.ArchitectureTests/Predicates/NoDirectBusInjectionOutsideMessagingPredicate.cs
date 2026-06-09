using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate (SK0701) that fails any type — outside the
/// <c>SharedKernel.Messaging</c> namespace prefix — that injects a MassTransit transport
/// interface (<c>IBus</c>, <c>IPublishEndpoint</c>, or <c>ISendEndpointProvider</c>) as a
/// constructor parameter.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.MessagingArchitectureRules"/> to enforce that application code
/// depends on the SharedKernel abstraction layer (<c>IMessageBus</c> / <c>IEventPublisher</c>)
/// rather than on the MassTransit transport types directly.
/// </para>
/// <para>
/// <strong>Namespace exemption (first guard):</strong> Types whose
/// <see cref="TypeDefinition.Namespace"/> starts with <c>"SharedKernel.Messaging"</c>
/// return <see langword="true"/> unconditionally — both <c>SharedKernel.Messaging.Abstractions</c>
/// and <c>SharedKernel.Messaging.MassTransit</c> (and all sub-namespaces) are exempt.
/// </para>
/// <para>
/// <strong>Detection:</strong> iterates <see cref="TypeDefinition.Methods"/> where
/// <see cref="MethodDefinition.IsConstructor"/> is <see langword="true"/> and checks each
/// <see cref="ParameterDefinition.ParameterType"/>.<see cref="MemberReference.Name"/>
/// against the forbidden set <c>{"IBus", "IPublishEndpoint", "ISendEndpointProvider"}</c>
/// (exact name match, case-sensitive).
/// </para>
/// <para>
/// <strong>Failure message:</strong>
/// <c>"{offendingType} injects MassTransit transport type '{parameterTypeName}' directly.
/// Use IMessageBus (for commands/queries) or IEventPublisher (for events) from
/// SharedKernel.Messaging.Abstractions instead."</c>
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
public sealed class NoDirectBusInjectionOutsideMessagingPredicate : ICustomRule
{
    private static readonly HashSet<string> ForbiddenTransportTypes = new(StringComparer.Ordinal)
    {
        "IBus",
        "IPublishEndpoint",
        "ISendEndpointProvider",
    };

    /// <summary>
    /// Returns <see langword="true"/> (rule met) for types in the <c>SharedKernel.Messaging</c>
    /// namespace and for types that do not inject any MassTransit transport type in a constructor;
    /// <see langword="false"/> otherwise.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when a non-exempt type injects a forbidden MassTransit transport
    /// type in a constructor; <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        // Namespace exemption — SharedKernel.Messaging.* is the provider boundary
        if (type.Namespace is not null &&
            type.Namespace.StartsWith("SharedKernel.Messaging", StringComparison.Ordinal))
        {
            return true;
        }

        // Constructor scan — check every constructor parameter
        foreach (var method in type.Methods)
        {
            if (!method.IsConstructor)
                continue;

            foreach (var parameter in method.Parameters)
            {
                if (ForbiddenTransportTypes.Contains(parameter.ParameterType.Name))
                    return false;
            }
        }

        return true;
    }
}
