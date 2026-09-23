using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails every type whose declaring module references any
/// assembly on a deny-list.
/// </summary>
/// <remarks>
/// <para>
/// The inverse of <see cref="AssemblyReferenceAllowListPredicate"/>, and the right shape when a
/// package has a large, legitimate and growing dependency set but a handful of specific packages
/// it must never take. An allow-list there would need editing every time an unrelated dependency
/// is added, and would fail for the wrong reason whenever someone forgot.
/// </para>
/// <para>
/// Reads the module's assembly-reference table rather than type references, so it fails a project
/// that has declared the dependency even before any code uses it — when the reference is added is
/// when a reviewer can still cheaply ask why.
/// </para>
/// <para>
/// <strong>Failure shape:</strong> the violation is a property of the module, so every type in a
/// violating assembly fails and appears in <c>FailingTypeNames</c>.
/// </para>
/// </remarks>
public sealed class ForbiddenAssemblyReferencePredicate : ICustomRule
{
    private readonly HashSet<string> _forbiddenAssemblyNames;

    /// <summary>
    /// Initialises the predicate with the assembly names the inspected module must not reference.
    /// </summary>
    /// <param name="forbiddenAssemblyNames">
    /// Exact assembly simple names, for example <c>MediatR</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="forbiddenAssemblyNames"/> is <see langword="null"/>.
    /// </exception>
    public ForbiddenAssemblyReferencePredicate(params string[] forbiddenAssemblyNames)
    {
        ArgumentNullException.ThrowIfNull(forbiddenAssemblyNames);
        _forbiddenAssemblyNames = new HashSet<string>(forbiddenAssemblyNames, StringComparer.Ordinal);
    }

    /// <summary>
    /// Returns <see langword="false"/> (rule violated) when the module declaring
    /// <paramref name="type"/> references any denied assembly.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns><see langword="true"/> when no denied assembly is referenced.</returns>
    public bool MeetsRule(TypeDefinition type) =>
        !type.Module.AssemblyReferences.Any(reference => _forbiddenAssemblyNames.Contains(reference.Name));
}
