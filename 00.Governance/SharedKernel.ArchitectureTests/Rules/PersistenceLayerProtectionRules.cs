using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that protect the EF Core persistence layer's three most
/// critical contracts: only <c>EfUnitOfWork</c> may call <c>SaveChanges[Async]</c>,
/// <c>IRepository</c> implementations must never return <c>IQueryable</c>, and
/// <c>03.Domain</c> assemblies must never reference EF Core, Npgsql, or any
/// <c>SharedKernel.Persistence.*</c> package.
/// </summary>
/// <remarks>
/// <para>
/// All three factory methods accept an <see cref="Assembly"/> parameter and return a
/// <see cref="ConditionList"/> — consistent with the established <c>ArchitectureRuleBase</c> API.
/// </para>
/// <para>
/// Rule 3 (<see cref="DomainAssembliesNeverReferencePersistenceStack"/>) is additive with
/// <see cref="DomainLayerPurityRules.DomainAssembliesNeverReferenceInfrastructure"/> — both
/// rules may run in the same test suite. They are not duplicates: the latter covers broad
/// infrastructure terms; this rule adds Npgsql and the in-repo persistence packages as a
/// WO-013-scoped gate. Never remove either in favour of the other.
/// </para>
/// <para>
/// Reference this class with <c>PrivateAssets="all"</c> so it never becomes a transitive
/// production dependency.
/// </para>
/// </remarks>
public static class PersistenceLayerProtectionRules
{
    /// <summary>
    /// Forbidden persistence-stack assembly name substrings for Rule 3.
    /// NetArchTest's <c>NotHaveDependencyOn</c> performs substring matching against
    /// referenced assembly full names.
    /// </summary>
    private static readonly string[] ForbiddenPersistenceTerms =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "SharedKernel.Persistence",
    ];

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied assembly
    /// calls <c>DbContext.SaveChanges</c> or <c>DbContext.SaveChangesAsync</c> directly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Types whose namespace starts with <c>"SharedKernel.Persistence.EfCore"</c> are
    /// exempted unconditionally inside <see cref="NoDirectSaveChangesPredicate"/> — this is
    /// the <c>EfUnitOfWork</c> exclusion. The exemption is enforced inside the predicate, not
    /// at the call site, so the rule correctly self-documents the single permitted caller.
    /// </para>
    /// <para>
    /// Calling <c>SaveChangesAsync</c> directly bypasses the EF Core interceptor chain
    /// (<c>AuditInterceptor</c>, <c>SoftDeleteInterceptor</c>, <c>OutboxInterceptor</c>,
    /// <c>ConcurrencyInterceptor</c>). Only <c>EfUnitOfWork</c> may commit — all other code
    /// must call <c>IUnitOfWork.CommitAsync()</c>.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>await _dbContext.SaveChangesAsync();</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>await _unitOfWork.CommitAsync();</code>
    /// </para>
    /// </remarks>
    /// <param name="assembly">The assembly to evaluate.</param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no type calls <c>DbContext.SaveChanges[Async]</c>
    /// directly (outside the EF Core namespace exemption).
    /// </returns>
    public static ConditionList OnlyEfUnitOfWorkMayCallSaveChanges(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoDirectSaveChangesPredicate());

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type implementing an
    /// <c>IRepository</c>-prefixed interface has a method returning <c>IQueryable</c> or
    /// <c>IQueryable&lt;T&gt;</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>IQueryable&lt;T&gt;</c> leaks EF Core expression-tree execution semantics into the
    /// application layer, making handler code dependent on EF Core internals. Query surface
    /// belongs exclusively on <c>IReadRepository</c> via <c>Specification&lt;T&gt;</c>; the
    /// write-side <c>IRepository&lt;T,TId&gt;</c> is scoped to mutation operations only.
    /// </para>
    /// <para>
    /// Scope covers <c>IRepository&lt;T,TId&gt;</c>, <c>IReadRepository&lt;T,TId&gt;</c>,
    /// and any sub-interface with the <c>"IRepository"</c> name prefix.
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
    /// <param name="assembly">The assembly to evaluate.</param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no <c>IRepository</c> implementor exposes
    /// <c>IQueryable</c>-returning methods.
    /// </returns>
    public static ConditionList RepositoriesMustNotExposeIQueryable(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoIQueryableReturnPredicate());

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied domain
    /// assembly has a dependency on <c>Microsoft.EntityFrameworkCore</c>, <c>Npgsql</c>, or
    /// any <c>SharedKernel.Persistence.*</c> package.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This rule is additive with
    /// <see cref="DomainLayerPurityRules.DomainAssembliesNeverReferenceInfrastructure"/> —
    /// both rules may run in the same test suite. The broader rule covers <c>EntityFramework</c>,
    /// <c>MassTransit</c>, <c>Redis</c>, and <c>RabbitMQ</c>; this rule adds Npgsql and the
    /// in-repo persistence packages as a WO-013-scoped gate. Never remove either in favour of
    /// the other.
    /// </para>
    /// <para>
    /// Any EF Core, Npgsql, or <c>SharedKernel.Persistence.*</c> reference inside
    /// <c>03.Domain</c> destroys DDD isolation and makes domain logic impossible to unit-test
    /// without a database.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>[Key] attribute from Microsoft.EntityFrameworkCore on a domain entity</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>Domain entity with no infrastructure annotations</code>
    /// </para>
    /// </remarks>
    /// <param name="assembly">The domain assembly to evaluate.</param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no persistence-stack dependency is present.
    /// </returns>
    public static ConditionList DomainAssembliesNeverReferencePersistenceStack(Assembly assembly)
    {
        var predicate = Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(ForbiddenPersistenceTerms[0]);

        for (int i = 1; i < ForbiddenPersistenceTerms.Length; i++)
        {
            predicate = predicate
                .And()
                .NotHaveDependencyOn(ForbiddenPersistenceTerms[i]);
        }

        return predicate;
    }
}
