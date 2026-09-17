using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails every type whose declaring module references an
/// assembly outside an allow-list: the .NET base class library plus a configured set of exact
/// assembly names.
/// </summary>
/// <remarks>
/// <para>
/// Namespace-based dependency checks (<c>NotHaveDependencyOn</c>) cannot tell two assemblies that
/// share a namespace apart — <c>Microsoft.Extensions.DependencyInjection.Abstractions</c> and the
/// concrete <c>Microsoft.Extensions.DependencyInjection</c> container both declare types in
/// <c>Microsoft.Extensions.DependencyInjection</c>. This predicate reads the compiled module's
/// assembly references instead, so it pins the exact package set a contract assembly may use.
/// </para>
/// <para>
/// The C# compiler omits a reference the code never uses, so this checks what the assembly
/// actually depends on, not what its project file lists.
/// </para>
/// <para>
/// <strong>BCL:</strong> <c>System</c>, <c>System.*</c>, <c>mscorlib</c> and <c>netstandard</c> are
/// always allowed.
/// </para>
/// <para>
/// <strong>Failure shape:</strong> the violation is a property of the module, so every type in a
/// violating assembly fails and appears in <c>FailingTypeNames</c>.
/// </para>
/// </remarks>
public sealed class AssemblyReferenceAllowListPredicate : ICustomRule
{
    private readonly HashSet<string> _allowedAssemblyNames;

    /// <summary>
    /// Initialises the predicate with the non-BCL assembly names the inspected module may reference.
    /// </summary>
    /// <param name="allowedAssemblyNames">
    /// Exact assembly simple names (e.g. <c>"Microsoft.Extensions.DependencyInjection.Abstractions"</c>).
    /// May be empty, which allows the BCL only.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="allowedAssemblyNames"/> is <see langword="null"/>.
    /// </exception>
    public AssemblyReferenceAllowListPredicate(params string[] allowedAssemblyNames)
    {
        ArgumentNullException.ThrowIfNull(allowedAssemblyNames);
        _allowedAssemblyNames = new HashSet<string>(allowedAssemblyNames, StringComparer.Ordinal);
    }

    /// <summary>
    /// Returns <see langword="false"/> (rule violated) when the module declaring
    /// <paramref name="type"/> references any assembly that is neither part of the BCL nor in the
    /// allow-list.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns><see langword="true"/> when every referenced assembly is allowed.</returns>
    public bool MeetsRule(TypeDefinition type) =>
        type.Module.AssemblyReferences.All(reference => IsAllowed(reference.Name));

    private bool IsAllowed(string assemblyName) =>
        IsBaseClassLibrary(assemblyName) || _allowedAssemblyNames.Contains(assemblyName);

    private static bool IsBaseClassLibrary(string assemblyName) =>
        assemblyName is "System" or "mscorlib" or "netstandard"
        || assemblyName.StartsWith("System.", StringComparison.Ordinal);
}
