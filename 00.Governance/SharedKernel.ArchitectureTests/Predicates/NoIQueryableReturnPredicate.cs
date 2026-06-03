using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails any type implementing an
/// <c>IRepository</c>-prefixed interface whose non-constructor, non-getter methods return
/// <c>IQueryable</c> or <c>IQueryable&lt;T&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.PersistenceLayerProtectionRules"/> to enforce that repository
/// implementations never expose raw <c>IQueryable&lt;T&gt;</c> return types. Leaking
/// <c>IQueryable</c> couples application handlers to EF Core expression-tree execution
/// semantics, defeating the read/write split. Query surface belongs exclusively on
/// <c>IReadRepository</c> via <c>Specification&lt;T&gt;</c>.
/// </para>
/// <para>
/// <strong>Scope:</strong> Only types whose <see cref="TypeDefinition.Interfaces"/> contains
/// an entry whose <c>InterfaceType.Name</c> starts with <c>"IRepository"</c> are inspected.
/// This deliberately covers both <c>IRepository&lt;T,TId&gt;</c> and
/// <c>IReadRepository&lt;T,TId&gt;</c> and any sub-interface with the same prefix.
/// </para>
/// <para>
/// <strong>Detection:</strong> A method return type matches when
/// <c>ReturnType.Name == "IQueryable"</c> (non-generic form) or
/// <c>ReturnType.FullName.Contains("IQueryable")</c> (generic form in IL such as
/// <c>System.Linq.IQueryable`1</c>). Both checks are required to cover all IL representations.
/// </para>
/// <para>
/// <strong>Offending pattern:</strong>
/// <code>IQueryable&lt;Order&gt; GetAll();</code>
/// </para>
/// <para>
/// <strong>Compliant pattern:</strong>
/// <code>Task&lt;IReadOnlyList&lt;Order&gt;&gt; FindAsync(ISpecification&lt;Order&gt; spec);</code>
/// </para>
/// </remarks>
public sealed class NoIQueryableReturnPredicate : ICustomRule
{
    /// <summary>
    /// Returns <see langword="true"/> (rule met) when the type either does not implement an
    /// <c>IRepository</c>-prefixed interface or none of its qualifying methods return
    /// <c>IQueryable</c>; <see langword="false"/> when a forbidden return type is found.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when an <c>IRepository</c> implementor exposes an
    /// <c>IQueryable</c>-returning method; <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        // Only inspect types that implement an IRepository-prefixed interface.
        if (!ImplementsIRepositoryPrefixedInterface(type))
            return true;

        foreach (var method in type.Methods)
        {
            // Skip constructors and property getters — not part of the query surface contract.
            if (method.IsConstructor || method.IsGetter)
                continue;

            if (ReturnsIQueryable(method.ReturnType))
                return false;
        }

        return true;
    }

    private static bool ImplementsIRepositoryPrefixedInterface(TypeDefinition type)
    {
        if (!type.HasInterfaces)
            return false;

        foreach (var iface in type.Interfaces)
        {
            if (iface.InterfaceType.Name.StartsWith("IRepository", System.StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool ReturnsIQueryable(TypeReference returnType)
    {
        // Non-generic form: IQueryable
        if (returnType.Name == "IQueryable")
            return true;

        // Generic form in IL: System.Linq.IQueryable`1 or any variant containing "IQueryable"
        if (returnType.FullName is not null &&
            returnType.FullName.Contains("IQueryable", System.StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
