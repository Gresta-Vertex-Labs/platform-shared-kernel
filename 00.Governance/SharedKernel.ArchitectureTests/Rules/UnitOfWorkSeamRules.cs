using System.Reflection;
using Mono.Cecil;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Local-seam interface distinctness guard
/// </summary>
/// <remarks>
/// <para>
/// Asserts that <c>SharedKernel.Application.Behaviors.IUnitOfWork</c> and
/// <c>SharedKernel.Persistence.Abstractions.IUnitOfWork</c> remain two genuinely independent
/// interface declarations — never merged into a single type and never one inheriting the other.
/// </para>
/// <para>
/// <strong>Rationale (negative-space / regression-guard rule):</strong>
/// The local-seam pattern (05.Application declares its own <c>IUnitOfWork</c>, bridged to
/// 06.Persistence's <c>IUnitOfWork</c> at the composition root — the same pattern already
/// proven for <c>IRequestContext</c> and <c>IRequestIdempotencyStore</c>) only holds if the
/// two interfaces stay genuinely independent. A future "simplification" that merges them or
/// makes one inherit the other would silently reintroduce the <c>05.Application</c> →
/// <c>06.Persistence</c> layering violation the local-seam pattern exists to prevent. Both
/// interfaces are independently declared today (the desired state); the fire-path test
/// fixtures are therefore contrived assemblies proving the predicate would catch a future
/// merge attempt.
/// </para>
/// <para>
/// This class contains no new SK diagnostic ID — it is a pure structural
/// <c>ICustomRule</c> check, following the
/// <c>RedisTopologyRules</c> / <c>CompositionRootExclusivityRules</c> /
/// <c>PresentationLayeringRules</c> precedent of SK-less rules for boundary/shape
/// prohibitions. No Roslyn analyzer is required — the check is post-compile, at the
/// NetArchTest / Mono.Cecil layer.
/// </para>
/// <para>
/// Reuses the existing <c>Mono.Cecil &gt;= 0.11.5</c> reference already present in
/// <c>SharedKernel.ArchitectureTests</c> — zero new NuGet dependencies.
/// Lives in <c>SharedKernel.ArchitectureTests/Rules/UnitOfWorkSeamRules.cs</c>.
/// Reference this class with <c>PrivateAssets="all"</c> so it never becomes a transitive
/// production dependency.
/// </para>
/// </remarks>
public static class UnitOfWorkSeamRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that the two declared
    /// <c>IUnitOfWork</c> interfaces — one in the application-behaviors assembly and one in
    /// the persistence-abstractions assembly — are never merged or made to inherit each other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Three independent checks performed by
    /// <see cref="UnitOfWorkInterfacesRemainDistinctPredicate"/>:</strong>
    /// <list type="number">
    ///   <item><description>
    ///     <strong>Existence:</strong> both <c>IUnitOfWork</c> types must be resolvable by
    ///     their exact full names in the respective supplied assemblies. A missing type
    ///     (renamed or removed) is treated as a violation requiring governance review, not a
    ///     silent pass.
    ///   </description></item>
    ///   <item><description>
    ///     <strong>Identity collapse:</strong> the two resolved
    ///     <see cref="TypeDefinition"/> instances must not be reference-equal — catches an
    ///     accidental type-forwarding/alias merge.
    ///   </description></item>
    ///   <item><description>
    ///     <strong>Bidirectional base-interface check:</strong> neither interface's
    ///     <see cref="TypeDefinition.Interfaces"/> collection may contain an entry whose
    ///     <c>InterfaceType.FullName</c> equals the other's full name — catches
    ///     <c>interface IUnitOfWork : {other}.IUnitOfWork</c> being introduced on either side.
    ///   </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>
    /// // Inside SharedKernel.Application.Behaviors:
    /// interface IUnitOfWork : SharedKernel.Persistence.Abstractions.IUnitOfWork { }
    /// </code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// Two independently-declared <c>IUnitOfWork</c> interfaces, bridged only by a concrete
    /// adapter (e.g. <c>EfUnitOfWork</c> implementing both) at the composition root — never
    /// by interface inheritance between the two abstractions themselves.
    /// </para>
    /// </remarks>
    /// <param name="applicationBehaviorsAssembly">
    /// The assembly that should contain
    /// <c>SharedKernel.Application.Behaviors.IUnitOfWork</c>.
    /// Supply via <c>typeof(SomeApplicationBehaviorsType).Assembly</c>.
    /// </param>
    /// <param name="persistenceAbstractionsAssembly">
    /// The assembly that should contain
    /// <c>SharedKernel.Persistence.Abstractions.IUnitOfWork</c>.
    /// Supply via <c>typeof(SomePersistenceAbstractionsType).Assembly</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting the two <c>IUnitOfWork</c> interfaces remain
    /// structurally distinct and mutually non-inheriting.
    /// </returns>
    public static ConditionList UnitOfWorkInterfacesRemainDistinct(
        Assembly applicationBehaviorsAssembly,
        Assembly persistenceAbstractionsAssembly)
    {
        var appModule = ModuleDefinition.ReadModule(applicationBehaviorsAssembly.Location);
        var persistenceModule = ModuleDefinition.ReadModule(persistenceAbstractionsAssembly.Location);

        var predicate = new UnitOfWorkInterfacesRemainDistinctPredicate(appModule, persistenceModule);

        // The predicate is a structural invariant across both assemblies. We run the rule
        // against all types in the application-behaviors assembly; the predicate caches its
        // result on first evaluation and returns the same value for every subsequent call.
        // NetArchTest requires at least one type for MeetCustomRule to invoke the predicate —
        // if the assembly is empty the check is vacuously true, which is acceptable because
        // an empty assembly cannot contain IUnitOfWork.
        return Types
            .InAssembly(applicationBehaviorsAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(predicate);
    }
}
