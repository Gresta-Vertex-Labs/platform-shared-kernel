using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate (SK0706) that fails any non-exempt type that injects
/// <c>MassTransit.IMessageScheduler</c> as a constructor parameter.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.ExtendedMessagingArchitectureRules"/> to enforce that
/// <c>MassTransit.IMessageScheduler</c> is never injected outside the
/// <c>SharedKernel.Messaging.*</c> adapter layer. The platform abstraction
/// <c>SharedKernel.Messaging.Abstractions.IMessageScheduler</c> is the only permitted scheduler
/// injection point in application, domain, and infrastructure code.
/// </para>
/// <para>
/// <strong>Namespace exemption guard (first check):</strong> types whose
/// <see cref="TypeDefinition.Namespace"/> starts with <c>"SharedKernel.Messaging"</c> return
/// <see langword="true"/> unconditionally — the messaging adapter layer may reference
/// <c>MassTransit.IMessageScheduler</c> for internal wiring.
/// </para>
/// <para>
/// <strong>Detection:</strong> for all other types, iterates
/// <see cref="TypeDefinition.Methods"/> where <c>IsConstructor</c> is <see langword="true"/>.
/// For each constructor parameter, checks two conditions — both must be true to fire:
/// <list type="number">
///   <item><description><c>ParameterType.Name == "IMessageScheduler"</c> (exact name match).</description></item>
///   <item><description>
///     <c>ParameterType.Namespace.StartsWith("MassTransit")</c> — distinguishes
///     <c>MassTransit.IMessageScheduler</c> (forbidden) from
///     <c>SharedKernel.Messaging.Abstractions.IMessageScheduler</c> (permitted).
///   </description></item>
/// </list>
/// <strong>Fallback:</strong> if <c>ParameterType.Namespace</c> is empty or null (the type
/// reference could not be fully resolved), the predicate falls back to checking
/// <c>ParameterType.Scope.Name.Contains("MassTransit")</c> — the Mono.Cecil scope name for an
/// externally-referenced type includes the assembly name, which contains <c>"MassTransit"</c>
/// for MassTransit types.
/// </para>
/// <para>
/// <strong>Failure message:</strong>
/// <c>"{offendingType} injects MassTransit.IMessageScheduler directly. Use
/// SharedKernel.Messaging.Abstractions.IMessageScheduler to preserve transport
/// independence."</c>
/// </para>
/// </remarks>
public sealed class NoDirectSchedulerInjectionOutsideMessagingPredicate : ICustomRule
{
    private const string MessagingNamespacePrefix = "SharedKernel.Messaging";
    private const string MessageSchedulerTypeName = "IMessageScheduler";
    private const string MassTransitNamespacePrefix = "MassTransit";

    /// <summary>
    /// Returns <see langword="true"/> (rule met) for types in the
    /// <c>SharedKernel.Messaging.*</c> namespace, and for all other types whose constructors do
    /// not inject <c>MassTransit.IMessageScheduler</c>; <see langword="false"/> otherwise.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when a non-exempt type injects
    /// <c>MassTransit.IMessageScheduler</c> in a constructor; <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        if (type.Namespace is not null &&
            type.Namespace.StartsWith(MessagingNamespacePrefix, System.StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var method in type.Methods)
        {
            if (!method.IsConstructor)
                continue;

            foreach (var parameter in method.Parameters)
            {
                var parameterType = parameter.ParameterType;

                if (parameterType.Name != MessageSchedulerTypeName)
                    continue;

                var parameterNamespace = parameterType.Namespace ?? string.Empty;

                if (parameterNamespace.StartsWith(MassTransitNamespacePrefix, System.StringComparison.Ordinal))
                    return false;

                if (parameterNamespace.Length == 0 &&
                    parameterType.Scope?.Name is { } scopeName &&
                    scopeName.Contains(MassTransitNamespacePrefix, System.StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
