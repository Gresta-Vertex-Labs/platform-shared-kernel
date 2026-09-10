using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails any type implementing <c>IUnitOfWork</c> that
/// declares a number of public instance constructors other than exactly one.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.EfCorePackageHygieneRules"/> to enforce that <c>EfUnitOfWork</c>
/// (and any other <c>IUnitOfWork</c> implementor) has exactly one public instance constructor.
/// <c>EfUnitOfWork</c> was reduced to a single constructor to resolve a DI ambiguity caused
/// by two competing registrations. A second "convenience constructor" would silently reintroduce
/// that ambiguity, causing runtime DI resolution failures.
/// </para>
/// <para>
/// Scope: Only types whose <see cref="TypeDefinition.Interfaces"/> contains an entry with
/// <see cref="InterfaceImplementation.InterfaceType"/>.<see cref="MemberReference.Name"/>
/// equal to <c>"IUnitOfWork"</c> (exact match) are inspected. All other types return
/// <see langword="true"/> unconditionally.
/// </para>
/// <para>
/// Constructor count: counts <see cref="MethodDefinition"/> entries where
/// <see cref="MethodDefinition.IsConstructor"/> is <see langword="true"/> and
/// <see cref="MethodDefinition.IsStatic"/> is <see langword="false"/> (instance constructors only).
/// </para>
/// <para>
/// <strong>Offending pattern:</strong>
/// <code>
/// class EfUnitOfWork : IUnitOfWork {
///     public EfUnitOfWork(AppDbContext ctx) { }
///     public EfUnitOfWork() { }  // ← second constructor — DI ambiguity regression
/// }
/// </code>
/// </para>
/// <para>
/// <strong>Compliant pattern:</strong>
/// <code>
/// class EfUnitOfWork : IUnitOfWork {
///     public EfUnitOfWork(AppDbContext ctx) { }
/// }
/// </code>
/// </para>
/// </remarks>
public sealed class SingleConstructorPredicate : ICustomRule
{
    /// <summary>
    /// Returns <see langword="false"/> (rule violated) when the type implements
    /// <c>IUnitOfWork</c> and declares a number of public instance constructors other than
    /// exactly one; <see langword="true"/> for non-<c>IUnitOfWork</c> types or compliant
    /// implementors.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when an <c>IUnitOfWork</c> implementor has a constructor count
    /// other than one; <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        // Self-scoping: only IUnitOfWork implementors are subject to this rule.
        var implementsIUnitOfWork = false;
        foreach (var iface in type.Interfaces)
        {
            if (iface.InterfaceType.Name == "IUnitOfWork")
            {
                implementsIUnitOfWork = true;
                break;
            }
        }

        if (!implementsIUnitOfWork)
            return true;

        // Count public instance constructors (IsConstructor && !IsStatic).
        var instanceConstructorCount = 0;
        foreach (var method in type.Methods)
        {
            if (method.IsConstructor && !method.IsStatic)
                instanceConstructorCount++;
        }

        return instanceConstructorCount == 1;
    }
}
