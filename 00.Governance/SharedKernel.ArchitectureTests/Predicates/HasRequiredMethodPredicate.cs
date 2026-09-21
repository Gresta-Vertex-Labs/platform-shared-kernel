using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails any type that implements an interface matching a
/// specified name prefix but does not declare a method with a specified name.
/// </summary>
/// <remarks>
/// <para>
/// This predicate operates at the concrete implementor level. It does not assert that the
/// interface itself declares the method; it asserts that the concrete implementing type has
/// a method with the required name somewhere in its <see cref="TypeDefinition.Methods"/>
/// collection (exact name match, any overload).
/// </para>
/// <para>
/// <strong>Scope guard:</strong> types that do not implement any interface whose
/// <c>InterfaceType.Name</c> starts with <c>interfaceNamePrefix</c> are returned
/// as passing unconditionally — the predicate self-scopes.
/// </para>
/// <para>
/// <strong>Prefix collision note:</strong> when using the prefix <c>"IRepository"</c>,
/// the predicate will also match <c>"IReadRepository"</c>-implementing types because
/// <c>"IReadRepository"</c> starts with <c>"IRepository"</c>. Callers that need to
/// distinguish write-side from read-side must use the longer prefix <c>"IReadRepository"</c>
/// for the read-side rule and rely on the longer prefix not matching write-side types. For
/// the write-side rule, the implementation explicitly excludes <c>"IReadRepository"</c>
/// implementors — see <see cref="Rules.RepositoryContractCompletenessRules"/> for the
/// exact scoping.
/// </para>
/// <para>
/// <strong>Failure message:</strong>
/// <c>"{offendingType} implements {interfaceNamePrefix}&lt;,&gt; but does not declare {requiredMethodName}."</c>
/// </para>
/// <para>
/// Lives in the <c>Predicates/</c> folder. Used by
/// <see cref="Rules.RepositoryContractCompletenessRules"/>.
/// </para>
/// </remarks>
public sealed class HasRequiredMethodPredicate : ICustomRule
{
    private readonly string _interfaceNamePrefix;
    private readonly string _requiredMethodName;
    private readonly bool _excludeReadRepository;

    /// <summary>
    /// Initialises a new instance of <see cref="HasRequiredMethodPredicate"/>.
    /// </summary>
    /// <param name="interfaceNamePrefix">
    /// The prefix that the implementing interface's simple <c>Name</c> must start with for the
    /// type to be in scope (e.g., <c>"IRepository"</c> or <c>"IReadRepository"</c>).
    /// </param>
    /// <param name="requiredMethodName">
    /// The exact method name that must be present in <see cref="TypeDefinition.Methods"/>
    /// (e.g., <c>"ExistsAsync"</c> or <c>"GetByIdsAsync"</c>).
    /// </param>
    /// <param name="excludeReadRepository">
    /// When <see langword="true"/>, types that implement an <c>IReadRepository</c>-prefixed
    /// interface are excluded from the scope even if they also implement an
    /// <c>IRepository</c>-prefixed interface. Set to <see langword="true"/> when using the
    /// <c>"IRepository"</c> prefix to target write-side repositories only.
    /// </param>
    public HasRequiredMethodPredicate(
        string interfaceNamePrefix,
        string requiredMethodName,
        bool excludeReadRepository = false)
    {
        _interfaceNamePrefix = interfaceNamePrefix;
        _requiredMethodName = requiredMethodName;
        _excludeReadRepository = excludeReadRepository;
    }

    /// <summary>
    /// Returns <see langword="false"/> (rule violated) when the type implements the targeted
    /// interface prefix but does not declare the required method; <see langword="true"/>
    /// otherwise.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when the type is in scope (implements the prefix interface) and
    /// the required method is absent; <see langword="true"/> for all other types.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        // Interfaces (e.g. IRepository : IReadRepository) are contracts, not implementations.
        if (type.IsInterface || !ImplementsTargetInterface(type))
            return true;

        // The method may be declared on the type or inherited from a base class (EfRepository inherits
        // GetByIdsAsync from EfReadRepository). Explicit implementations are named "Namespace.IFoo.Method".
        for (var current = type; current is not null; current = TryResolveBase(current))
        {
            foreach (var method in current.Methods)
            {
                if (string.Equals(method.Name, _requiredMethodName, System.StringComparison.Ordinal)
                    || method.Name.EndsWith("." + _requiredMethodName, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        // Type is in scope but neither declares nor inherits the required method — violation.
        return false;
    }

    private static TypeDefinition? TryResolveBase(TypeDefinition type)
    {
        try
        {
            return type.BaseType?.Resolve();
        }
        catch (AssemblyResolutionException)
        {
            return null; // a base type outside the resolvable assemblies (e.g. System.Object's assembly)
        }
    }

    private bool ImplementsTargetInterface(TypeDefinition type)
    {
        if (!type.HasInterfaces)
            return false;

        bool matchesPrefix = false;

        foreach (var iface in type.Interfaces)
        {
            var ifaceName = iface.InterfaceType.Name;

            // Optionally exclude IReadRepository implementors from write-side check.
            if (_excludeReadRepository
                && ifaceName.StartsWith("IReadRepository", System.StringComparison.Ordinal))
            {
                return false;
            }

            if (ifaceName.StartsWith(_interfaceNamePrefix, System.StringComparison.Ordinal))
                matchesPrefix = true;
        }

        return matchesPrefix;
    }
}
