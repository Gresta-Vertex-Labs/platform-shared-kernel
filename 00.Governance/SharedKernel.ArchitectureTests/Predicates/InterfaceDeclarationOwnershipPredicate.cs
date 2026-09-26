using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails any type whose <see cref="TypeDefinition.Name"/>
/// matches one of a configured set of interface names.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.PersistenceInterfaceOwnershipRules"/> to assert that certain
/// interface declarations (e.g., <c>IUserContext</c>) exist only in
/// their designated owner assembly (<c>SharedKernel.Security.Abstractions</c>). The rule is
/// applied to every other assembly under test; the owner assembly itself is never passed to the
/// rule.
/// </para>
/// <para>
/// <strong>Design:</strong> Stateless — no cached state. Construct a new instance per call site.
/// The name set is supplied at construction time and is immutable for the lifetime of the
/// predicate.
/// </para>
/// <para>
/// <strong>Failure message:</strong> Names the offending type and the assembly in which it was
/// found, so the consumer can identify which non-owner assembly contains the re-declaration.
/// </para>
/// <para>
/// <strong>Offending pattern (Rule 1):</strong>
/// <code>interface IUserContext { Guid UserId { get; } } // inside SharedKernel.Persistence.EfCore</code>
/// </para>
/// <para>
/// <strong>Compliant pattern:</strong>
/// <code>// Reference SharedKernel.Security.Abstractions.IUserContext instead</code>
/// </para>
/// </remarks>
public sealed class InterfaceDeclarationOwnershipPredicate : ICustomRule
{
    private readonly System.Collections.Generic.HashSet<string> _forbiddenNames;

    /// <summary>
    /// Initialises the predicate with the set of interface type names whose declarations are
    /// forbidden in any non-owner assembly.
    /// </summary>
    /// <param name="forbiddenInterfaceNames">
    /// The simple CLR type names (e.g., <c>"IUserContext"</c>) that must not be declared outside
    /// the designated owner assembly.
    /// </param>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="forbiddenInterfaceNames"/> is <see langword="null"/> or empty.
    /// </exception>
    public InterfaceDeclarationOwnershipPredicate(params string[] forbiddenInterfaceNames)
    {
        if (forbiddenInterfaceNames is null || forbiddenInterfaceNames.Length == 0)
            throw new System.ArgumentNullException(nameof(forbiddenInterfaceNames),
                "At least one interface name must be supplied.");

        _forbiddenNames = new System.Collections.Generic.HashSet<string>(
            forbiddenInterfaceNames,
            System.StringComparer.Ordinal);
    }

    /// <summary>
    /// Returns <see langword="false"/> (rule violated) when the inspected type's
    /// <see cref="TypeDefinition.Name"/> is in the configured forbidden name set;
    /// <see langword="true"/> otherwise.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when the type name matches a forbidden interface name;
    /// <see langword="true"/> for all other types.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        // Return false (violated) when the type's simple name is in the forbidden set.
        // The failure message is surfaced by NetArchTest via the type name and, where
        // visible, the assembly name — both are embedded in FailingTypeNames.
        return !_forbiddenNames.Contains(type.Name);
    }
}
