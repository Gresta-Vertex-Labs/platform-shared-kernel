using System.Reflection;
using Mono.Cecil;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that enforce interface declaration ownership and
/// read-side repository contract correctness, codifying two earlier interface
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
///     not be declared outside <c>SharedKernel.Security.Abstractions</c>.
///   </description></item>
///   <item><description>
///     <see cref="TenantIdentityInterfacesAreNeverRedeclared"/> —
///     <c>ITenantProvider</c>, <c>ICurrentTenantService</c> and <c>ITenantContextAccessor</c> are never
///     re-declared: the tenant is <c>IRequestContext.TenantId</c>.
///   </description></item>
///   <item><description>
///     <see cref="IReadRepositoryMustNotExposeIQueryable"/> — types implementing an
///     <c>IReadRepository</c>-prefixed interface must not return <c>IQueryable&lt;T&gt;</c>.
///   </description></item>
///   <item><description>
///     <see cref="ReadOnlyRepositoriesNeverTrack"/> — types implementing an
///     <c>IReadRepository</c>-prefixed interface but no <c>IRepository</c>-prefixed one must never call
///     <c>AsTracking</c> (the read contract never tracks).
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
    /// <c>IUserContext</c> was migrated to <c>SharedKernel.Security.Abstractions</c>.
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
    /// Returns a <see cref="ConditionList"/> asserting that no type named <c>ITenantProvider</c>,
    /// <c>ICurrentTenantService</c> or <c>ITenantContextAccessor</c> is declared in any of the supplied assemblies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The caller's tenant is <c>IRequestContext.TenantId</c> (<c>SharedKernel.Execution.Context</c>). WO-086/P-565
    /// deleted every second tenant-identity interface (<c>ITenantProvider</c>, Messaging's
    /// <c>ITenantContextAccessor</c>, the persistence-local tenant services); a re-declared one splits the source of
    /// truth again and silently disagrees with the one persistence filters by.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>interface ICurrentTenantService { Guid TenantId { get; } } // inside SharedKernel.Persistence.EfCore</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>// Inject SharedKernel.Execution.Context.IRequestContext and read TenantId</code>
    /// </para>
    /// </remarks>
    /// <param name="assemblies">The assemblies to check for re-declared tenant-identity interfaces.</param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting none of the three names is declared in the supplied assemblies.
    /// </returns>
    public static ConditionList TenantIdentityInterfacesAreNeverRedeclared(
        params Assembly[] assemblies)
    {
        var predicate = new InterfaceDeclarationOwnershipPredicate(
            "ITenantProvider",
            "ICurrentTenantService",
            "ITenantContextAccessor");
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
    /// Returns a <see cref="ConditionList"/> asserting that no read-only repository — a type implementing an
    /// <c>IReadRepository</c>-prefixed interface but no <c>IRepository</c>-prefixed one — calls
    /// <c>AsTracking</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The read contract never tracks: an entity returned by <c>IReadRepository</c> is detached, so a change to
    /// it can never be saved by accident, and reads skip the change tracker's snapshot cost. Tracked loads
    /// belong to the write-side <c>IRepository</c> (which extends the read contract and is exempt here).
    /// </para>
    /// <para>
    /// Method bodies are inspected including compiler-generated nested types (async state machines, lambdas),
    /// so an <c>AsTracking()</c> call inside an <c>async</c> method is found.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>_context.Set&lt;Order&gt;().AsTracking().FirstOrDefaultAsync(...) // in an IReadRepository-only type</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>_context.Set&lt;Order&gt;().AsNoTracking().FirstOrDefaultAsync(...)</code>
    /// </para>
    /// </remarks>
    /// <param name="assembly">The assembly to evaluate.</param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no read-only repository calls <c>AsTracking</c>.
    /// </returns>
    public static ConditionList ReadOnlyRepositoriesNeverTrack(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new ReadOnlyRepositoryNeverTracksPredicate());

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
