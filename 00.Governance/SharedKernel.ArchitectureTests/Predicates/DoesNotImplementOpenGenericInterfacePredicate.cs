using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails any type whose interface list contains an
/// implementation of an open-generic or closed-generic interface whose name starts with
/// a configured prefix.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.DomainLayerPurityRules"/> to enforce that no type in the domain
/// assembly implements <c>IDomainEventHandler&lt;TEvent&gt;</c>. Event handlers belong in
/// <c>05.Application</c> or <c>07.Messaging</c> — never in <c>03.Domain</c>.
/// </para>
/// <para>
/// The check works in IL: <c>TypeDefinition.Interfaces</c> is inspected for
/// <see cref="InterfaceImplementation"/> entries whose <c>InterfaceType.Name</c> starts with
/// the configured prefix. Both generic and non-generic IL forms are covered because the check
/// operates on the interface's own name, not on the full generic instantiation.
/// </para>
/// </remarks>
public sealed class DoesNotImplementOpenGenericInterfacePredicate : ICustomRule
{
    private readonly string _interfaceNamePrefix;

    /// <summary>
    /// Initialises the predicate with the interface name prefix to check for.
    /// Defaults to <c>"IDomainEventHandler"</c>.
    /// </summary>
    /// <param name="interfaceNamePrefix">
    /// The prefix against which each implemented interface's <c>Name</c> is checked.
    /// E.g., <c>"IDomainEventHandler"</c> matches both <c>IDomainEventHandler&lt;T&gt;</c>
    /// and any non-generic form.
    /// </param>
    public DoesNotImplementOpenGenericInterfacePredicate(
        string interfaceNamePrefix = "IDomainEventHandler")
    {
        _interfaceNamePrefix = interfaceNamePrefix;
    }

    /// <summary>
    /// Returns <see langword="true"/> (rule met) if the type does not implement any interface
    /// whose name starts with the configured prefix; <see langword="false"/> otherwise.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when the type implements a forbidden interface;
    /// <see langword="true"/> when no such interface is found.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        if (!type.HasInterfaces)
            return true;

        foreach (var iface in type.Interfaces)
        {
            var ifaceName = iface.InterfaceType.Name;
            if (ifaceName.StartsWith(_interfaceNamePrefix, System.StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
