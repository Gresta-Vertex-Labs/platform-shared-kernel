using System.Reflection;
using Mono.Cecil;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that enforce interface declaration ownership and
/// read-side repository contract correctness, codifying the P-078 and P-080 interface
/// migration decisions.
/// </summary>
/// <remarks>
/// <para>
/// Four factory methods, each returning a <see cref="ConditionList"/> consistent with the
/// established <c>ArchitectureRuleBase</c> API:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <see cref="IUserContextDeclaredOnlyInSecurityAbstractions"/> — <c>IUserContext</c> must
///     not be declared outside <c>SharedKernel.Security.Abstractions</c> (P-078 migration).
///   </description></item>
///   <item><description>
///     <see cref="TenantIdentityInterfacesDeclaredOnlyInSecurityAbstractions"/> —
///     <c>ITenantProvider</c> and <c>ICurrentTenantService</c> must not be re-declared
///     outside <c>SharedKernel.Security.Abstractions</c>.
///   </description></item>
///   <item><description>
///     <see cref="IReadRepositoryMustNotExposeIQueryable"/> — types implementing an
///     <c>IReadRepository</c>-prefixed interface must not return <c>IQueryable&lt;T&gt;</c>.
///   </description></item>
///   <item><description>
///     <see cref="NoGetByIdAsyncOnReadRepository"/> — types implementing an
///     <c>IReadRepository</c>-prefixed interface must not declare <c>GetByIdAsync</c> (removed
///     in P-080 to eliminate duplication with the write-side <c>IRepository</c>).
///   </description></item>
/// </list>
/// <para>
/// <strong>Caller contract for Rules 1 and 2:</strong> do <em>not</em> pass
/// <c>SharedKernel.Security.Abstractions</c> to these methods — only pass the assemblies to be
/// checked for erroneous re-declarations. These rules assert <em>absence</em> everywhere else,
/// not presence in the owner.
/// </para>
/// <para>
/// Reference this class with <c>PrivateAssets="all"</c> so it never becomes a transitive
/// production dependency.
/// </para>
/// </remarks>
public static class PersistenceInterfaceOwnershipRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type named
    /// <c>IUserContext</c> is declared in any of the supplied assemblies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>IUserContext</c> was migrated to <c>SharedKernel.Security.Abstractions</c> in P-078.
    /// Local re-declarations in persistence or application layers duplicate the contract and
    /// break the single-source-of-truth principle for security identity abstractions.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>interface IUserContext { Guid UserId { get; } } // inside SharedKernel.Persistence.EfCore</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>// Inject SharedKernel.Security.Abstractions.IUserContext</code>
    /// </para>
    /// </remarks>
    /// <param name="assemblies">
    /// The assemblies to check for erroneous <c>IUserContext</c> declarations.
    /// Do <em>not</em> include <c>SharedKernel.Security.Abstractions</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no type named <c>IUserContext</c> exists in the
    /// supplied assemblies.
    /// </returns>
    public static ConditionList IUserContextDeclaredOnlyInSecurityAbstractions(
        params Assembly[] assemblies)
    {
        var predicate = new InterfaceDeclarationOwnershipPredicate("IUserContext");
        return BuildMultiAssemblyRule(assemblies, predicate);
    }

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type named
    /// <c>ITenantProvider</c> or <c>ICurrentTenantService</c> is declared in any of the
    /// supplied assemblies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tenant-identity contracts belong exclusively in <c>SharedKernel.Security.Abstractions</c>.
    /// Duplicates in other layers (e.g., a local <c>ICurrentTenantService</c> in
    /// <c>SharedKernel.Persistence.EfCore</c>) cause silent mismatches when the owner interface
    /// evolves.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>interface ICurrentTenantService { Guid TenantId { get; } } // inside SharedKernel.Persistence.EfCore</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>// Reference SharedKernel.Security.Abstractions.ICurrentTenantService</code>
    /// </para>
    /// </remarks>
    /// <param name="assemblies">
    /// The assemblies to check for erroneous tenant-identity interface declarations.
    /// Do <em>not</em> include <c>SharedKernel.Security.Abstractions</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no type named <c>ITenantProvider</c> or
    /// <c>ICurrentTenantService</c> exists in the supplied assemblies.
    /// </returns>
    public static ConditionList TenantIdentityInterfacesDeclaredOnlyInSecurityAbstractions(
        params Assembly[] assemblies)
    {
        var predicate = new InterfaceDeclarationOwnershipPredicate(
            "ITenantProvider",
            "ICurrentTenantService");
        return BuildMultiAssemblyRule(assemblies, predicate);
    }

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type implementing an
    /// <c>IReadRepository</c>-prefixed interface has a method or property returning
    /// <c>IQueryable</c> or <c>IQueryable&lt;T&gt;</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This rule is scoped to <c>IReadRepository</c>-prefix types specifically (as opposed to
    /// the broader <c>IRepository</c> prefix used by
    /// <see cref="PersistenceLayerProtectionRules.RepositoriesMustNotExposeIQueryable"/>). Both
    /// rules may run in the same test suite without conflict.
    /// </para>
    /// <para>
    /// <c>IQueryable&lt;T&gt;</c> on a read-side repository leaks EF Core execution semantics
    /// into application handlers. Query surface belongs on <c>Specification&lt;T&gt;</c>, not
    /// on raw <c>IQueryable</c> return types.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>IQueryable&lt;Order&gt; GetAll(); // on an IReadRepository implementor</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>Task&lt;IReadOnlyList&lt;Order&gt;&gt; FindAsync(ISpecification&lt;Order&gt; spec);</code>
    /// </para>
    /// </remarks>
    /// <param name="assembly">The assembly to evaluate.</param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no <c>IReadRepository</c> implementor exposes
    /// <c>IQueryable</c>-returning methods.
    /// </returns>
    public static ConditionList IReadRepositoryMustNotExposeIQueryable(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new ReadRepositoryNoIQueryablePredicate());

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type implementing an
    /// <c>IReadRepository</c>-prefixed interface declares a method named
    /// <c>GetByIdAsync</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>GetByIdAsync</c> was removed from <c>IReadRepository&lt;T,TId&gt;</c> in P-080 to
    /// eliminate duplication with the write-side <c>IRepository&lt;T,TId&gt;</c>. Re-declaring
    /// it on a concrete implementor reintroduces the anti-pattern and diverges from the platform
    /// read/write split contract.
    /// </para>
    /// <para>
    /// The rule fires on abstract base classes too — the method must be removed at every
    /// declaration level.
    /// </para>
    /// <para>
    /// <strong>Failure message content:</strong>
    /// <c>{type}.GetByIdAsync must be removed — use FindByIdAsync (returns Result&lt;T&gt;) or
    /// GetAsync (returns T?) instead. GetByIdAsync was removed in P-080 to eliminate
    /// duplication.</c>
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>Task&lt;Order?&gt; GetByIdAsync(Guid id); // on an IReadRepository implementor</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>Task&lt;Order?&gt; GetAsync(Guid id); // or: Task&lt;Result&lt;Order&gt;&gt; FindByIdAsync(Guid id);</code>
    /// </para>
    /// </remarks>
    /// <param name="assembly">The assembly to evaluate.</param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no <c>IReadRepository</c> implementor declares
    /// <c>GetByIdAsync</c>.
    /// </returns>
    public static ConditionList NoGetByIdAsyncOnReadRepository(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoGetByIdOnReadRepositoryPredicate());

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a single <see cref="ConditionList"/> that applies the supplied predicate to
    /// all types across multiple assemblies by chaining <c>And()</c> clauses.
    /// </summary>
    /// <remarks>
    /// When only one assembly is supplied the result is a direct single-assembly scan.
    /// When multiple assemblies are supplied, each additional assembly's types are appended
    /// via an <c>And()</c> chain so that a single <see cref="ConditionList"/> is returned.
    /// </remarks>
    private static ConditionList BuildMultiAssemblyRule(
        Assembly[] assemblies,
        ICustomRule predicate)
    {
        if (assemblies is null || assemblies.Length == 0)
            throw new System.ArgumentException("At least one assembly must be supplied.", nameof(assemblies));

        var result = Types
            .InAssembly(assemblies[0])
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(predicate);

        for (int i = 1; i < assemblies.Length; i++)
        {
            result = result
                .And()
                .MeetCustomRule(predicate);
        }

        return result;
    }
}

/// <summary>
/// Internal variant of <see cref="NoIQueryableReturnPredicate"/> scoped exclusively to
/// <c>IReadRepository</c>-prefixed interface implementors (as opposed to the broader
/// <c>IRepository</c> prefix).
/// </summary>
/// <remarks>
/// Provides a targeted failure message for read-side violations, complementing
/// <see cref="PersistenceLayerProtectionRules.RepositoriesMustNotExposeIQueryable"/>.
/// Both rules may run in the same test suite without conflict.
/// </remarks>
internal sealed class ReadRepositoryNoIQueryablePredicate : ICustomRule
{
    private const string ReadRepositoryPrefix = "IReadRepository";

    /// <inheritdoc />
    public bool MeetsRule(TypeDefinition type)
    {
        if (!ImplementsReadRepositoryInterface(type))
            return true;

        foreach (var method in type.Methods)
        {
            if (method.IsConstructor || method.IsGetter)
                continue;

            if (ReturnsIQueryable(method.ReturnType))
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

    private static bool ReturnsIQueryable(TypeReference returnType)
    {
        if (returnType.Name == "IQueryable")
            return true;

        if (returnType.FullName is not null &&
            returnType.FullName.Contains("IQueryable", System.StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
