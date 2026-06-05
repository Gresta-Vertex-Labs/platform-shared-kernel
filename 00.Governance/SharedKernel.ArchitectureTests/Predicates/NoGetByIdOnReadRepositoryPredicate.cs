using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails any type implementing an
/// <c>IReadRepository</c>-prefixed interface that declares a method named
/// <c>GetByIdAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.PersistenceInterfaceOwnershipRules"/> to enforce that
/// <c>GetByIdAsync</c> — removed from <c>IReadRepository&lt;T,TId&gt;</c> in P-080 to
/// eliminate duplication with the write-side <c>IRepository</c> — is not re-introduced
/// on any concrete read-side repository implementor.
/// </para>
/// <para>
/// <strong>Scope:</strong> Only types whose <see cref="TypeDefinition.Interfaces"/> contains
/// an entry whose <c>InterfaceType.Name</c> starts with <c>"IReadRepository"</c> are
/// inspected. Types not implementing any <c>IReadRepository</c>-prefixed interface are
/// returned as passing unconditionally.
/// </para>
/// <para>
/// <strong>Detection:</strong> An exact method name match on <c>"GetByIdAsync"</c> across
/// all <see cref="TypeDefinition.Methods"/>. The rule fires on both direct declaration and
/// any overload variant — any method named <c>GetByIdAsync</c> is a violation regardless
/// of its parameter signature.
/// </para>
/// <para>
/// <strong>Failure message:</strong> Names the offending type and references
/// <c>FindByIdAsync</c> (returns <c>Result&lt;T&gt;</c>) and <c>GetAsync</c>
/// (returns <c>T?</c>) as the correct alternatives.
/// </para>
/// <para>
/// <strong>Offending pattern:</strong>
/// <code>Task&lt;Order?&gt; GetByIdAsync(Guid id) // on an IReadRepository implementor</code>
/// </para>
/// <para>
/// <strong>Compliant pattern:</strong>
/// <code>Task&lt;Order?&gt; GetAsync(Guid id) // or: Task&lt;Result&lt;Order&gt;&gt; FindByIdAsync(Guid id)</code>
/// </para>
/// </remarks>
public sealed class NoGetByIdOnReadRepositoryPredicate : ICustomRule
{
    private const string ForbiddenMethodName = "GetByIdAsync";
    private const string ReadRepositoryPrefix = "IReadRepository";

    /// <summary>
    /// Returns <see langword="false"/> (rule violated) when the type implements an
    /// <c>IReadRepository</c>-prefixed interface and declares a method named
    /// <c>GetByIdAsync</c>; <see langword="true"/> otherwise.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when an <c>IReadRepository</c> implementor declares
    /// <c>GetByIdAsync</c>; <see langword="true"/> for all other types.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        // Only inspect types that implement an IReadRepository-prefixed interface.
        if (!ImplementsReadRepositoryInterface(type))
            return true;

        foreach (var method in type.Methods)
        {
            if (string.Equals(method.Name, ForbiddenMethodName, System.StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool ImplementsReadRepositoryInterface(TypeDefinition type)
    {
        if (!type.HasInterfaces)
            return false;

        foreach (var iface in type.Interfaces)
        {
            if (iface.InterfaceType.Name.StartsWith(ReadRepositoryPrefix, System.StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
