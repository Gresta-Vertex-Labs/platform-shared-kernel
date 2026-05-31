using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails any type implementing <c>IDomainService</c>
/// whose constructors accept parameters whose types are in forbidden infrastructure namespaces.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.DomainLayerPurityRules"/> to enforce that domain services have no
/// infrastructure constructor parameters. Domain services may only accept <c>IClock</c>, other
/// domain interfaces, and primitives from <c>01.Core</c> in their constructors.
/// </para>
/// <para>
/// <strong>Forbidden namespace prefixes:</strong>
/// <c>Microsoft.EntityFrameworkCore</c>, <c>MassTransit</c>, <c>StackExchange.Redis</c>,
/// <c>RabbitMQ.Client</c>.
/// </para>
/// <para>
/// The check is scoped to types whose <see cref="TypeDefinition.Interfaces"/> contains an entry
/// whose <c>InterfaceType.Name</c> equals <c>"IDomainService"</c>.
/// </para>
/// </remarks>
public sealed class NoInfrastructureConstructorParametersPredicate : ICustomRule
{
    private static readonly string[] ForbiddenNamespacePrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "MassTransit",
        "StackExchange.Redis",
        "RabbitMQ.Client",
    ];

    private const string DomainServiceInterfaceName = "IDomainService";

    /// <summary>
    /// Returns <see langword="true"/> (rule met) if the type does not implement
    /// <c>IDomainService</c>, or if it does implement <c>IDomainService</c> but no constructor
    /// parameter has a type in a forbidden infrastructure namespace.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when a domain service has an infrastructure constructor parameter;
    /// <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        // Only inspect types that implement IDomainService
        if (!ImplementsDomainService(type))
            return true;

        foreach (var method in type.Methods)
        {
            if (!method.IsConstructor)
                continue;

            foreach (var param in method.Parameters)
            {
                var paramNamespace = param.ParameterType.Namespace ?? string.Empty;
                foreach (var forbiddenPrefix in ForbiddenNamespacePrefixes)
                {
                    if (paramNamespace.StartsWith(forbiddenPrefix, System.StringComparison.Ordinal))
                        return false;
                }
            }
        }

        return true;
    }

    private static bool ImplementsDomainService(TypeDefinition type)
    {
        if (!type.HasInterfaces)
            return false;

        foreach (var iface in type.Interfaces)
        {
            if (iface.InterfaceType.Name == DomainServiceInterfaceName)
                return true;
        }

        return false;
    }
}
