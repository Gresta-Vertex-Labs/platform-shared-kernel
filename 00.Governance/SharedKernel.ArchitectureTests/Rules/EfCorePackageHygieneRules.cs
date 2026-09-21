using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that close the three regression vectors introduced by
/// no concrete downcast of <c>ISpecificationEvaluator&lt;T&gt;</c>, all
/// <c>IUnitOfWork</c> implementors must have exactly one public constructor, and the
/// application layer must never reference <c>IDbContextTransaction</c> directly.
/// </summary>
/// <remarks>
/// <para>
/// All three factory methods accept an <see cref="Assembly"/> parameter and return a
/// <see cref="ConditionList"/> — consistent with the established <c>ArchitectureRuleBase</c> API.
/// </para>
/// <para>
/// These rules are additive enforcement gates that prevent three specific regression patterns
/// that were diagnosed and fixed once already. Without build-time enforcement, any future
/// refactor can silently reintroduce the anti-pattern.
/// </para>
/// <list type="bullet">
///   <item><description>
///     Rule 1 — <see cref="NoSpecificationEvaluatorDowncastInEfCoreAssembly"/>: prevents
///     re-introduction of the <c>(SpecificationEvaluator&lt;T&gt;)evaluator</c> downcast
///     that was eliminated when <c>GetProjectedQuery</c> was added to the interface.
///   </description></item>
///   <item><description>
///     Rule 2 — <see cref="IUnitOfWorkImplementorsMustHaveExactlyOneConstructor"/>: prevents
///     re-introduction of a second constructor on <c>EfUnitOfWork</c> that caused DI ambiguity
///     (a regression this rule has already caught once).
///   </description></item>
///   <item><description>
///     Rule 3 — <see cref="ApplicationLayerMustNotReferenceDbContextTransaction"/>: enforces
///     that <c>IUnitOfWork.ExecuteInTransactionAsync</c> is the sole transaction entry point in the
///     application layer; direct <c>IDbContextTransaction</c> coupling is prohibited.
///   </description></item>
///   <item><description>
///     Rule 4 — <see cref="NoDirectEfPropertyUsageInEfCoreAssembly"/>: prevents
///     re-introduction of a direct, client-side-evaluated <c>EF.Property&lt;T&gt;</c> call —
///     a defect class this codebase has hit more than once.
///   </description></item>
/// </list>
/// <para>
/// Reference this class with <c>PrivateAssets="all"</c> so it never becomes a transitive
/// production dependency.
/// </para>
/// </remarks>
public static class EfCorePackageHygieneRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied assembly
    /// performs a <c>castclass</c> IL instruction whose target type name starts with
    /// <c>"SpecificationEvaluator"</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This rule must be called with the <c>SharedKernel.Persistence.EfCore</c> assembly.
    /// The <c>castclass</c> check is a simple operand name prefix match — no semantic model
    /// or type-hierarchy walk is required. The rule fires on any cast whose target
    /// <c>TypeReference.Name</c> starts with <c>"SpecificationEvaluator"</c>, covering both
    /// the generic form and any subclass forms in IL.
    /// </para>
    /// <para>
    /// <c>GetProjectedQuery</c> was added to <c>ISpecificationEvaluator&lt;T&gt;</c> to
    /// eliminate the concrete downcast. Without this rule, a future refactor could silently
    /// re-introduce the <c>(SpecificationEvaluator&lt;T&gt;)evaluator</c> pattern, bypassing
    /// the abstraction and preventing interface substitution.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>var concreteEval = (SpecificationEvaluator&lt;T&gt;)_evaluator;</code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>_evaluator.GetProjectedQuery(query, spec);</code>
    /// </para>
    /// </remarks>
    /// <param name="assembly">
    /// The assembly to evaluate — typically <c>SharedKernel.Persistence.EfCore</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no type performs a concrete
    /// <c>SpecificationEvaluator</c> downcast.
    /// </returns>
    public static ConditionList NoSpecificationEvaluatorDowncastInEfCoreAssembly(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoSpecificationEvaluatorDowncastPredicate());

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that every type implementing
    /// <c>IUnitOfWork</c> in the supplied assembly has exactly one public instance constructor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>EfUnitOfWork</c> was reduced to a single constructor to resolve DI ambiguity
    /// caused by two competing registrations. A second "convenience constructor" would silently
    /// reintroduce the ambiguity, causing runtime DI resolution failures that only surface
    /// under specific DI configuration scenarios.
    /// </para>
    /// <para>
    /// The <see cref="SingleConstructorPredicate"/> self-scopes to <c>IUnitOfWork</c>
    /// implementors only — non-implementor types always pass. Passing an unrelated assembly
    /// produces zero matches and the rule trivially passes without masking real violations,
    /// provided the correct persistence assembly is also passed.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>
    /// class EfUnitOfWork : IUnitOfWork {
    ///     public EfUnitOfWork(AppDbContext ctx) { }
    ///     public EfUnitOfWork() { }  // second constructor — DI ambiguity regression
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
    /// <param name="assembly">
    /// The assembly to evaluate — typically the persistence assembly containing concrete
    /// <c>IUnitOfWork</c> implementors (e.g., <c>SharedKernel.Persistence.EfCore</c>).
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting all <c>IUnitOfWork</c> implementors have
    /// exactly one public instance constructor.
    /// </returns>
    public static ConditionList IUnitOfWorkImplementorsMustHaveExactlyOneConstructor(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new SingleConstructorPredicate());

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied assembly
    /// references <c>IDbContextTransaction</c> via a field type, constructor parameter, or
    /// method call operand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This rule must be called with the <c>05.Application</c> assembly.
    /// <c>IUnitOfWork.ExecuteInTransactionAsync</c> is the only permitted transaction entry point
    /// for application handlers. Direct injection of <c>IDbContextTransaction</c> couples
    /// application code to EF Core's specific transaction implementation, making the
    /// transaction abstraction boundary unenforceable.
    /// </para>
    /// <para>
    /// The namespace exemption inside <see cref="NoDbContextTransactionInApplicationPredicate"/>
    /// passes all types in <c>SharedKernel.Persistence.*</c> unconditionally — the persistence
    /// layer itself may use <c>IDbContextTransaction</c> internally. Passing persistence
    /// assemblies to this method is redundant; the exemption is a safety net, not the primary
    /// enforcement mechanism.
    /// </para>
    /// <para>
    /// The <c>"IDbContextTransaction"</c> substring check covers the full interface name
    /// including namespace in the <c>FullName</c> property, ensuring <c>BeginTransactionAsync</c>
    /// return types and <c>IDbContextTransaction</c>-typed fields are both detected.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>
    /// class CreateOrderHandler {
    ///     public CreateOrderHandler(IDbContextTransaction tx) { }
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>
    /// class CreateOrderHandler {
    ///     public CreateOrderHandler(IUnitOfWork unitOfWork) { }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="assembly">
    /// The assembly to evaluate — typically the <c>05.Application</c> assembly.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no type references <c>IDbContextTransaction</c>
    /// outside the permitted persistence namespace exemption.
    /// </returns>
    public static ConditionList ApplicationLayerMustNotReferenceDbContextTransaction(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoDbContextTransactionInApplicationPredicate());

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no method body in the supplied
    /// assembly contains a direct <c>Call</c>/<c>Callvirt</c> IL instruction targeting
    /// <c>Microsoft.EntityFrameworkCore.EF.Property&lt;TProperty&gt;</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This rule must be called with the <c>SharedKernel.Persistence.EfCore</c> assembly.
    /// <c>EF.Property&lt;TProperty&gt;(object entity, string propertyName)</c> called directly
    /// in ordinary executable code forces client-side evaluation of the surrounding query — the
    /// exact defect class already fixed twice in this codebase — once in a read repository's
    /// <c>GetByIdsAsync</c>, once in a tenanted repository's <c>GetByIdForTenantAsync*</c>
    /// methods.
    /// </para>
    /// <para>
    /// See <see cref="NoDirectEfPropertyUsagePredicate"/> for the full structural
    /// self-exemption rationale — the platform's one legitimate <c>EF.Property&lt;T&gt;</c>
    /// pattern (inside a <c>HasQueryFilter(Expression&lt;Func&lt;TEntity,bool&gt;&gt; filter)</c>
    /// global query filter) is excluded automatically because the C# compiler never emits a
    /// <c>Call</c>/<c>Callvirt</c> opcode against <c>EF.Property</c> when the call is lowered
    /// into an expression tree. This rule carries NO exemption mechanism — no namespace guard,
    /// no allow-list registry — mirroring <see cref="NoSpecificationEvaluatorDowncastInEfCoreAssembly"/>'s
    /// own zero-exemption precedent exactly.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>
    /// return dbSet.AsEnumerable()
    ///     .FirstOrDefault(e => EF.Property&lt;TId&gt;(e, "Id").Equals(id));  // client-side evaluation
    /// </code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>
    /// var param = Expression.Parameter(typeof(T), "e");
    /// var idProperty = Expression.Property(param, "Id");
    /// var idConstant = Expression.Constant(id, typeof(TId));
    /// var equals = Expression.Equal(idProperty, idConstant);
    /// return Expression.Lambda&lt;Func&lt;T, bool&gt;&gt;(equals, param);
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="assembly">
    /// The assembly to evaluate — typically <c>SharedKernel.Persistence.EfCore</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no method body directly calls
    /// <c>EF.Property&lt;T&gt;</c>.
    /// </returns>
    public static ConditionList NoDirectEfPropertyUsageInEfCoreAssembly(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoDirectEfPropertyUsagePredicate());
}
