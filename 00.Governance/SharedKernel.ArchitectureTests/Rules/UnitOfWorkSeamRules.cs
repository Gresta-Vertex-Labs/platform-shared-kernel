using System.Reflection;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Single-declaration guard for the contracts the application pipeline and the persistence layer
/// share (P-558).
/// </summary>
/// <remarks>
/// <para>
/// <c>IUnitOfWork</c>, <c>IRequestContext</c> and <c>IAuditTrailWriter</c> are declared exactly once,
/// in <c>SharedKernel.Application.Abstractions</c>; <c>SharedKernel.Application.Behaviors</c>
/// consumes them and <c>SharedKernel.Persistence.EfCore</c> implements them directly. This replaces
/// the former <c>UnitOfWorkInterfacesRemainDistinct</c> rule, which guarded the opposite design — two
/// same-named interfaces in 05 and 06, bridged by composition-root adapters in 13 — that P-558
/// deliberately removed.
/// </para>
/// <para>
/// A regression would be any other assembly declaring its own copy again (a second
/// <c>IUnitOfWork</c>, a transactional variant, a persistence-local actor/tenant seam), which would
/// bring back the adapters. This rule fails when an assembly other than
/// <c>SharedKernel.Application.Abstractions</c> declares an interface with one of those names.
/// </para>
/// </remarks>
public static class UnitOfWorkSeamRules
{
    /// <summary>
    /// The interface names that may be declared only in <c>SharedKernel.Application.Abstractions</c>,
    /// including the retired duplicates they replaced.
    /// </summary>
    public const string SharedContractNamePattern =
        "^(IUnitOfWork|ITransactionalUnitOfWork|IPersistenceTransaction|IRequestContext|IAuditTrailWriter|ICurrentActorContext|ICurrentTenantContext)$";

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting <paramref name="assembly"/> declares no
    /// interface named like one of the shared pipeline/persistence contracts.
    /// </summary>
    /// <param name="assembly">
    /// Any assembly other than <c>SharedKernel.Application.Abstractions</c> — typically
    /// <c>SharedKernel.Application</c>, <c>.Behaviors</c> and every <c>SharedKernel.Persistence.*</c>
    /// assembly.
    /// </param>
    /// <returns>A <see cref="ConditionList"/> over the assembly's interfaces.</returns>
    public static ConditionList SharedContractsAreNotRedeclared(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .AreInterfaces()
            .Should()
            .NotHaveNameMatching(SharedContractNamePattern);
}
